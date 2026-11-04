import { mkdir, open, readFile } from "node:fs/promises";
import path from "node:path";
import { Pool, type PoolConfig } from "pg";

export type ContactLead = {
  id: string;
  name: string;
  email: string;
  subject: string | null;
  message: string;
  status: "new";
  createdAt: string;
  source: "website";
};

export interface LeadRepository { save(lead: ContactLead): Promise<void> }
export class ContactStorageUnavailable extends Error {}
export class ContactSubmissionConflict extends Error {}

export function createCoffaLeadRepository(apiUrl: string, apiKey: string, fetcher: typeof fetch = fetch, timeoutMs = 8000): LeadRepository {
  let endpoint: string;
  try {
    const url = new URL(apiUrl);
    if (!/^https?:\/\/[^/?#]+\/?$/i.test(apiUrl) || /[\s\\]/.test(apiUrl) || !["http:", "https:"].includes(url.protocol)
      || url.username || url.password || url.pathname !== "/" || url.search || url.hash || !url.hostname
      || !apiKey || /[\r\n]/.test(apiKey)) throw new Error();
    endpoint = new URL("/api/public/contact", url.origin).toString();
  } catch {
    throw new ContactStorageUnavailable("Coffa contact API configuration is invalid.");
  }
  return { async save(lead) {
    const controller = new AbortController();
    const timeout = setTimeout(() => controller.abort(), Math.min(10000, Math.max(1, timeoutMs)));
    try {
      const response = await fetcher(endpoint, {
        method: "POST", cache: "no-store", redirect: "error", signal: controller.signal,
        headers: { "Content-Type": "application/json", "X-Coffa-Contact-Key": apiKey },
        body: JSON.stringify({ id: lead.id, name: lead.name, email: lead.email, subject: lead.subject, message: lead.message, createdAt: lead.createdAt, source: lead.source }),
      });
      if (response.status === 409) throw new ContactSubmissionConflict();
      if (response.status !== 200) throw new ContactStorageUnavailable();
      const body: unknown = await response.json();
      if (!body || typeof body !== "object" || !("ok" in body) || body.ok !== true
        || !("leadId" in body) || typeof body.leadId !== "string" || body.leadId.toLowerCase() !== lead.id.toLowerCase()) throw new ContactStorageUnavailable();
    } catch (error) {
      if (error instanceof ContactSubmissionConflict) throw error;
      // Neither configuration secrets nor provider response bodies leave this boundary.
      throw new ContactStorageUnavailable("Coffa contact API is unavailable.");
    } finally { clearTimeout(timeout); }
  } };
}

function sameSubmission(left: ContactLead, right: ContactLead) {
  return left.id === right.id && left.name === right.name && left.email === right.email
    && left.subject === right.subject && left.message === right.message && left.source === right.source;
}

// One queue per development file also serializes separate repository instances.
const fileQueues = new Map<string, Promise<void>>();

export function createFileLeadRepository(filePath: string): LeadRepository {
  const absolutePath = path.resolve(filePath);
  return { async save(lead) {
    const previous = fileQueues.get(absolutePath) ?? Promise.resolve();
    const operation = previous.catch(() => {}).then(async () => {
      let records: ContactLead[] = [];
      try {
        const content = await readFile(absolutePath, "utf8");
        records = content.split("\n").filter(Boolean).map(line => JSON.parse(line) as ContactLead);
      } catch (error) {
        if (!(error instanceof Error && "code" in error && error.code === "ENOENT")) throw error;
      }
      const existing = records.find(record => record.id === lead.id);
      if (existing) {
        if (!sameSubmission(existing, lead)) throw new ContactSubmissionConflict();
        return;
      }
      await mkdir(path.dirname(absolutePath), { recursive: true, mode: 0o700 });
      const file = await open(absolutePath, "a", 0o600);
      try {
        await file.chmod(0o600);
        await file.appendFile(`${JSON.stringify(lead)}\n`, "utf8");
        await file.sync();
      } finally {
        await file.close();
      }
    });
    fileQueues.set(absolutePath, operation);
    try { await operation; } finally {
      if (fileQueues.get(absolutePath) === operation) fileQueues.delete(absolutePath);
    }
  } };
}

type Database = Pick<Pool, "query">;

export function createPostgresLeadRepository(database: Database): LeadRepository {
  return { async save(lead) {
    const values = [lead.id, lead.name, lead.email, lead.subject, lead.message, lead.createdAt, lead.source];
    const inserted = await database.query(
      `INSERT INTO contact_leads (id, name, email, subject, message, created_at, source)
       VALUES ($1, $2, $3, $4, $5, $6, $7)
       ON CONFLICT (id) DO NOTHING RETURNING id`, values,
    );
    if (inserted.rowCount) return;
    // A retry is accepted only when the original submission has the same content.
    const existing = await database.query(
      `SELECT id FROM contact_leads WHERE id = $1 AND name = $2 AND email = $3
       AND subject IS NOT DISTINCT FROM $4 AND message = $5 AND source = $6`,
      [lead.id, lead.name, lead.email, lead.subject, lead.message, lead.source],
    );
    if (!existing.rowCount) throw new ContactSubmissionConflict();
  } };
}

let configuredRepository: { key: string; repository: LeadRepository } | undefined;

export function getContactRepository(env: Record<string, string | undefined> = process.env, fetcher: typeof fetch = fetch): LeadRepository {
  const apiUrl = env.COFFA_API_URL?.trim();
  const apiKey = env.COFFA_CONTACT_KEY?.trim();
  if (apiUrl || apiKey) {
    if (!apiUrl || !apiKey) throw new ContactStorageUnavailable("Coffa contact API configuration is incomplete.");
    return createCoffaLeadRepository(apiUrl, apiKey, fetcher);
  }
  const connectionString = env.DATABASE_URL?.trim();
  if (connectionString) {
    if (configuredRepository?.key === connectionString) return configuredRepository.repository;
    const config: PoolConfig = { connectionString, max: 3, connectionTimeoutMillis: 5000, idleTimeoutMillis: 10000, statement_timeout: 8000, application_name: "davidcoffa-contact" };
    const pool = new Pool(config);
    // Do not log connection strings, query parameters or visitor information.
    pool.on("error", () => console.error("contact_database_connection_error"));
    const repository = createPostgresLeadRepository(pool);
    configuredRepository = { key: connectionString, repository };
    return repository;
  }
  if (env.NODE_ENV === "development" || env.NODE_ENV === "test") {
    return createFileLeadRepository(path.join(process.cwd(), ".data", "contact-leads.jsonl"));
  }
  throw new ContactStorageUnavailable("Contact storage is not configured.");
}
