"use client";
import Link from "next/link";
import { useRouter, useSearchParams } from "next/navigation";
import { useCallback, useEffect, useState, type FormEvent, type ReactNode } from "react";
import { api, date, errorMessage, money, today, type PageResult, type PortalUser } from "@/lib/portal/api";
import { can, label, type Field, type Module } from "@/lib/portal/modules";
import { Badge, Empty, Loading, Modal, Notice, payload, RecordForm, type Row } from "./Primitives";
import { ExpenseEditor, FinanceEditor, ImportInvoice, UploadDocument } from "./Finance";
import { PeriodPicker } from "./Overview";
import { useLanguage } from "@/components/LanguageProvider";

const archiveEndpoints = new Set(["organizations", "contacts", "notes", "leads", "projects", "tasks", "documents", "time-entries", "expenses"]);
const customerReadEndpoints = new Set(["organizations", "contacts", "projects", "tasks", "tickets", "documents", "quotes", "invoices"]);
const documentFields: Field[] = [
  { key: "category", label: "Categorie", required: true },
  { key: "sharedWithCustomer", label: "Expliciet delen met klant", type: "checkbox" },
];
const financialFieldKeys = new Set(["budget", "hourlyRate", "expectedValue"]);

function safeFields(module: Module, user: PortalUser) {
  return module.fields.filter(field => (!field.permission || can(user, field.permission, true)) && (!financialFieldKeys.has(field.key) || can(user, "finance", true)));
}
function safeValues(values: Record<string, unknown>, user: PortalUser) {
  return Object.fromEntries(Object.entries(values).filter(([key]) => !financialFieldKeys.has(key) || can(user, "finance", true)));
}
function cellValue(row: Row, key: string) {
  if (key === "fileName") return row.originalName ?? row.fileName;
  if (key === "size") return row.sizeBytes ?? row.size;
  return row[key];
}

export function Records({ module, user, organizationId, projectId, compact = false, extraActions }: { module: Module; user: PortalUser; organizationId?: string; projectId?: string; compact?: boolean; extraActions?: ReactNode }) {
  const {language}=useLanguage();
  const tx=(nl:string,en:string)=>language==="en"?en:nl;
  const params = useSearchParams();
  const router = useRouter();
  const urlSearch = compact ? "" : params.get("q") ?? "";
  const [data, setData] = useState<(PageResult<Row> & {summary?:Record<string,number>}) | null>(null);
  const [search, setSearch] = useState(urlSearch);
  const [status, setStatus] = useState("");
  const [organizationFilter,setOrganizationFilter]=useState("");
  const [projectFilter,setProjectFilter]=useState("");
  const [priorityFilter,setPriorityFilter]=useState("");
  const [unbilled,setUnbilled]=useState(false);
  const [period, setPeriod] = useState(() => ({from: today().slice(0,7)+"-01",to:today()}));
  const filterOptions = module.stages ?? ({organizations:["active","prospect","inactive"],quotes:["draft","issued","sent","accepted","rejected","expired"],invoices:["draft","open","partial","paid","overdue","credit"],subscriptions:["active","paused","ended"]} as Record<string,string[]>)[module.endpoint];
  const [page, setPage] = useState(1);
  const [view, setView] = useState("list");
  const [editing, setEditing] = useState<Row | null | "new">(null);
  const [special, setSpecial] = useState("");
  const [problem, setProblem] = useState("");
  const [notice, setNotice] = useState("");
  const [revision, setRevision] = useState(0);
  const [busy, setBusy] = useState(false);
  const [selected, setSelected] = useState<Record<string, Row>>({});
  const customer = user.role === "Customer";
  const readable = customer ? customerReadEndpoints.has(module.endpoint) : can(user, module.permission);
  const writable = !module.readOnly && (customer ? Boolean(module.customerWrite) : can(user, module.permission, true));
  const editable = writable && !customer;
  const finance = ["quotes", "invoices", "expenses"].includes(module.endpoint);
  const financialDocument = ["quotes", "invoices"].includes(module.endpoint);
  const canInvoiceTime = module.endpoint === "time-entries" && !customer && can(user, "finance", true);
  const selectedRows = Object.values(selected);
  const selectedOrganization = selectedRows[0]?.organizationId;
  const close = useCallback(() => { setEditing(null); setSpecial(""); }, []);
  const refresh = () => setRevision(value => value + 1);

  useEffect(() => { setSearch(urlSearch); setPage(1); }, [urlSearch, module.endpoint]);
  useEffect(() => { setSelected({}); }, [search, status, page, organizationId, projectId, module.endpoint, revision]);
  useEffect(() => {
    if (!readable) return;
    const controller = new AbortController();
    const timer = setTimeout(() => {
      const query = new URLSearchParams({ page: String(page), pageSize: "25", q: search, search });
      if (status) query.set(module.status ?? "status", status);
      if (organizationId || organizationFilter) query.set("organizationId", organizationId || organizationFilter);
      if (projectId || projectFilter) query.set("projectId", projectId || projectFilter);
      if (priorityFilter) query.set("priority",priorityFilter);
      if (unbilled) query.set("unbilled","true");
      if (module.endpoint === "time-entries") { query.set("from",period.from); query.set("to",period.to); }
      setProblem(""); setData(null);
      api<PageResult<Row> & {summary?:Record<string,number>}>(`${module.endpoint}?${query}`, { signal: controller.signal }).then(setData).catch(error => { if (!controller.signal.aborted) setProblem(errorMessage(error)); });
    }, 200);
    return () => { clearTimeout(timer); controller.abort(); };
  }, [module.endpoint, module.status, page, search, status, organizationId, projectId, revision, readable, period.from, period.to, organizationFilter, projectFilter, priorityFilter, unbilled]);

  async function save(values: Record<string, unknown>) {
    if (!writable) return;
    const row = editing !== "new" ? editing : null;
    const body = safeValues(values, user);
    if (module.endpoint !== "documents") {
      if (organizationId) body.organizationId = organizationId;
      if (projectId) body.projectId = projectId;
    }
    await api(`${module.endpoint}${row ? `/${row.id}` : ""}`, { method: row ? "PUT" : "POST", body });
    close(); refresh(); setNotice("Opgeslagen.");
  }
  async function move(row: Row, next: string) {
    if (!editable || !module.status) return;
    setBusy(true); setProblem("");
    try {
      await api(`${module.endpoint}/${row.id}`, { method: "PUT", body: { ...payload(safeFields(module, user), row), [module.status]: next } });
      refresh();
    } catch (error) { setProblem(errorMessage(error)); } finally { setBusy(false); }
  }
  async function archive(row: Row) {
    if (!editable || !archiveEndpoints.has(module.endpoint) || row.invoiceId) return;
    const deletingTime = module.endpoint === "time-entries";
    const name = row.name ?? row.title ?? row.companyName ?? row.originalName ?? "dit item";
    if (!window.confirm(`Wil je ${String(name)} ${deletingTime ? "verwijderen" : "archiveren"}?`)) return;
    setBusy(true); setProblem("");
    try { await api(`${module.endpoint}/${row.id}`, { method: "DELETE" }); refresh(); setNotice(deletingTime ? "Urenregistratie verwijderd." : "Gearchiveerd."); }
    catch (error) { setProblem(errorMessage(error)); } finally { setBusy(false); }
  }
  async function generate() {
    setBusy(true); setProblem("");
    try {
      const result = await api<{ generated?: number }>("subscriptions/generate", { method: "POST", body: {} });
      refresh(); setNotice(typeof result.generated === "number" ? `${result.generated} factuurconcept${result.generated === 1 ? "" : "en"} aangemaakt voor de vervallen abonnementsperioden.` : "Factuurgeneratie is afgerond.");
    } catch (error) { setProblem(errorMessage(error)); } finally { setBusy(false); }
  }
  function toggleTime(row: Row) {
    if (row.invoiceId || !row.isBillable || (selectedOrganization && row.organizationId !== selectedOrganization)) return;
    setSelected(previous => { const next = { ...previous }; if (next[row.id]) delete next[row.id]; else next[row.id] = row; return next; });
  }
  async function invoiceTime(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!canInvoiceTime || !selectedRows.length || selectedRows.some(row => row.organizationId !== selectedOrganization || row.invoiceId || !row.isBillable)) return;
    const title = String(new FormData(event.currentTarget).get("title") ?? "").trim();
    setBusy(true); setProblem("");
    try {
      const invoice = await api<Row>("invoices/from-time", { method: "POST", body: { organizationId: selectedOrganization, timeEntryIds: selectedRows.map(row => row.id), ...(title ? { title } : {}) } });
      close(); setSelected({}); refresh(); router.push(`/portaal/facturen/${invoice.id}`);
    } catch (error) { setProblem(errorMessage(error)); } finally { setBusy(false); }
  }

  if (!readable) return <Notice error>{tx("Je account heeft geen toegang tot dit overzicht.","Your account does not have access to this overview.")}</Notice>;
  const initial = editing && editing !== "new" ? editing : { organizationId: organizationId || organizationFilter || user.organizationId || "", projectId: projectId || projectFilter || "" };
  const columns = module.columns.filter(column => !["hourlyRate", "expectedValue", "value"].includes(column.key) || can(user, "finance")).map(column => ({ ...column, label: label(column.label, language) }));
  const headingAction = writable && <button className="os-button primary" onClick={() => module.endpoint === "documents" ? setSpecial("upload") : setEditing("new")}>{module.endpoint === "documents" ? tx("Document uploaden","Upload document") : `${module.singular} ${tx("toevoegen","add")}`} <span aria-hidden="true">+</span></button>;

  return <section className={compact ? "os-records compact" : "os-records"}>
    {!compact && <div className="os-page-heading"><div><span className="os-eyebrow">COFFA OS / {customer ? tx("JOUW OMGEVING","YOUR WORKSPACE") : tx("WERKOMGEVING","WORKSPACE")}</span><h1>{module.title}<span>.</span></h1><p>{module.description}</p></div><div className="os-heading-actions">{extraActions}{writable && module.endpoint === "invoices" && <button className="os-button secondary" onClick={() => setSpecial("import")}>{tx("Oude factuur importeren","Import existing invoice")}</button>}{writable && module.endpoint === "subscriptions" && <button className="os-button secondary" onClick={generate} disabled={busy}>{tx("Facturen genereren","Generate invoices")}</button>}{headingAction}</div></div>}
    <Notice error>{problem}</Notice><Notice>{notice}</Notice>
    {module.endpoint === "subscriptions" && !compact && <SubscriptionOverview revision={revision} />}
    {module.endpoint === "time-entries" && <div className="os-time-period"><PeriodPicker value={period} onChange={next=>{setPeriod(next);setPage(1);setSelected({});}} /></div>}
    {!customer&&<div className="os-heading-actions os-record-filters">
      {!organizationId&&["contacts","projects","tasks","tickets","time-entries","documents","quotes","invoices","subscriptions"].includes(module.endpoint)&&<ReferenceFilter resource="organization-options" label={tx("Alle klanten","All clients")} value={organizationFilter} onChange={value=>{setOrganizationFilter(value);setProjectFilter("");setPage(1);setSelected({});}}/>}
      {!projectId&&["tasks","time-entries"].includes(module.endpoint)&&can(user,"projects")&&<ReferenceFilter resource="projects" label={tx("Alle projecten","All projects")} organizationId={organizationId||organizationFilter} value={projectFilter} onChange={value=>{setProjectFilter(value);setPage(1);setSelected({});}}/>}
      {module.endpoint==="tickets"&&<select aria-label={tx("Filter op prioriteit","Filter by priority")} value={priorityFilter} onChange={event=>{setPriorityFilter(event.target.value);setPage(1);}}><option value="">{tx("Alle prioriteiten","All priorities")}</option>{["low","normal","high","urgent"].map(value=><option value={value} key={value}>{label(value)}</option>)}</select>}
      {module.endpoint==="time-entries"&&<label className="os-check"><input type="checkbox" checked={unbilled} onChange={event=>{setUnbilled(event.target.checked);setPage(1);setSelected({});}}/>{tx("Alleen nog niet gefactureerde uren","Show unbilled time entries only")}</label>}
    </div>}
    {module.endpoint==="time-entries"&&data?.summary&&<><div className="os-kpi-grid">{[[tx("Geregistreerde uren","Recorded hours"),data.summary.totalHours.toLocaleString(language==="en"?"en-GB":"nl-NL",{maximumFractionDigits:2})],[tx("Factureerbare uren","Billable hours"),data.summary.billableHours.toLocaleString(language==="en"?"en-GB":"nl-NL",{maximumFractionDigits:2})],...(can(user,"finance")?[[tx("Factureerbare waarde","Billable value"),money(data.summary.billableValue)],[tx("Effectief tarief per uur","Effective hourly rate"),money(data.summary.effectiveHourlyRate)]]:[])].map(([name,value])=><div className="os-kpi" key={String(name)}><span>{name}</span><strong>{value}</strong></div>)}</div>{can(user,"finance")&&<p className="os-muted">{tx("Het effectieve tarief is de geregistreerde factureerbare waarde gedeeld door alle bestede uren binnen je filters.","The effective hourly rate is the recorded billable value divided by all hours worked within your filters.")}</p>}</>}
    <div className="os-table-toolbar">
      <label className="os-search"><span aria-hidden="true">⌕</span><span className="sr-only">{tx("Zoeken in","Search in")} {module.title}</span><input type="search" value={search} onChange={event => { setSearch(event.target.value); setPage(1); }} placeholder={`${tx("Zoek in","Search in")} ${module.title.toLowerCase()}…`} /></label>
      <div className="os-toolbar-right">{filterOptions && <select aria-label={tx("Filter op status","Filter by status")} value={status} onChange={event => { setStatus(event.target.value); setPage(1); }}><option value="">{tx("Alle statussen","All statuses")}</option>{filterOptions.map(stage => <option key={stage} value={stage}>{label(stage)}</option>)}</select>}{module.stages && <div className="os-segmented" aria-label={tx("Weergave","View")}><button onClick={() => setView("list")} aria-pressed={view === "list"}>{tx("Lijst","List")}</button><button onClick={() => setView("board")} aria-pressed={view === "board"}>Kanban</button></div>}{compact && headingAction}</div>
    </div>
    {canInvoiceTime && selectedRows.length > 0 && <div className="os-panel"><div className="os-heading-actions"><p>{selectedRows.length} {selectedRows.length===1?tx("registratie","entry"):tx("registraties","entries")} {tx("geselecteerd","selected")} · {selectedRows.reduce((sum, row) => sum + Number(row.durationMinutes), 0)} {tx("minuten","minutes")} · {tx("één klantorganisatie.","one customer organization.")}</p><button className="os-button primary" disabled={busy} onClick={() => setSpecial("time-invoice")}>{tx("Factuurconcept maken","Create invoice draft")} ↗</button><button className="os-text-button" onClick={() => setSelected({})}>{tx("Selectie wissen","Clear selection")}</button></div></div>}
    {!data && !problem ? <Loading /> : data?.items.length ? view === "board" && module.stages ? <div className="os-kanban">{module.stages.map(stage => <div className="os-kanban-column" key={stage}><h2>{label(stage)} <span>{data.items.filter(row => row[module.status!] === stage).length}</span></h2>{data.items.filter(row => row[module.status!] === stage).map(row => <article key={row.id}>{module.detail ? <Link href={`/portaal/${module.key}/${row.id}`}><h3>{String(row.title ?? row.name ?? row.companyName ?? row.contactName)}</h3></Link> : <h3>{String(row.title ?? row.name ?? row.companyName ?? row.contactName)}</h3>}{can(user, "finance") && row.expectedValue != null && <p>{money(row.expectedValue)}</p>}{row.dueDate != null && <p>{date(row.dueDate)}</p>}{editable && <select aria-label={`${tx("Verplaats","Move")} ${row.title ?? row.name ?? row.companyName ?? row.contactName}`} value={stage} disabled={busy} onChange={event => move(row, event.target.value)}>{module.stages!.map(option => <option key={option} value={option}>{label(option)}</option>)}</select>}</article>)}</div>)}</div> : <div className="os-table-scroll"><table className="os-table"><thead><tr>{canInvoiceTime && <th scope="col">{tx("Selecteren","Select")}</th>}{columns.map(column => <th key={column.key} scope="col">{column.label}</th>)}<th scope="col"><span className="sr-only">{tx("Acties","Actions")}</span></th></tr></thead><tbody>{data.items.map(row => <tr key={row.id}>{canInvoiceTime && <td>{!row.invoiceId && Boolean(row.isBillable) ? <input type="checkbox" aria-label={`${tx("Selecteer uren","Select time entry")}: ${String(row.description ?? date(row.workDate))}`} checked={Boolean(selected[row.id])} disabled={busy || Boolean(selectedOrganization && row.organizationId !== selectedOrganization)} onChange={() => toggleTime(row)} /> : <span className="os-muted">{row.invoiceId ? tx("Gefactureerd","Invoiced") : tx("Niet factureerbaar","Not billable")}</span>}</td>}{columns.map((column, index) => { const value = cellValue(row, column.key); return <td key={column.key}>{["status", "stage", "priority"].includes(column.key) ? <Badge value={value} /> : index === 0 && module.detail ? <Link className="os-record-link" href={`/portaal/${module.key}/${row.id}`}>{String(value || (finance ? tx("Concept","Draft") : row.title ?? row.name ?? row.contactName ?? tx("Openen","Open")))}</Link> : value == null ? "—" : column.format === "money" ? money(value) : column.format === "date" ? date(value) : column.format === "boolean" ? value ? tx("Ja","Yes") : tx("Nee","No") : column.key === "category" || column.key === "frequency" ? label(value) : String(value)}</td>; })}<td><div className="os-row-actions">{module.endpoint === "documents" ? <a href={`/api/os/documents/${row.id}/download`} className="os-text-button">{tx("Download","Download")} ↗</a> : module.detail ? <Link href={`/portaal/${module.key}/${row.id}`} className="os-text-button">{tx("Openen","Open")} ↗</Link> : null}{editable && (!finance || module.endpoint === "expenses") && !(module.endpoint === "time-entries" && row.invoiceId) && <button className="os-text-button" onClick={() => setEditing(row)}>{tx("Bewerken","Edit")}</button>}{editable && archiveEndpoints.has(module.endpoint) && !(module.endpoint === "time-entries" && row.invoiceId) && <button className="os-text-button" disabled={busy} onClick={() => archive(row)}>{module.endpoint === "time-entries" ? tx("Verwijderen","Delete") : tx("Archiveren","Archive")}</button>}{module.endpoint === "time-entries" && Boolean(row.invoiceId) && <span className="os-muted">{tx("Definitief gekoppeld aan factuur","Permanently linked to invoice")}</span>}</div></td></tr>)}</tbody></table></div> : data ? <Empty>{search || status ? tx("Geen resultaten voor deze zoekopdracht.","No results for this search.") : tx(`Je hebt nog geen ${module.title.toLowerCase()} in dit overzicht.`,`There are no ${module.title.toLowerCase()} in this overview yet.`)}</Empty> : null}
    {data && <div className="os-pagination"><span>{data.total} {tx("resultaten","results")}{view === "board" ? ` · ${tx("Kanban toont de huidige pagina","Kanban shows the current page")}` : ""}</span><div><button className="os-button secondary" disabled={page <= 1} onClick={() => setPage(value => value - 1)}>← {tx("Vorige","Previous")}</button><span>{tx("Pagina","Page")} {page}</span><button className="os-button secondary" disabled={page * data.pageSize >= data.total} onClick={() => setPage(value => value + 1)}>{tx("Volgende","Next")} →</button></div></div>}
    {editing && <Modal title={`${module.singular} ${editing === "new" ? "toevoegen" : "bewerken"}`} onClose={close}>{financialDocument ? <FinanceEditor kind={module.endpoint as "quotes" | "invoices"} user={user} initial={initial} onSaved={() => { close(); refresh(); }} /> : module.endpoint === "expenses" ? <ExpenseEditor initial={initial} onSaved={() => { close(); refresh(); setNotice("Uitgave opgeslagen."); }} /> : <RecordForm fields={module.endpoint === "documents" ? documentFields : safeFields(module, user)} initial={initial} user={user} onSave={save} />}</Modal>}
    {special === "import" && <Modal title="Bestaande factuur importeren" onClose={close}><ImportInvoice user={user} onSaved={() => { close(); refresh(); }} /></Modal>}
    {special === "upload" && <Modal title="Document toevoegen" onClose={close}><UploadDocument user={user} organizationId={organizationId} projectId={projectId} onSaved={() => { close(); refresh(); }} /></Modal>}
    {special === "time-invoice" && <Modal title="Geselecteerde uren factureren" onClose={close}><form className="os-form" onSubmit={invoiceTime}><p>{selectedRows.length} registraties worden met hun historische uurtarieven gekoppeld aan één factuurconcept. De backend controleert opnieuw of ze nog niet gefactureerd zijn.</p><Notice error>{problem}</Notice><label>Omschrijving van de factuur (optioneel)<input name="title" maxLength={200} placeholder="Bijvoorbeeld: Ontwikkelwerkzaamheden" /></label><button className="os-button primary" disabled={busy}>{busy ? "Concept maken…" : "Factuurconcept maken ↗"}</button></form></Modal>}
  </section>;
}

function SubscriptionOverview({revision}:{revision:number}) {
  const [summary,setSummary]=useState<{mrr:number;arr:number;activeContracts:number;endedContracts:number;upcoming:Row[]}|null>(null);
  const [problem,setProblem]=useState("");
  useEffect(()=>{const controller=new AbortController();setProblem("");api<NonNullable<typeof summary>>("subscriptions/summary",{signal:controller.signal}).then(setSummary).catch(error=>{if(!controller.signal.aborted)setProblem(errorMessage(error));});return()=>controller.abort();},[revision]);
  return <><Notice error>{problem}</Notice>{summary?<><div className="os-kpi-grid">{[["MRR · per maand",money(summary.mrr)],["ARR · per jaar",money(summary.arr)],["Actieve contracten",summary.activeContracts],["Beëindigde contracten",summary.endedContracts]].map(([name,value])=><div className="os-kpi" key={String(name)}><span>{name}</span><strong>{value}</strong></div>)}</div><div className="os-panel"><h2>Eerstvolgende automatische facturen</h2>{summary.upcoming.length?summary.upcoming.map(item=><div className="os-list-row" key={item.id}><span>{String(item.name)}</span><strong>{date(item.nextInvoiceDate)}</strong></div>):<Empty>Er staan geen automatische facturen gepland voor actieve contracten.</Empty>}</div></>:!problem?<Loading/>:null}</>;
}

function ReferenceFilter({resource,label:caption,value,onChange,organizationId}:{resource:string;label:string;value:string;onChange:(value:string)=>void;organizationId?:string}) {
 const [options,setOptions]=useState<Row[]>([]); const [problem,setProblem]=useState("");
 useEffect(()=>{const controller=new AbortController();setProblem("");(async()=>{const rows:Row[]=[];let page=1,total=1;while(rows.length<total){const query=new URLSearchParams({page:String(page++),pageSize:"100"});if(organizationId)query.set("organizationId",organizationId);const result=await api<PageResult<Row>>(`${resource}?${query}`,{signal:controller.signal});rows.push(...result.items);total=result.total;if(!result.items.length)break;}setOptions(rows);})().catch(error=>{if(!controller.signal.aborted)setProblem(errorMessage(error));});return()=>controller.abort();},[resource,organizationId]);
 return <><select aria-label={caption} value={value} onChange={event=>onChange(event.target.value)}><option value="">{caption}</option>{options.map(row=><option value={row.id} key={row.id}>{String(row.name??row.title)}</option>)}</select><Notice error>{problem}</Notice></>;
}
