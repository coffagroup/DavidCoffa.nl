import { createHash, randomBytes } from "node:crypto";
import { validateContact } from "./contact-validation";
import { ContactStorageUnavailable, ContactSubmissionConflict, type LeadRepository } from "./contact-repository";

const MAX_BODY_BYTES = 24 * 1024;
const WINDOW_MS = 15 * 60 * 1000;
const UUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;
const HOST = /^(?:\[[0-9a-f:.]+\]|[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?(?:\.[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?)*\.?)(?::[0-9]{1,5})?$/i;

function expectedRequestOrigin(request: Request, allowedOrigin?: string) {
  if (allowedOrigin) {
    const configured = new URL(allowedOrigin);
    if (!["http:", "https:"].includes(configured.protocol) || configured.username || configured.password || configured.pathname !== "/" || configured.search || configured.hash) throw new Error("Invalid configured origin");
    return configured.origin;
  }
  const url = new URL(request.url);
  if (!["http:", "https:"].includes(url.protocol)) throw new Error("Invalid protocol");
  const host = request.headers.get("host");
  if (!host) return url.origin;
  if (!HOST.test(host)) throw new Error("Invalid host");
  // Next can normalize request.url to localhost even when the browser used the
  // loopback IP. Host retains the browser's destination; forwarded values are
  // deliberately ignored. A TLS-terminating proxy can use the explicit config.
  return new URL(`${url.protocol}//${host}`).origin;
}

function json(body: object, status = 200, headers: Record<string, string> = {}) {
  return Response.json(body, { status, headers: { "Cache-Control": "no-store", ...headers } });
}

export function createContactRateLimiter(now = Date.now) {
  const salt = randomBytes(16).toString("hex");
  const buckets = new Map<string, { count: number; resetAt: number }>();
  return (email: string) => {
    const timestamp = now();
    for (const [key, bucket] of buckets) if (bucket.resetAt <= timestamp) buckets.delete(key);
    const emailKey = createHash("sha256").update(`${salt}:${email.toLowerCase()}`).digest("hex");
    const keys = ["total", emailKey];
    const limits = [100, 5];
    for (let index = 0; index < keys.length; index++) {
      const bucket = buckets.get(keys[index]);
      if (bucket && bucket.count >= limits[index]) return Math.max(1, Math.ceil((bucket.resetAt - timestamp) / 1000));
    }
    keys.forEach(key => {
      const bucket = buckets.get(key) ?? { count: 0, resetAt: timestamp + WINDOW_MS };
      bucket.count++;
      buckets.set(key, bucket);
    });
    return 0;
  };
}

async function readBody(request: Request) {
  const contentLength = request.headers.get("content-length");
  if (contentLength && Number(contentLength) > MAX_BODY_BYTES) throw new RangeError();
  if (!request.body) throw new SyntaxError();
  const reader = request.body.getReader();
  let size = 0;
  const chunks: Uint8Array[] = [];
  try {
    while (true) {
      const { done, value } = await reader.read();
      if (done) break;
      size += value.byteLength;
      if (size > MAX_BODY_BYTES) { await reader.cancel(); throw new RangeError(); }
      chunks.push(value);
    }
  } finally { reader.releaseLock(); }
  const buffer = new Uint8Array(size);
  let offset = 0;
  for (const chunk of chunks) { buffer.set(chunk, offset); offset += chunk.length; }
  return JSON.parse(new TextDecoder("utf-8", { fatal: true }).decode(buffer));
}

export function createContactHandler({ repository, rateLimit = createContactRateLimiter(), allowedOrigin, now = () => new Date() }: {
  repository: () => LeadRepository;
  rateLimit?: (email: string) => number;
  allowedOrigin?: string;
  now?: () => Date;
}) {
  return async (request: Request) => {
    let expectedOrigin: string;
    try { expectedOrigin = expectedRequestOrigin(request, allowedOrigin); } catch { return json({ ok: false, message: "Versturen lukt op dit moment niet. Mail me rechtstreeks." }, 503); }
    if (request.headers.get("origin") !== expectedOrigin) return json({ ok: false, message: "Vernieuw de pagina en probeer het opnieuw." }, 403);
    if (request.headers.get("content-type")?.split(";")[0].trim().toLowerCase() !== "application/json") return json({ ok: false, message: "Dit bericht kon niet worden verwerkt." }, 415);
    let input: unknown;
    try { input = await readBody(request); } catch (error) {
      return json({ ok: false, message: error instanceof RangeError ? "Je bericht is te groot. Gebruik maximaal 4000 tekens voor je vraag." : "Dit bericht kon niet worden gelezen. Probeer het opnieuw." }, error instanceof RangeError ? 413 : 400);
    }
    const result = validateContact(input);
    if (!result.data) return json({ ok: false, message: "Controleer de gemarkeerde velden.", errors: result.errors }, 400);
    const submissionId = (input as Record<string, unknown>).submissionId;
    if (typeof submissionId !== "string" || !UUID.test(submissionId)) return json({ ok: false, message: "Vernieuw de pagina en probeer het opnieuw." }, 400);
    const retryAfter = rateLimit(result.data.email);
    if (retryAfter) return json({ ok: false, message: "Er zijn kort achter elkaar meerdere berichten verstuurd. Probeer het later opnieuw of mail me rechtstreeks." }, 429, { "Retry-After": String(retryAfter) });
    try {
      await repository().save({
        id: submissionId.toLowerCase(), name: result.data.name, email: result.data.email,
        subject: result.data.subject || null, message: result.data.message,
        status: "new", createdAt: now().toISOString(), source: "website",
      });
      return json({ ok: true });
    } catch (error) {
      if (error instanceof ContactSubmissionConflict) return json({ ok: false, message: "Dit bericht is gewijzigd tijdens het versturen. Vernieuw de pagina en probeer het opnieuw." }, 409);
      if (!(error instanceof ContactStorageUnavailable)) console.error("contact_submission_storage_error");
      return json({ ok: false, message: "Versturen lukt op dit moment niet. Je bericht is niet bevestigd. Probeer het opnieuw of mail me rechtstreeks via info@davidcoffa.nl." }, 503);
    }
  };
}
