"use client";
import { useEffect, useState, type FormEvent } from "react";
import { api, errorMessage, money, today, type PortalUser } from "@/lib/portal/api";
import { Notice, type Row } from "./Primitives";
import { useLanguage } from "@/components/LanguageProvider";

type Line = { description: string; quantity: string; unitPrice: string; discountPercent: string; taxRate: string };
const newLine = (): Line => ({ description: "", quantity: "1", unitPrice: "0", discountPercent: "0", taxRate: "21" });
function scaled(value: string, digits: number) {
  if (!/^\d+(?:\.\d*)?$/.test(value) || (value.split(".")[1]?.length ?? 0) > digits) throw new Error("Ongeldig bedrag");
  const [whole, decimal = ""] = value.split(".");
  return BigInt(whole) * 10n ** BigInt(digits) + BigInt(decimal.padEnd(digits, "0") || "0");
}
const rounded = (numerator: bigint, denominator: bigint) => numerator < 0n ? -((-numerator + denominator / 2n) / denominator) : (numerator + denominator / 2n) / denominator;
function totals(lines: Line[]) {
  let net = 0n, tax = 0n;
  try {
    for (const line of lines) {
      const quantity = scaled(line.quantity, 4), price = scaled(line.unitPrice, 2), discount = scaled(line.discountPercent, 2), rate = scaled(line.taxRate, 2);
      if (quantity <= 0n || quantity > 1000000000n || price > 1000000000n || discount > 10000n || rate > 10000n) return null;
      const cents = rounded(quantity * price * (10000n - discount), 100000000n);
      net += cents; tax += rounded(cents * rate, 10000n);
    }
  } catch { return null; }
  return { net: Number(net) / 100, tax: Number(tax) / 100, total: Number(net + tax) / 100 };
}
function useReferences(user: PortalUser) {
  const [organizations, setOrganizations] = useState<Row[]>([]), [projects, setProjects] = useState<Row[]>([]), [problem, setProblem] = useState("");
  const projectsAllowed = user.role === "SuperAdmin" || user.permissions.includes("projects.read");
  useEffect(() => {
    const controller = new AbortController();
    async function all(resource: string) {
      const values: Row[] = []; let page = 1, total = 1;
      while (values.length < total) {
        const result = await api<{ items: Row[]; total: number }>(`${resource}?pageSize=100&page=${page++}`, { signal: controller.signal });
        values.push(...result.items); total = result.total;
        if (!result.items.length) break;
      }
      return values;
    }
    all("organization-options").then(values => { if (!controller.signal.aborted) setOrganizations(values); }).catch(error => { if (!controller.signal.aborted) setProblem(errorMessage(error)); });
    if (projectsAllowed) all("projects").then(values => { if (!controller.signal.aborted) setProjects(values); }).catch(error => { if (!controller.signal.aborted) setProblem(errorMessage(error)); });
    return () => controller.abort();
  }, [projectsAllowed]);
  return { organizations, projects, projectsAllowed, problem };
}
export function FinanceEditor({ kind, user, initial = {}, onSaved, onBusyChange }: { kind: "quotes" | "invoices"; user: PortalUser; initial?: Record<string, unknown>; onSaved: (record?: Row) => void; onBusyChange?: (busy: boolean) => void }) {
  const { t } = useLanguage();
  const refs = useReferences(user);
  const [organizationId, setOrganizationId] = useState(String(initial.organizationId ?? "")), [projectId, setProjectId] = useState(String(initial.projectId ?? ""));
  const [title, setTitle] = useState(String(initial.title ?? "")), [notes, setNotes] = useState(String(initial.notes ?? "")), [terms, setTerms] = useState(String(initial.terms ?? "")), [shared, setShared] = useState(Boolean(initial.sharedWithCustomer));
  const [invoiceDate, setInvoiceDate] = useState(String(initial.invoiceDate ?? today())), [dueDate, setDueDate] = useState(String(initial.dueDate ?? initial.validUntil ?? ""));
  const [lines, setLines] = useState<Line[]>(() => Array.isArray(initial.lines) ? initial.lines.map(line => Object.fromEntries(Object.keys(newLine()).map(key => [key, String((line as Record<string, unknown>)[key] ?? "")])) as Line) : [newLine()]);
  const [busy, setBusy] = useState(false), [problem, setProblem] = useState(""); const total = totals(lines);
  async function submit(event: FormEvent) {
    event.preventDefault(); if (busy) return;
    if (!total) { setProblem(t("Controleer aantallen, prijzen, korting en btw per regel.", "Check the quantity, price, discount and VAT on each line.")); return; }
    setBusy(true); onBusyChange?.(true); setProblem("");
    try {
      const record = await api<Row>(`${kind}${initial.id ? `/${initial.id}` : ""}`, { method: initial.id ? "PUT" : "POST", body: { organizationId, projectId: projectId || null, title, notes, terms, sharedWithCustomer: shared, ...(kind === "quotes" ? { validUntil: dueDate } : { invoiceDate, dueDate }), lines: lines.map(line => ({ description: line.description, quantity: Number(line.quantity), unitPrice: Number(line.unitPrice), discountPercent: Number(line.discountPercent), taxRate: Number(line.taxRate) })) } }); onSaved(record);
    } catch (error) { setProblem(errorMessage(error)); } finally { setBusy(false); onBusyChange?.(false); }
  }
  function update(index: number, key: keyof Line, value: string) { setLines(previous => previous.map((line, item) => item === index ? { ...line, [key]: value } : line)); }
  return <form className="os-form" onSubmit={submit} aria-busy={busy}>
    <Notice error>{problem || refs.problem}</Notice>
    <div className="os-form-grid">
      <label>{t("Klantorganisatie", "Customer organisation")}<select required disabled={busy} value={organizationId} onChange={event => { setOrganizationId(event.target.value); setProjectId(""); }}><option value="">{t("Kies een klant", "Choose a customer")}</option>{refs.organizations.map(row => <option value={row.id} key={row.id}>{String(row.name)}</option>)}</select></label>
      {refs.projectsAllowed && <label>{t("Project (optioneel)", "Project (optional)")}<select disabled={busy} value={projectId} onChange={event => setProjectId(event.target.value)}><option value="">{t("Geen project", "No project")}</option>{refs.projects.filter(row => row.organizationId === organizationId).map(row => <option value={row.id} key={row.id}>{String(row.name)}</option>)}</select></label>}
      <label className="os-span-two">{t("Omschrijving", "Description")}<input required disabled={busy} maxLength={200} value={title} onChange={event => setTitle(event.target.value)} /></label>
      {kind === "invoices" && <label>{t("Factuurdatum", "Invoice date")}<input type="date" required disabled={busy} min="1900-01-01" max="2200-12-31" value={invoiceDate} onChange={event => setInvoiceDate(event.target.value)} /></label>}
      <label>{kind === "quotes" ? t("Geldig tot", "Valid until") : t("Vervaldatum", "Due date")}<input type="date" required disabled={busy} min={kind === "invoices" ? invoiceDate : "1900-01-01"} max="2200-12-31" value={dueDate} onChange={event => setDueDate(event.target.value)} /></label>
    </div>
    <div className="os-line-editor"><h3>{kind === "quotes" ? t("Offerteregels", "Quote line items") : t("Factuurregels", "Invoice line items")}</h3>
      {lines.map((line, index) => <fieldset className="os-finance-line" key={index} disabled={busy}><legend>{t("Regel", "Line")} {index + 1}</legend>
        <label className="os-line-description">{t("Omschrijving", "Description")}<input required maxLength={1000} value={line.description} onChange={event => update(index, "description", event.target.value)} /></label>
        <label>{t("Aantal", "Quantity")}<input required type="number" min="0.0001" max="100000" step="0.0001" value={line.quantity} onChange={event => update(index, "quantity", event.target.value)} /></label>
        <label>{t("Prijs excl. btw", "Price excl. VAT")}<input required type="number" min="0" max="10000000" step="0.01" value={line.unitPrice} onChange={event => update(index, "unitPrice", event.target.value)} /></label>
        <label>{t("Korting %", "Discount %")}<input required type="number" min="0" max="100" step="0.01" value={line.discountPercent} onChange={event => update(index, "discountPercent", event.target.value)} /></label>
        <label>{t("Btw %", "VAT %")}<input required type="number" min="0" max="100" step="0.01" value={line.taxRate} onChange={event => update(index, "taxRate", event.target.value)} /></label>
        <button className="os-text-button" type="button" disabled={lines.length === 1 || busy} onClick={() => setLines(previous => previous.filter((_, item) => item !== index))}>{t("Verwijder regel", "Remove line")}</button>
      </fieldset>)}
      <button className="os-button secondary" type="button" disabled={busy || lines.length >= 200} onClick={() => setLines(previous => [...previous, newLine()])}>{t("Regel toevoegen", "Add line")} +</button>
    </div>
    {total && <dl className="os-totals" aria-live="polite"><div><dt>{t("Subtotaal", "Subtotal")}</dt><dd>{money(total.net)}</dd></div><div><dt>{t("Btw", "VAT")}</dt><dd>{money(total.tax)}</dd></div><div><dt>{t("Totaal", "Total")}</dt><dd>{money(total.total)}</dd></div></dl>}
    <div className="os-form-grid">
      <label>{t("Opmerkingen", "Notes")}<textarea rows={3} maxLength={10000} disabled={busy} value={notes} onChange={event => setNotes(event.target.value)} /></label>
      <label>{t("Voorwaarden", "Terms")}<textarea rows={3} maxLength={20000} disabled={busy} value={terms} onChange={event => setTerms(event.target.value)} /></label>
      <label className="os-check"><input type="checkbox" disabled={busy} checked={shared} onChange={event => setShared(event.target.checked)} />{t("Beschikbaar stellen in klantenportaal", "Make available in the client portal")}</label>
    </div>
    <p className="os-muted">{t("Je slaat eerst een concept op. De server controleert de bedragen. Definitief uitgeven en e-mailen zijn afzonderlijke acties.", "First save a draft. The server verifies the amounts. Issuing the final document and emailing it are separate actions.")}</p>
    <button className="os-button primary" disabled={busy}>{busy ? t("Opslaan…", "Saving…") : t("Concept opslaan", "Save draft")} ↗</button>
  </form>;
}

export function ExpenseEditor({ initial = {}, onSaved }: { initial?: Record<string, unknown>; onSaved: () => void }) {
  const { t } = useLanguage();
  const [dateValue, setDateValue] = useState(String(initial.date ?? today()));
  const [supplier, setSupplier] = useState(String(initial.supplier ?? ""));
  const [description, setDescription] = useState(String(initial.description ?? ""));
  const [netAmount, setNetAmount] = useState(String(initial.netAmount ?? ""));
  const [taxRate, setTaxRate] = useState(String(initial.taxRate ?? "21"));
  const [busy, setBusy] = useState(false);
  const [problem, setProblem] = useState("");
  const net = Number(netAmount), rate = Number(taxRate);
  const tax = Number.isFinite(net) && Number.isFinite(rate) ? Math.round((net * rate + Number.EPSILON) * 100) / 100 : 0;
  async function submit(event: FormEvent) {
    event.preventDefault(); if (busy) return;
    setBusy(true); setProblem("");
    try {
      await api(`expenses${initial.id ? `/${initial.id}` : ""}`, {
        method: initial.id ? "PUT" : "POST",
        body: { date: dateValue, supplier, description, netAmount: net, taxRate: rate },
      });
      onSaved();
    } catch (error) { setProblem(errorMessage(error)); } finally { setBusy(false); }
  }
  return <form className="os-form" onSubmit={submit} aria-busy={busy}>
    <Notice error>{problem}</Notice>
    <div className="os-form-grid">
      <label>{t("Datum", "Date")}<input type="date" required min="1900-01-01" max={today()} value={dateValue} onChange={event => setDateValue(event.target.value)} disabled={busy} /></label>
      <label>{t("Leverancier", "Supplier")}<input required maxLength={200} value={supplier} onChange={event => setSupplier(event.target.value)} disabled={busy} /></label>
      <label className="os-span-two">{t("Omschrijving", "Description")}<input required maxLength={1000} value={description} onChange={event => setDescription(event.target.value)} disabled={busy} /></label>
      <label>{t("Bedrag excl. btw", "Amount excl. VAT")}<input required type="number" min="0.01" max="1000000000" step="0.01" value={netAmount} onChange={event => setNetAmount(event.target.value)} disabled={busy} /></label>
      <label>{t("Btw-percentage", "VAT rate")}<input required type="number" min="0" max="100" step="0.01" value={taxRate} onChange={event => setTaxRate(event.target.value)} disabled={busy} /></label>
    </div>
    <p className="os-muted">{t("Btw:", "VAT:")} {money(tax)} · {t("Totaal incl. btw:", "Total incl. VAT:")} {money(net + tax)}</p>
    <button className="os-button primary" disabled={busy}>{busy ? t("Opslaan…", "Saving…") : initial.id ? t("Uitgave opslaan", "Save expense") : t("Uitgave registreren", "Add expense")} ↗</button>
  </form>;
}

export function ImportInvoice({ user, onSaved }: { user: PortalUser; onSaved: () => void }) {
  const { t } = useLanguage();
  const refs = useReferences(user); const [problem, setProblem] = useState(""), [busy, setBusy] = useState(false), [paidAmount, setPaidAmount] = useState("0"), [organizationId, setOrganizationId] = useState(""), [invoiceDate, setInvoiceDate] = useState("");
  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); if (busy) return;
    const body = new FormData(event.currentTarget); const file = body.get("file");
    if (!(file instanceof File) || file.size < 5 || file.size > 10 * 1024 * 1024) { setProblem(t("Kies een PDF van maximaal 10 MB.", "Choose a PDF no larger than 10 MB.")); return; }
    try { if (scaled(String(body.get("netAmount")), 2) + scaled(String(body.get("taxAmount")), 2) !== scaled(String(body.get("totalAmount")), 2)) { setProblem(t("Exclusief btw plus btw moet gelijk zijn aan het totaalbedrag.", "The amount excluding VAT plus VAT must equal the total.")); return; } } catch { setProblem(t("Vul geldige bedragen met maximaal twee decimalen in.", "Enter valid amounts with no more than two decimal places.")); return; }
    setBusy(true); setProblem("");
    try { await api("invoices/import", { method: "POST", body }); onSaved(); } catch (error) { setProblem(errorMessage(error)); } finally { setBusy(false); }
  }
  return <form className="os-form" onSubmit={submit} aria-busy={busy}>
    <p>{t("Leg de oorspronkelijke gegevens vast. Deze factuur blijft herkenbaar als historische import.", "Enter the original details. This invoice will remain identified as a historical import.")}</p><Notice error>{problem || refs.problem}</Notice>
    <div className="os-form-grid">
      <label>{t("Klant", "Customer")}<select name="organizationId" required disabled={busy} value={organizationId} onChange={event => setOrganizationId(event.target.value)}><option value="">{t("Kies een klant", "Choose a customer")}</option>{refs.organizations.map(row => <option key={row.id} value={row.id}>{String(row.name)}</option>)}</select></label>
      <label>{t("Origineel factuurnummer", "Original invoice number")}<input name="number" required maxLength={64} disabled={busy} /></label>
      {refs.projectsAllowed && <label>{t("Project (optioneel)", "Project (optional)")}<select name="projectId" disabled={busy} key={organizationId}><option value="">{t("Geen project", "No project")}</option>{refs.projects.filter(row => row.organizationId === organizationId).map(row => <option key={row.id} value={row.id}>{String(row.name)}</option>)}</select></label>}
      <label>{t("Factuurdatum", "Invoice date")}<input name="invoiceDate" type="date" required min="1900-01-01" max="2200-12-31" value={invoiceDate} disabled={busy} onChange={event => setInvoiceDate(event.target.value)} /></label>
      <label>{t("Vervaldatum", "Due date")}<input name="dueDate" type="date" required min={invoiceDate || "1900-01-01"} max="2200-12-31" disabled={busy} /></label>
      {[{ key: "netAmount", label: t("Exclusief btw", "Excluding VAT") }, { key: "taxAmount", label: t("Btw-bedrag", "VAT amount") }, { key: "totalAmount", label: t("Totaalbedrag", "Total amount") }].map(field => <label key={field.key}>{field.label}<input name={field.key} required type="number" min={field.key === "totalAmount" ? "0.01" : "0"} step="0.01" disabled={busy} /></label>)}
      <label>{t("Reeds ontvangen", "Already received")}<input name="paidAmount" required type="number" min="0" step="0.01" value={paidAmount} disabled={busy} onChange={event => setPaidAmount(event.target.value)} /></label>
      {Number(paidAmount) > 0 && <label>{t("Werkelijke betaaldatum", "Actual payment date")}<input name="paymentDate" type="date" required min="1900-01-01" max={today()} disabled={busy} /><span className="os-muted">{t("Gebruik de datum waarop je deze betaling hebt ontvangen.", "Enter the date you received this payment.")}</span></label>}
      <label className="os-span-two">{t("Originele PDF (maximaal 10 MB)", "Original PDF (up to 10 MB)")}<input name="file" type="file" accept=".pdf,application/pdf" required disabled={busy} /></label>
    </div><button className="os-button primary" disabled={busy}>{busy ? t("Importeren…", "Importing…") : t("Factuur importeren", "Import invoice")} ↗</button>
  </form>;
}
export function UploadDocument({ user, organizationId, projectId, ticketId, onSaved }: { user: PortalUser; organizationId?: string; projectId?: string; ticketId?: string; onSaved: () => void }) {
  const { t } = useLanguage();
  const [organizations, setOrganizations] = useState<Row[]>([]), [problem, setProblem] = useState(""), [busy, setBusy] = useState(false);
  useEffect(() => {
    if (organizationId || user.organizationId) return;
    const controller = new AbortController();
    (async () => { const rows: Row[] = []; let page = 1, total = 1; while (rows.length < total) { const data = await api<{ items: Row[]; total: number }>(`organization-options?pageSize=100&page=${page++}`, { signal: controller.signal }); rows.push(...data.items); total = data.total; if (!data.items.length) break; } if (!controller.signal.aborted) setOrganizations(rows); })().catch(error => { if (!controller.signal.aborted) setProblem(errorMessage(error)); });
    return () => controller.abort();
  }, [organizationId, user.organizationId]);
  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); if (busy) return;
    const body = new FormData(event.currentTarget), file = body.get("file");
    if (!(file instanceof File) || file.size < 1 || file.size > 20 * 1024 * 1024) { setProblem(t("Kies één bestand van maximaal 20 MB.", "Choose one file up to 20 MB.")); return; }
    if (organizationId || user.organizationId) body.set("organizationId", organizationId ?? user.organizationId!);
    if (projectId) body.set("projectId", projectId); if (ticketId) body.set("ticketId", ticketId);
    setBusy(true); setProblem("");
    try { await api("documents", { method: "POST", body }); onSaved(); } catch (error) { setProblem(errorMessage(error)); } finally { setBusy(false); }
  }
  if (user.role === "Customer" && !ticketId) return <p>{t("Voeg bestanden toe vanuit je supportticket, zodat je bestand bij de juiste vraag terechtkomt.", "Add files from your support ticket so they are attached to the right request.")}</p>;
  return <form className="os-form" onSubmit={submit} aria-busy={busy}>
    <Notice error>{problem}</Notice>
    {!organizationId && user.role !== "Customer" && <label>{t("Klantorganisatie", "Customer organisation")}<select name="organizationId" required disabled={busy}><option value="">{t("Kies een klant", "Choose a customer")}</option>{organizations.map(row => <option key={row.id} value={row.id}>{String(row.name)}</option>)}</select></label>}
    <label>{t("Bestand (maximaal 20 MB)", "File (up to 20 MB)")}<input name="file" type="file" required disabled={busy} accept=".pdf,.png,.jpg,.jpeg,.webp,.txt,.csv,.docx,.xlsx" /></label>
    <label>{t("Categorie", "Category")}<input name="category" required disabled={busy} maxLength={80} defaultValue={ticketId ? "Support" : "Algemeen"} /></label>
    {user.role !== "Customer" && <label className="os-check"><input type="checkbox" disabled={busy} name="sharedWithCustomer" value="true" />{t("Expliciet delen met de klant", "Explicitly share with the customer")}</label>}
    <p className="os-muted">{t("Bestanden worden privé opgeslagen. PDF, afbeeldingen, tekst, CSV, DOCX en XLSX zijn toegestaan.", "Files are stored privately. PDF, images, text, CSV, DOCX and XLSX files are supported.")}</p>
    <button className="os-button primary" disabled={busy}>{busy ? t("Uploaden…", "Uploading…") : t("Document opslaan", "Save document")} ↗</button>
  </form>;
}
