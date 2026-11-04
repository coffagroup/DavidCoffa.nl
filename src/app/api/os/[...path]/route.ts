import type { NextRequest } from "next/server";

export const runtime = "nodejs";
export const dynamic = "force-dynamic";
const MAX_BYTES = 21 * 1024 * 1024;
const METHODS = new Set(["GET", "POST", "PUT", "PATCH", "DELETE"]);
const error = (status: number, message: string) => Response.json({ message }, { status, headers: { "Cache-Control": "no-store" } });

async function readLimitedBody(request: Request) {
  if (Number(request.headers.get("content-length")) > MAX_BYTES) throw new RangeError();
  if (!request.body) return undefined;
  const reader = request.body.getReader();
  const chunks: Uint8Array[] = [];
  let length = 0;
  try {
    while (true) {
      const { value, done } = await reader.read();
      if (done) break;
      length += value.byteLength;
      if (length > MAX_BYTES) { await reader.cancel(); throw new RangeError(); }
      chunks.push(value);
    }
  } finally { reader.releaseLock(); }
  const bytes = new Uint8Array(length);
  let offset = 0;
  for (const chunk of chunks) { bytes.set(chunk, offset); offset += chunk.byteLength; }
  return bytes;
}

async function proxy(request: NextRequest, context: { params: Promise<{ path: string[] }> }) {
  if (!METHODS.has(request.method)) return error(405, "Deze actie is niet beschikbaar.");
  const configured = process.env.COFFA_API_URL;
  if (!configured) return error(503, "Coffa OS is nog niet verbonden met de API. Stel COFFA_API_URL in op de server.");
  const { path } = await context.params;
  if (path.some(part => !/^[a-zA-Z0-9_-][a-zA-Z0-9._-]*$/.test(part) || part === "." || part === "..")) return error(400, "Ongeldige route.");
  const unsafe = !["GET", "HEAD"].includes(request.method);
  if (unsafe) {
    // Browser-controlled Origin must match the actual destination Host. No
    // forwarded headers or client-provided API destination are trusted.
    const origin = request.headers.get("origin");
    const host = request.headers.get("host");
    try {
      if (!origin || !host || new URL(origin).host !== host || !["http:", "https:"].includes(new URL(origin).protocol)) return error(403, "Vernieuw de pagina en probeer opnieuw.");
    } catch { return error(403, "Ongeldige aanvraag."); }
  }
  try {
    const base = new URL(configured);
    if (!["http:", "https:"].includes(base.protocol) || base.username || base.password) return error(503, "De API-configuratie is ongeldig.");
    const target = new URL(`/api/${path.map(encodeURIComponent).join("/")}`, base.origin);
    target.search = request.nextUrl.search;
    const headers = new Headers();
    for (const key of ["cookie", "content-type", "accept", "x-csrf-token", "idempotency-key"]) {
      const value = request.headers.get(key);
      if (value) headers.set(key, value);
    }
    const response = await fetch(target, {
      method: request.method, headers,
      body: unsafe ? await readLimitedBody(request) : undefined,
      redirect: "manual", cache: "no-store", signal: AbortSignal.timeout(60000),
    });
    const outgoing = new Headers({ "Cache-Control": "private, no-store", "X-Content-Type-Options": "nosniff" });
    for (const key of ["content-type", "content-disposition", "retry-after"]) {
      const value = response.headers.get(key);
      if (value) outgoing.set(key, value);
    }
    for (const cookie of response.headers.getSetCookie()) outgoing.append("set-cookie", cookie);
    // Never forward redirects to the private API host.
    if (response.status >= 300 && response.status < 400) return error(502, "De API gaf een onverwachte doorverwijzing.");
    return new Response(response.body, { status: response.status, headers: outgoing });
  } catch (exception) {
    if (exception instanceof RangeError) return error(413, "Het bestand is te groot. Gebruik maximaal 20 MB.");
    console.error("coffa_api_unavailable");
    return error(503, "Coffa OS is tijdelijk niet bereikbaar. Je gegevens zijn niet als opgeslagen bevestigd.");
  }
}
export const GET = proxy;
export const POST = proxy;
export const PUT = proxy;
export const PATCH = proxy;
export const DELETE = proxy;
