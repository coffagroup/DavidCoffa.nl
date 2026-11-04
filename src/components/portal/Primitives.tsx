"use client";
import { useEffect, useRef, useState, type FormEvent, type ReactNode } from "react";
import { api, errorMessage, today, type PortalUser } from "@/lib/portal/api";
import { can, label, type Field } from "@/lib/portal/modules";
import { useLanguage } from "@/components/LanguageProvider";
export type Row = { id: string; [key: string]: unknown };
export function Notice({ children, error = false }: { children: ReactNode; error?: boolean }) { return children ? <div className={`os-notice${error ? " is-error" : ""}`} role={error ? "alert" : "status"}>{children}</div> : null; }
export function Empty({ children }: { children?: ReactNode }) { const {language}=useLanguage();return <div className="os-empty"><span aria-hidden="true">↳</span><p>{children??(language==="en"?"There is no data in this overview yet.":"Er staan nog geen gegevens in dit overzicht.")}</p></div>; }
export function Loading() { const {language}=useLanguage();return <div className="os-loading" role="status"><span className="os-spinner" /> {language==="en"?"Loading data…":"Gegevens laden…"}</div>; }
export function Badge({ value }: { value: unknown }) { const {language}=useLanguage();return <span className={`os-badge status-${String(value)}`}>{label(value,language)}</span>; }
export function Modal({ title, children, onClose }: { title: string; children: ReactNode; onClose: () => void }) {
  const {language}=useLanguage();
  const ref = useRef<HTMLDivElement>(null);
  useEffect(() => {
    const previous = document.activeElement as HTMLElement;
    ref.current?.querySelector<HTMLElement>("input,select,textarea,button")?.focus();
    function keyboard(event: KeyboardEvent) {
      if (event.key === "Escape") onClose();
      if (event.key === "Tab") {
        const nodes = [...ref.current!.querySelectorAll<HTMLElement>('button:not([disabled]),input:not([disabled]),select:not([disabled]),textarea:not([disabled]),a[href]')];
        if (!nodes.length) return;
        if (event.shiftKey && document.activeElement === nodes[0]) { event.preventDefault(); nodes.at(-1)?.focus(); }
        else if (!event.shiftKey && document.activeElement === nodes.at(-1)) { event.preventDefault(); nodes[0].focus(); }
      }
    }
    document.addEventListener("keydown", keyboard);
    return () => { document.removeEventListener("keydown", keyboard); previous?.focus(); };
  }, [onClose]);
  return <div className="os-modal-backdrop"><div className="os-modal" role="dialog" aria-modal="true" aria-label={title} ref={ref}><div className="os-modal-heading"><h2>{title}</h2><button type="button" className="os-icon-button" onClick={onClose} aria-label={language==="en"?"Close":"Sluiten"}>×</button></div>{children}</div></div>;
}
export function payload(fields: Field[], values: Record<string, unknown>) {
  const result: Record<string, unknown> = {};
  for (const field of fields) {
    const value = values[field.key];
    result[field.key] = field.type === "number" ? value === "" || value == null ? 0 : Number(value)
      : field.type === "checkbox" ? Boolean(value)
      : field.type === "tags" ? Array.isArray(value) ? value : String(value ?? "").split(",").map(item => item.trim()).filter(Boolean)
      : field.resource || field.type === "date" ? value || null : value ?? "";
  }
  return result;
}
export function RecordForm({ fields, user, initial = {}, onSave, submitLabel = "Opslaan" }: { fields: Field[]; user: PortalUser; initial?: Record<string, unknown>; onSave: (data: Record<string, unknown>) => Promise<void>; submitLabel?: string }) {
  const {language}=useLanguage();
  const localizedSubmitLabel=language==="en"&&submitLabel==="Opslaan"?"Save":submitLabel;
  const visibleFields = fields.filter(field => (!field.permission || can(user, field.permission, true)) && !(user.role === "Customer" && ["status", "assignedToUserId", "sharedWithCustomer"].includes(field.key)));
  const referenceAllowed = (resource: string) => resource === "projects" || resource === "tasks" ? user.role === "Customer" || can(user, "projects") : resource === "tickets" ? user.role === "Customer" || can(user, "tickets") : resource === "assignees" ? can(user, "projects") || can(user, "tickets") : true;
  const [values, setValues] = useState<Record<string, unknown>>(() => Object.fromEntries(fields.map(field => [field.key, initial[field.key] ?? (field.key === "organizationId" && user.role === "Customer" ? user.organizationId : field.type === "checkbox" ? false : field.key === "priority" ? "normal" : field.type === "select" ? field.options?.[0] ?? "" : field.key === "workDate" ? today() : "")])));
  const [options, setOptions] = useState<Record<string, Row[]>>({});
  const [problem, setProblem] = useState("");
  const [saving, setSaving] = useState(false);
  useEffect(() => {
    let active = true;
    const resources = ([...new Set(visibleFields.map(field => field.resource).filter(Boolean))] as string[]).filter(referenceAllowed);
    Promise.all(resources.map(async resource => { const rows: Row[] = []; let page = 1, total = 1; while (rows.length < total) { const result = await api<{items: Row[]; total: number}>(`${resource==="organizations"?"organization-options":resource}?pageSize=100&page=${page++}`); rows.push(...result.items); total = result.total; if (!result.items.length) break; } return [resource, rows] as const; })).then(entries => { if (active) setOptions(Object.fromEntries(entries)); }).catch(error => { if (active) setProblem(errorMessage(error)); });
    return () => { active = false; };
    // Fields are supplied from static module definitions.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);
  async function submit(event: FormEvent) {
    event.preventDefault(); setSaving(true); setProblem("");
    try { await onSave(payload(visibleFields, values)); } catch (error) { setProblem(errorMessage(error)); } finally { setSaving(false); }
  }
  return <form className="os-form" onSubmit={submit}>
    <Notice error>{problem}</Notice>
    <div className="os-form-grid">{visibleFields.map(field => {
      if ((field.resource && !referenceAllowed(field.resource)) || (user.role === "Customer" && ["organizationId", "status", "assignedToUserId", "sharedWithCustomer"].includes(field.key))) return null;
      const id = `os-field-${field.key}`;
      const value = values[field.key];
      const change = (next: unknown) => setValues(previous => ({ ...previous, [field.key]: next }));
      const candidates = (options[field.resource ?? ""] ?? []).filter(row => !["projects", "tasks", "tickets"].includes(field.resource ?? "") || !values.organizationId || !row.organizationId || row.organizationId === values.organizationId);
      return <label key={field.key} className={field.type === "textarea" ? "os-span-two" : field.type === "checkbox" ? "os-check" : ""} htmlFor={id}>
        {field.type === "checkbox" ? <><input id={id} type="checkbox" checked={Boolean(value)} onChange={event => change(event.target.checked)} disabled={saving} />{label(field.label,language)}</> : <><span>{label(field.label,language)}{field.required && <span aria-hidden="true"> *</span>}</span>
          {field.resource || field.type === "select" ? <select id={id} required={field.required} value={String(value ?? "")} disabled={saving} onChange={event => change(event.target.value)}><option value="">{field.required ? language==="en"?"Choose an option":"Maak een keuze" : language==="en"?"None":"Geen"}</option>{field.resource ? candidates.map(row => <option key={row.id} value={row.id}>{String(row.name ?? row.title ?? row.companyName ?? row.email ?? row.id)}</option>) : field.options?.map(option => <option key={option} value={option}>{label(option,language)}</option>)}</select>
          : field.type === "textarea" ? <textarea id={id} rows={4} maxLength={10000} required={field.required} disabled={saving} value={String(value ?? "")} onChange={event => change(event.target.value)} />
          : <input id={id} type={field.type === "number" ? "number" : field.type === "date" ? "date" : field.type === "email" ? "email" : "text"} step={field.step ?? "1"} min={field.type === "number" ? "0" : undefined} maxLength={field.type === "number" ? undefined : 2000} required={field.required} disabled={saving} value={Array.isArray(value) ? value.join(", ") : String(value ?? "")} onChange={event => change(event.target.value)} />}</>}
      </label>;
    })}</div>
    <div className="os-form-footer"><span>{language==="en"?"Fields marked * are required.":"Velden met * zijn verplicht."}</span><button className="os-button primary" disabled={saving} type="submit">{saving ? language==="en"?"Saving…":"Opslaan…" : localizedSubmitLabel} <span aria-hidden="true">↗</span></button></div>
  </form>;
}
