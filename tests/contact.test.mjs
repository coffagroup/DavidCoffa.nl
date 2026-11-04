import assert from "node:assert/strict";
import { execFileSync } from "node:child_process";
import { mkdtemp, mkdir, readFile, rm, stat } from "node:fs/promises";
import { tmpdir } from "node:os";
import path from "node:path";
import { createRequire } from "node:module";
import { after, test } from "node:test";
import { randomUUID } from "node:crypto";

// Compile only the server modules under test. Output stays inside the ignored
// .next directory so normal Node module resolution can find the installed pg.
await mkdir(".next", { recursive: true });
const buildDirectory = await mkdtemp(path.resolve(".next/contact-tests-"));
after(() => rm(buildDirectory, { recursive: true, force: true }));
execFileSync(process.execPath, ["node_modules/typescript/bin/tsc", "src/lib/contact-validation.ts", "src/lib/contact-repository.ts", "src/lib/contact-handler.ts", "--outDir", buildDirectory, "--module", "commonjs", "--moduleResolution", "node", "--target", "ES2022", "--skipLibCheck", "--esModuleInterop", "--strict"], { stdio: "pipe" });
const require = createRequire(import.meta.url);
const { createContactHandler, createContactRateLimiter } = require(path.join(buildDirectory, "contact-handler.js"));
const { createFileLeadRepository, createPostgresLeadRepository, getContactRepository, ContactStorageUnavailable, ContactSubmissionConflict } = require(path.join(buildDirectory, "contact-repository.js"));

const origin = "https://davidcoffa.nl";
function payload(overrides = {}) {
  return { name: "Test ondernemer", email: "test@example.com", message: "Ik wil terugkerend werk vereenvoudigen.", subject: "Automatisering", website: "", submissionId: randomUUID(), ...overrides };
}
function request(body = payload(), options = {}) {
  return new Request(`${origin}/api/contact`, {
    method: "POST", headers: { origin, "content-type": "application/json", ...options.headers },
    body: typeof body === "string" ? body : JSON.stringify(body),
  });
}
function handlerWith(save = async () => {}, extra = {}) {
  return createContactHandler({ repository: () => ({ save }), ...extra });
}

test("confirms receipt only after storage completes, with server-owned status and source", async () => {
  const records = [];
  let finish;
  const pendingStorage = new Promise(resolve => { finish = resolve; });
  const handler = handlerWith(async lead => { await pendingStorage; records.push(lead); }, { now: () => new Date("2026-11-04T10:00:00Z") });
  let responded = false;
  const responsePromise = handler(request(payload({ name: "  Test ondernemer  ", source: "forged", status: "closed" }))).then(response => { responded = true; return response; });
  await new Promise(resolve => setTimeout(resolve, 10));
  assert.equal(responded, false);
  finish();
  const response = await responsePromise;
  assert.equal(response.status, 200);
  assert.deepEqual(await response.json(), { ok: true });
  assert.equal(records[0].name, "Test ondernemer");
  assert.equal(records[0].source, "website");
  assert.equal(records[0].status, "new");
  assert.equal(records[0].createdAt, "2026-11-04T10:00:00.000Z");
  assert.equal("website" in records[0], false);
});

test("rejects invalid or excessive inputs, malformed JSON and populated honeypots before saving", async () => {
  let saves = 0;
  const handler = handlerWith(async () => { saves++; });
  for (const value of [null, [], payload({ name: "" }), payload({ email: "invalid" }), payload({ message: "kort" }), payload({ message: "a".repeat(4001) }), payload({ subject: {} }), payload({ website: "https://spam.example" }), payload({ submissionId: "invalid" })]) {
    assert.equal((await handler(request(value))).status, 400);
  }
  assert.equal((await handler(request("{invalid"))).status, 400);
  assert.equal((await handler(request(payload({ message: "🦊".repeat(10000) })))).status, 413);
  assert.equal(saves, 0);
});

test("checks origin and content type", async () => {
  let saves = 0;
  const handler = handlerWith(async () => { saves++; });
  assert.equal((await handler(request(payload(), { headers: { origin: "https://other.example" } }))).status, 403);
  const withoutOrigin = request();
  withoutOrigin.headers.delete("origin");
  assert.equal((await handler(withoutOrigin)).status, 403);
  assert.equal((await handler(request(payload(), { headers: { "content-type": "text/plain" } }))).status, 415);
  const proxyHandler = handlerWith(async () => { saves++; }, { allowedOrigin: "https://www.davidcoffa.nl" });
  assert.equal((await proxyHandler(request(payload(), { headers: { origin: "https://www.davidcoffa.nl" } }))).status, 200);
  assert.equal(saves, 1);
});

test("uses PostgreSQL-compatible Unicode character counts for message limits", async () => {
  let saves = 0;
  const handler = handlerWith(async () => { saves++; });
  assert.equal((await handler(request(payload({ message: "🦊".repeat(5) })))).status, 400);
  assert.equal((await handler(request(payload({ message: "🦊".repeat(10) })))).status, 200);
  assert.equal((await handler(request(payload({ message: "🦊".repeat(4000) })))).status, 200);
  assert.equal((await handler(request(payload({ message: "🦊".repeat(4001) })))).status, 400);
  assert.equal(saves, 2);
});

test("uses a validated actual Host when Next normalizes the request URL", async () => {
  let saves = 0;
  const handler = handlerWith(async () => { saves++; });
  const normalizedRequest = (requestOrigin, host = "127.0.0.1:3001", extraHeaders = {}) => new Request("http://localhost:3001/api/contact", {
    method: "POST", headers: { host, origin: requestOrigin, "content-type": "application/json", ...extraHeaders }, body: JSON.stringify(payload()),
  });
  assert.equal((await handler(normalizedRequest("http://127.0.0.1:3001"))).status, 200);
  assert.equal((await handler(normalizedRequest("http://other.example:3001"))).status, 403);
  assert.equal((await handler(normalizedRequest("http://127.0.0.1:3001", "127.0.0.1:3001", { "x-forwarded-host": "evil.example", "x-forwarded-proto": "https" }))).status, 200);
  assert.equal((await handler(normalizedRequest("https://evil.example", "127.0.0.1:3001", { "x-forwarded-host": "evil.example", "x-forwarded-proto": "https" }))).status, 403);
  for (const host of ["user@127.0.0.1:3001", "127.0.0.1:3001/path", "127.0.0.1:3001?query", "127.0.0.1:3001#fragment", "127.0.0.1:3001\\evil", "127.0.0.1:99999", "127.0.0.1:3001,evil.example"]) {
    assert.equal((await handler(normalizedRequest("http://127.0.0.1:3001", host))).status, 503);
  }
  assert.equal(saves, 2);
});

test("counts streamed bytes rather than trusting Content-Length", async () => {
  const body = new ReadableStream({ start(controller) { controller.enqueue(new TextEncoder().encode("x".repeat(25000))); controller.close(); } });
  const streamed = new Request(`${origin}/api/contact`, { method: "POST", headers: { origin, "content-type": "application/json", "content-length": "1" }, body, duplex: "half" });
  assert.equal((await handlerWith()(streamed)).status, 413);
});

test("storage failures and missing production config never report success", async () => {
  const unavailable = handlerWith(undefined, { repository: () => { throw new ContactStorageUnavailable(); } });
  assert.equal((await unavailable(request())).status, 503);
  const originalError = console.error;
  const logEntries = [];
  console.error = (...args) => logEntries.push(args);
  try {
    const response = await handlerWith(async () => { throw new Error("password=secret and private visitor data"); })(request());
    assert.equal(response.status, 503);
    assert.equal((await response.text()).includes("secret"), false);
    assert.deepEqual(logEntries, [["contact_submission_storage_error"]]);
  } finally { console.error = originalError; }
  assert.throws(() => getContactRepository({ NODE_ENV: "production" }), ContactStorageUnavailable);
  const unconfiguredProduction = handlerWith(undefined, { repository: () => getContactRepository({ NODE_ENV: "production" }) });
  const unconfiguredResponse = await unconfiguredProduction(request());
  assert.equal(unconfiguredResponse.status, 503);
  assert.equal((await unconfiguredResponse.json()).ok, false);
});

test("limits repeated submissions and allows them after the window", async () => {
  let now = 1000000;
  const handler = handlerWith(undefined, { rateLimit: createContactRateLimiter(() => now) });
  for (let index = 0; index < 5; index++) assert.equal((await handler(request())).status, 200);
  const response = await handler(request());
  assert.equal(response.status, 429);
  assert.equal(response.headers.get("retry-after"), "900");
  now += 15 * 60 * 1000;
  assert.equal((await handler(request())).status, 200);
});

test("also caps total submissions when email addresses keep changing", async () => {
  const limiter = createContactRateLimiter();
  for (let index = 0; index < 100; index++) assert.equal(limiter(`test-${index}@example.com`), 0);
  assert.ok(limiter("new-address@example.com") > 0);
});

test("file storage survives new repository instances and serializes concurrent writes and retries", async () => {
  const directory = await mkdtemp(path.join(tmpdir(), "coffa-contact-"));
  const file = path.join(directory, "private", "contact-leads.jsonl");
  try {
    const repository = createFileLeadRepository(file);
    const makeLead = () => ({ id: randomUUID(), name: "Test", email: "test@example.com", subject: null, message: "Een concrete testvraag.", status: "new", createdAt: new Date().toISOString(), source: "website" });
    const leads = Array.from({ length: 12 }, makeLead);
    await Promise.all([...leads.map(lead => repository.save(lead)), ...leads.map(lead => createFileLeadRepository(file).save({ ...lead, createdAt: new Date().toISOString() }))]);
    const stored = (await readFile(file, "utf8")).trim().split("\n").map(line => JSON.parse(line));
    assert.equal(stored.length, 12);
    assert.equal(new Set(stored.map(lead => lead.id)).size, 12);
    assert.equal((await stat(file)).mode & 0o777, 0o600);
    await assert.rejects(() => repository.save({ ...leads[0], message: "Andere vraag bij dezelfde UUID." }), ContactSubmissionConflict);
    assert.equal((await readFile(file, "utf8")).trim().split("\n").length, 12);
  } finally { await rm(directory, { recursive: true, force: true }); }
});

test("Postgres uses bound values and accepts only identical duplicate submissions", async () => {
  const calls = [];
  const results = [{ rowCount: 1 }, { rowCount: 0 }, { rowCount: 1 }, { rowCount: 0 }, { rowCount: 0 }];
  const repository = createPostgresLeadRepository({ async query(sql, values) { calls.push({ sql, values }); return results.shift(); } });
  const lead = { id: randomUUID(), name: "Robert'); DROP TABLE contact_leads;--", email: "test@example.com", subject: null, message: "Een concrete testvraag.", status: "new", createdAt: new Date().toISOString(), source: "website" };
  await repository.save(lead);
  assert.equal(calls[0].sql.includes(lead.name), false);
  assert.equal(calls[0].values[1], lead.name);
  await repository.save(lead);
  await assert.rejects(() => repository.save({ ...lead, message: "Een gewijzigde vraag met hetzelfde id." }), ContactSubmissionConflict);
});
