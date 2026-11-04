export class PortalError extends Error {
  constructor(message: string, public status: number, public errors?: Record<string, string[]>) { super(message); }
}
export type PortalUser = { id: string; email: string; name: string; role: "SuperAdmin" | "Employee" | "Customer"; organizationId: string | null; permissions: string[]; requiresTwoFactorSetup: boolean; twoFactorEnabled: boolean };
export type PageResult<T = Record<string, unknown>> = { items: T[]; total: number; page: number; pageSize: number };

export async function api<T = unknown>(path: string, options: { method?: string; body?: unknown; signal?: AbortSignal } = {}): Promise<T> {
  const method = options.method ?? "GET";
  const headers = new Headers({ Accept: "application/json" });
  if (method !== "GET") {
    const csrfResponse = await fetch("/api/os/auth/csrf", { credentials: "same-origin", cache: "no-store" });
    const csrf = await csrfResponse.json();
    if (!csrfResponse.ok || !csrf.token) throw new PortalError(csrf.message ?? "De sessie kon niet worden voorbereid. Vernieuw de pagina.", csrfResponse.status);
    headers.set("X-CSRF-TOKEN", csrf.token);
  }
  const isForm = options.body instanceof FormData;
  if (options.body !== undefined && !isForm) headers.set("Content-Type", "application/json");
  const response = await fetch(`/api/os/${path}`, { method, headers, credentials: "same-origin", cache: "no-store", signal: options.signal, body: options.body === undefined ? undefined : isForm ? options.body as FormData : JSON.stringify(options.body) });
  const text = await response.text();
  let data;
  try { data = text ? JSON.parse(text) : null; } catch { data = null; }
  if (!response.ok) throw new PortalError(data?.detail ?? data?.message ?? data?.title ?? "De actie is niet gelukt. Probeer opnieuw.", response.status, data?.errors);
  return data as T;
}
export function errorMessage(error: unknown) { return error instanceof Error ? error.message : "Er ging iets mis. Probeer opnieuw."; }
const activeLocale = () => typeof document !== "undefined" && document.documentElement.lang === "en" ? "en-GB" : "nl-NL";
export const money = (value: unknown) => new Intl.NumberFormat(activeLocale(), { style: "currency", currency: "EUR" }).format(Number(value ?? 0));
export const date = (value: unknown) => value ? new Intl.DateTimeFormat(activeLocale(), { dateStyle: "medium" }).format(new Date(String(value))) : "—";
export const today = () => new Date().toLocaleDateString("en-CA", { timeZone: "Europe/Amsterdam" });
export async function download(path: string, filename: string) {
  const response = await fetch(`/api/os/${path}`, { credentials: "same-origin", cache: "no-store" });
  if (!response.ok) {
    const data = await response.json().catch(() => null);
    throw new PortalError(data?.detail ?? data?.message ?? "Download niet beschikbaar.", response.status);
  }
  const href = URL.createObjectURL(await response.blob());
  const anchor = document.createElement("a"); anchor.href = href; anchor.download = filename; anchor.click();
  setTimeout(() => URL.revokeObjectURL(href), 1000);
}
