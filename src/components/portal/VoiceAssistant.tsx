"use client";

import Link from "next/link";
import { useCallback, useEffect, useRef, useState } from "react";
import { api, errorMessage, type PortalUser } from "@/lib/portal/api";
import { can, modules, type Field } from "@/lib/portal/modules";
import { FinanceEditor } from "./Finance";
import { Loading, Modal, Notice, RecordForm, type Row } from "./Primitives";
import { useLanguage } from "@/components/LanguageProvider";

const MAX_AUDIO_BYTES = 15 * 1024 * 1024;
const MAX_RECORDING_SECONDS = 120;
const AUDIO_ACCEPT = ".mp3,.mp4,.m4a,.mpeg,.mpga,.wav,.webm,.ogg,.flac,audio/*";
const kinds = {
  create_organization: { module: "klanten", endpoint: "organizations", title: "Klant", permission: "crm" },
  create_contact: { module: "contactpersonen", endpoint: "contacts", title: "Contactpersoon", permission: "crm" },
  create_lead: { module: "leads", endpoint: "leads", title: "Lead", permission: "crm" },
  create_note: { module: "notities", endpoint: "notes", title: "Interne notitie", permission: "crm" },
  create_project: { module: "projecten", endpoint: "projects", title: "Project", permission: "projects" },
  create_task: { module: "taken", endpoint: "tasks", title: "Taak", permission: "projects" },
  create_time_entry: { module: "uren", endpoint: "time-entries", title: "Urenregistratie", permission: "time" },
  create_ticket: { module: "tickets", endpoint: "tickets", title: "Supportticket", permission: "tickets" },
  create_quote: { module: "offertes", endpoint: "quotes", title: "Offerteconcept", permission: "finance" },
  create_invoice: { module: "facturen", endpoint: "invoices", title: "Factuurconcept", permission: "finance" },
  create_subscription: { module: "abonnementen", endpoint: "subscriptions", title: "Abonnement", permission: "finance" },
} as const;

type ActionKind = keyof typeof kinds;
const englishKindTitles: Record<ActionKind, string> = {
  create_organization: "Customer",
  create_contact: "Contact",
  create_lead: "Lead",
  create_note: "Internal note",
  create_project: "Project",
  create_task: "Task",
  create_time_entry: "Time entry",
  create_ticket: "Support ticket",
  create_quote: "Draft quote",
  create_invoice: "Draft invoice",
  create_subscription: "Subscription",
};
type PlannedAction = { kind: ActionKind; summary: string; organizationName: string | null; projectName: string | null; initial: Record<string, unknown>; warnings: string[] };
type Plan = { message: string; actions: PlannedAction[]; questions: string[] };
type SavedAction = { href: string; label: string };
const noteFields: Field[] = [
  { key: "organizationId", label: "Klantorganisatie", resource: "organizations", required: true },
  { key: "title", label: "Onderwerp", required: true },
  { key: "body", label: "Interne notitie", type: "textarea", required: true },
];

function reviewInitial(action: PlannedAction) {
  const definition = kinds[action.kind];
  const fields = action.kind === "create_note" ? noteFields : modules.find(module => module.key === definition.module)?.fields ?? [];
  const allowed = action.kind === "create_quote" || action.kind === "create_invoice"
    ? ["organizationId", "projectId", "title", "validUntil", "invoiceDate", "dueDate", "notes", "terms", "lines"]
    : fields.map(field => field.key);
  // An AI proposal can prefill a new form, never select an existing record to update.
  return { ...Object.fromEntries(allowed.filter(key => key in action.initial).map(key => [key, action.initial[key]])), sharedWithCustomer: false, automaticInvoicing: false };
}

export function VoiceAssistant({ user, onClose }: { user: PortalUser; onClose: () => void }) {
  const { t } = useLanguage();
  const [configured, setConfigured] = useState<boolean | null>(null);
  const [statusProblem, setStatusProblem] = useState("");
  const [problem, setProblem] = useState("");
  const [text, setText] = useState("");
  const [audio, setAudio] = useState<File | null>(null);
  const [audioUrl, setAudioUrl] = useState("");
  const [recording, setRecording] = useState(false);
  const [starting, setStarting] = useState(false);
  const [seconds, setSeconds] = useState(0);
  const [supportsRecording, setSupportsRecording] = useState(false);
  const [pending, setPending] = useState<"transcribe" | "plan" | null>(null);
  const [plan, setPlan] = useState<Plan | null>(null);
  const [activeAction, setActiveAction] = useState(0);
  const [saved, setSaved] = useState<Record<number, SavedAction>>({});
  const [skipped, setSkipped] = useState<number[]>([]);
  const [saving, setSaving] = useState(false);
  const alive = useRef(true);
  const recorder = useRef<MediaRecorder | null>(null);
  const stream = useRef<MediaStream | null>(null);
  const chunks = useRef<Blob[]>([]);
  const chunkSize = useRef(0);
  const discardRecording = useRef(false);
  const request = useRef<AbortController | null>(null);
  const actionTitle = (kind: ActionKind) => t(kinds[kind].title, englishKindTitles[kind]);

  const stopTracks = useCallback(() => {
    stream.current?.getTracks().forEach(track => track.stop());
    stream.current = null;
  }, []);
  const stopRecording = useCallback(() => {
    const current = recorder.current;
    if (current && current.state !== "inactive") {
      try { current.stop(); } catch { discardRecording.current = true; recorder.current = null; if (alive.current) setRecording(false); }
    }
    stopTracks();
    if (!current && alive.current) setRecording(false);
  }, [stopTracks]);

  useEffect(() => {
    alive.current = true;
    setSupportsRecording(Boolean(typeof MediaRecorder !== "undefined" && navigator.mediaDevices?.getUserMedia));
    const controller = new AbortController();
    api<{ configured: boolean }>("assistant/status", { signal: controller.signal })
      .then(status => setConfigured(status.configured))
      .catch(error => { if (!controller.signal.aborted) setStatusProblem(errorMessage(error)); });
    return () => {
      alive.current = false;
      controller.abort(); request.current?.abort();
      discardRecording.current = true;
      stopRecording(); chunks.current = [];
    };
  }, [stopRecording]);
  useEffect(() => {
    if (!audio) { setAudioUrl(""); return; }
    const url = URL.createObjectURL(audio); setAudioUrl(url);
    return () => URL.revokeObjectURL(url);
  }, [audio]);
  useEffect(() => {
    if (!recording) return;
    const start = Date.now();
    const clock = window.setInterval(() => setSeconds(Math.min(MAX_RECORDING_SECONDS, Math.floor((Date.now() - start) / 1000))), 500);
    const limit = window.setTimeout(stopRecording, MAX_RECORDING_SECONDS * 1000);
    return () => { window.clearInterval(clock); window.clearTimeout(limit); };
  }, [recording, stopRecording]);

  async function startRecording() {
    if (recording || starting || pending || saving) return;
    setProblem(""); setStarting(true);
    try {
      if (!navigator.mediaDevices?.getUserMedia || typeof MediaRecorder === "undefined") throw new Error(t("Deze browser ondersteunt opnemen niet. Upload een audiobestand of typ je opdracht.", "This browser does not support recording. Upload an audio file or type your instruction."));
      const media = await navigator.mediaDevices.getUserMedia({ audio: true });
      if (!alive.current) { media.getTracks().forEach(track => track.stop()); return; }
      stream.current = media;
      const mimeType = ["audio/webm;codecs=opus", "audio/mp4", "audio/webm"].find(type => MediaRecorder.isTypeSupported(type));
      const next = new MediaRecorder(media, mimeType ? { mimeType } : undefined);
      recorder.current = next; chunks.current = []; chunkSize.current = 0; discardRecording.current = false;
      next.ondataavailable = event => {
        if (!event.data.size || discardRecording.current) return;
        chunkSize.current += event.data.size;
        if (chunkSize.current > MAX_AUDIO_BYTES) {
          discardRecording.current = true; chunks.current = [];
          if (alive.current) setProblem(t("De opname is groter dan 15 MB. Maak een kortere opname.", "The recording exceeds 15 MB. Make a shorter recording."));
          stopRecording(); return;
        }
        chunks.current.push(event.data);
      };
      next.onstop = () => {
        stopTracks(); recorder.current = null;
        if (!alive.current) return;
        setRecording(false);
        if (!discardRecording.current && chunks.current.length) {
          const type = next.mimeType || "audio/webm";
          const extension = type.includes("mp4") ? "mp4" : "webm";
          const file = new File(chunks.current, `coffa-opdracht.${extension}`, { type });
          if (file.size) setAudio(file);
        }
        chunks.current = [];
      };
      next.onerror = () => {
        discardRecording.current = true; chunks.current = [];
        if (alive.current) setProblem(t("De opname is onderbroken. Probeer opnieuw of upload een audiobestand.", "Recording was interrupted. Try again or upload an audio file."));
        stopRecording();
      };
      setAudio(null); setSeconds(0); setRecording(true); next.start(1000);
    } catch (error) {
      stopTracks();
      if (alive.current) {
        setRecording(false); recorder.current = null; chunks.current = [];
        setProblem(error instanceof DOMException && (error.name === "NotAllowedError" || error.name === "SecurityError")
          ? t("Je microfoon is niet beschikbaar. Geef deze website microfoontoegang, upload een opname of typ je opdracht.", "Your microphone is unavailable. Allow microphone access for this website, upload a recording or type your instruction.")
          : errorMessage(error));
      }
    } finally { if (alive.current) setStarting(false); }
  }
  function chooseAudio(file: File | undefined) {
    setProblem("");
    if (!file) return;
    if (!/\.(mp3|mp4|m4a|mpeg|mpga|wav|webm|ogg|flac)$/i.test(file.name)) { setProblem(t("Kies een MP3-, MP4-, M4A-, WAV-, WebM-, OGG- of FLAC-bestand.", "Choose an MP3, MP4, M4A, WAV, WebM, OGG or FLAC file.")); return; }
    if (file.size < 1 || file.size > MAX_AUDIO_BYTES) { setProblem(t("Kies een audiobestand van maximaal 15 MB.", "Choose an audio file up to 15 MB.")); return; }
    setAudio(file);
  }
  async function transcribe() {
    if (!audio || pending || recording || saving) return;
    const controller = new AbortController(); request.current = controller;
    setPending("transcribe"); setProblem("");
    try {
      const body = new FormData(); body.set("file", audio);
      const result = await api<{ text: string }>("assistant/transcribe", { method: "POST", body, signal: controller.signal });
      if (alive.current) { setText(result.text); setPlan(null); setSaved({}); setSkipped([]); }
    } catch (error) { if (alive.current && !controller.signal.aborted) setProblem(errorMessage(error)); }
    finally { if (alive.current) setPending(null); }
  }
  async function workOut() {
    if (!text.trim() || pending || recording || saving) return;
    const controller = new AbortController(); request.current = controller;
    setPending("plan"); setProblem("");
    try {
      const result = await api<Plan>("assistant/plan", { method: "POST", body: { text: text.trim() }, signal: controller.signal });
      if (!Array.isArray(result.actions) || !Array.isArray(result.questions) || result.actions.some(action => !action || !Object.hasOwn(kinds, action.kind) || !action.initial || typeof action.initial !== "object" || Array.isArray(action.initial) || !Array.isArray(action.warnings))) throw new Error(t("De opdracht kon niet als een ondersteunde actie worden uitgewerkt. Formuleer je opdracht opnieuw.", "The instruction could not be turned into a supported action. Please rephrase it."));
      if (alive.current) { setPlan(result); setActiveAction(0); setSaved({}); setSkipped([]); }
    } catch (error) { if (alive.current && !controller.signal.aborted) setProblem(errorMessage(error)); }
    finally { if (alive.current) setPending(null); }
  }
  function nextAction(index: number, completed: Record<number, SavedAction> = saved, omitted: number[] = skipped) {
    const next = plan?.actions.findIndex((_, item) => item > index && !completed[item] && !omitted.includes(item));
    if (next != null && next >= 0) setActiveAction(next);
  }
  function markSaved(index: number, record?: Row) {
    if (!plan) return;
    const definition = kinds[plan.actions[index].kind];
    const detail = modules.find(module => module.key === definition.module)?.detail;
    const href = plan.actions[index].kind === "create_note"
      ? `/portaal/klanten${record?.organizationId ? `/${encodeURIComponent(String(record.organizationId))}` : ""}`
      : `/portaal/${definition.module}${detail && record?.id ? `/${encodeURIComponent(record.id)}` : ""}`;
    const completed = { ...saved, [index]: { href, label: actionTitle(plan.actions[index].kind) } };
    setSaved(completed); nextAction(index, completed);
  }
  async function saveAction(index: number, values: Record<string, unknown>) {
    if (!plan || saved[index] || saving) return;
    const action = plan.actions[index], definition = kinds[action.kind];
    if (user.role !== "SuperAdmin" || !can(user, definition.permission, true)) throw new Error(t("Je account heeft geen toegang tot deze actie.", "Your account does not have access to this action."));
    setSaving(true);
    try {
      const record = await api<Row>(definition.endpoint, { method: "POST", body: values });
      // A newly created customer can be used in later proposals from this instruction.
      if (action.kind === "create_organization" && record.id && values.name) {
        const name = String(values.name).trim().toLocaleLowerCase("nl-NL");
        setPlan(previous => previous ? { ...previous, actions: previous.actions.map((item, itemIndex) => itemIndex > index && !item.initial.organizationId && item.organizationName?.trim().toLocaleLowerCase("nl-NL") === name ? { ...item, initial: { ...item.initial, organizationId: record.id } } : item) } : previous);
      }
      markSaved(index, record);
    } finally { setSaving(false); }
  }

  const close = useCallback(() => { if (saving) { setProblem(t("De gegevens worden nog opgeslagen. Wacht even op de bevestiging.", "Your data is still being saved. Please wait for confirmation.")); return; } onClose(); }, [saving, onClose, t]);

  const busy = Boolean(pending || recording || starting || saving);
  const completedCount = Object.keys(saved).length;
  const current = plan?.actions[activeAction];
  const currentDefinition = current && kinds[current.kind];
  const currentModule = currentDefinition && modules.find(module => module.key === currentDefinition.module);
  const allowed = user.role === "SuperAdmin";

  return <Modal title={t("Vertel wat je wilt regelen", "Tell me what you need done")} onClose={close}>
    <div className="os-assistant">
      <p className="os-assistant-intro">{t("Van een gesproken opdracht naar ingevulde formulieren. Jij controleert de klant, bedragen en datums en slaat daarna op.", "Turn a spoken instruction into completed forms. Review the customer, amounts and dates before saving.")}</p>
      <div className="os-assistant-steps" aria-label={t("Hoe de assistent werkt", "How the assistant works")}><span><b>1</b> {t("Spreek of typ", "Speak or type")}</span><span><b>2</b> {t("Controleer", "Review")}</span><span><b>3</b> {t("Sla op", "Save")}</span></div>
      <Notice error>{problem || statusProblem}</Notice>
      {!allowed ? <Notice error>{t("De spraakassistent is beschikbaar voor de Founder.", "The voice assistant is available to the Founder.")}</Notice> : configured === null && !statusProblem ? <Loading /> : <>
        {configured === false && <Notice>{t("De OpenAI-koppeling is nog niet ingesteld. Stel", "The OpenAI integration is not configured. Set")} <code>OPENAI_API_KEY</code> {t("in op de API-service en start die opnieuw. Deel je sleutel niet in de chat.", "on the API service and restart it. Do not share your key in chat.")}</Notice>}
        <div className="os-assistant-capture">
          <div className="os-assistant-capture-heading"><div><span className="os-eyebrow">{t("JOUW OPDRACHT", "YOUR INSTRUCTION")}</span><h3>{t("Praat zoals je het zou uitleggen.", "Say it as you would explain it.")}</h3></div><span className={recording ? "os-recording-time is-recording" : "os-recording-time"} role="timer" aria-label={t("Opnameduur", "Recording duration")}>{String(Math.floor(seconds / 60)).padStart(2, "0")}:{String(seconds % 60).padStart(2, "0")}</span></div>
          <div className="os-heading-actions">
            {recording ? <button type="button" className="os-button primary" onClick={stopRecording}><span className="os-recording-dot" aria-hidden="true" /> {t("Opname stoppen", "Stop recording")}</button> : <button type="button" className="os-button primary" disabled={busy || !supportsRecording || !configured} onClick={startRecording}><span aria-hidden="true">◉</span> {starting ? t("Microfoon openen…", "Opening microphone…") : t("Inspreken", "Record voice")}</button>}
            <label className={`os-button secondary os-audio-upload${busy || !configured ? " is-disabled" : ""}`}>{t("Audiobestand kiezen", "Choose audio file")}<input type="file" accept={AUDIO_ACCEPT} disabled={busy || !configured} onChange={event => { chooseAudio(event.target.files?.[0]); event.currentTarget.value = ""; }} /></label>
          </div>
          <p className="os-muted">{t("Maximaal 2 minuten opnemen of een bestand van maximaal 15 MB kiezen. Audio en opdrachten worden via jouw server door OpenAI verwerkt. Een opname verstuur je pas met de knop hieronder.", "Record up to 2 minutes or choose a file up to 15 MB. Audio and instructions are processed by OpenAI through your server. A recording is only sent when you press the button below.")}</p>
          {!supportsRecording && <p className="os-muted">{t("Opnemen is in deze browser niet beschikbaar. Je kunt wel audio uploaden of je opdracht typen.", "Recording is not available in this browser. You can upload audio or type your instruction.")}</p>}
          {audio && <div className="os-assistant-audio"><div><strong>{audio.name}</strong><span>{(audio.size / 1024 / 1024).toLocaleString("nl-NL", { maximumFractionDigits: 1 })} MB · {t("klaar om om te zetten", "ready to transcribe")}</span></div>{audioUrl && <audio controls src={audioUrl} preload="metadata" aria-label={t("Luister je opname terug", "Listen to your recording")} />}<div className="os-heading-actions"><button type="button" className="os-button secondary" disabled={busy || !configured} onClick={transcribe}>{pending === "transcribe" ? t("Audio omzetten…", "Transcribing audio…") : t("Opname omzetten naar tekst", "Transcribe recording")}</button><button type="button" className="os-text-button" disabled={busy} onClick={() => setAudio(null)}>{t("Opname verwijderen", "Remove recording")}</button></div></div>}
          <label className="os-assistant-transcript">{audio ? t("Controleer de herkende tekst of typ je opdracht", "Review the transcript or type your instruction") : t("Of typ je opdracht", "Or type your instruction")}<textarea rows={5} maxLength={12000} disabled={busy} value={text} placeholder={t("Bijvoorbeeld: maak een conceptfactuur voor [klantnaam] voor vier uur ontwikkeling à 95 euro exclusief 21% btw. Maak ook een maandelijks hostingabonnement van 25 euro vanaf 1 december.", "For example: create a draft invoice for [customer name] for four hours of development at €95 excluding 21% VAT. Also create a monthly hosting subscription for €25 starting 1 December.")} onChange={event => { setText(event.target.value); setPlan(null); setSaved({}); setSkipped([]); }} /></label>
          <button type="button" className="os-button primary" disabled={busy || !configured || !text.trim()} onClick={workOut}>{pending === "plan" ? t("Opdracht uitwerken…", "Working out instruction…") : t("Opdracht uitwerken", "Process instruction")} →</button>
        </div>
        {plan && <div className="os-assistant-plan"><div className="os-panel-heading" aria-live="polite"><div><span className="os-eyebrow">{t("KLAAR VOOR JOUW CONTROLE","READY FOR YOUR REVIEW")}</span><h3>{plan.actions.length ? `${plan.actions.length} ${t("voorgestelde","proposed")} ${plan.actions.length === 1 ? t("actie","action") : t("acties","actions")}` : t("Nog wat informatie nodig","More information needed")}</h3></div>{completedCount > 0 && <span className="os-assistant-progress">{completedCount} {t("opgeslagen","saved")}</span>}</div><p>{plan.message}</p>{plan.questions.length > 0 && <div className="os-assistant-questions"><strong>{t("Controleer of vul aan","Review or complete")}</strong><ul>{plan.questions.map((question, index) => <li key={index}>{question}</li>)}</ul><p>{t("Je kunt dit in het formulier aanvullen of je opdracht hierboven aanpassen.","You can complete this in the form or edit your instruction above.")}</p></div>}
          {plan.actions.length > 0 && <ol className="os-assistant-actions">{plan.actions.map((action, index) => <li key={index}><button type="button" disabled={saving} aria-current={activeAction === index ? "step" : undefined} onClick={() => setActiveAction(index)}><span className={`os-assistant-action-number${saved[index] ? " is-saved" : ""}`}>{saved[index] ? "✓" : index + 1}</span><span><strong>{actionTitle(action.kind)}</strong><small>{action.summary}</small></span><span className="os-assistant-action-status">{saved[index] ? t("Opgeslagen","Saved") : skipped.includes(index) ? t("Overgeslagen","Skipped") : activeAction === index ? t("Controleren","Review") : t("Voorstel","Proposed")}</span></button></li>)}</ol>}
          {current && currentDefinition && <section className="os-assistant-review" aria-label={`${actionTitle(current.kind)} ${t("controleren","review")}`}>{saved[activeAction] ? <div className="os-assistant-saved"><span aria-hidden="true">✓</span><h3>{actionTitle(current.kind)} {t("opgeslagen.","saved.")}</h3><Link className="os-button secondary" href={saved[activeAction].href} onClick={onClose}>{t("Open","Open")} {currentDefinition.module === "notities" ? t("klantdossier","customer record") : actionTitle(current.kind).toLowerCase()} ↗</Link></div> : !can(user, currentDefinition.permission, true) ? <Notice error>{t("Je hebt geen rechten om dit onderdeel op te slaan.","You do not have permission to save this item.")}</Notice> : <><h3>{actionTitle(current.kind)} {t("controleren","review")}</h3>{(current.organizationName || current.projectName) && <p className="os-muted">{t("Genoemd in je opdracht:","Mentioned in your instruction:")} {[current.organizationName, current.projectName].filter(Boolean).join(" · ")}. {t("Controleer hieronder of de juiste koppeling is gekozen.","Check below that the correct link is selected.")}</p>}{current.warnings?.length > 0 && <div className="os-assistant-questions"><ul>{current.warnings.map((warning, index) => <li key={index}>{warning}</li>)}</ul></div>}{current.kind === "create_quote" || current.kind === "create_invoice" ? <FinanceEditor key={activeAction} user={user} kind={current.kind === "create_quote" ? "quotes" : "invoices"} initial={reviewInitial(current)} onBusyChange={setSaving} onSaved={record => markSaved(activeAction, record)} /> : <RecordForm key={activeAction} user={user} fields={current.kind === "create_note" ? noteFields : currentModule?.fields ?? []} initial={reviewInitial(current)} onSave={values => saveAction(activeAction, values)} submitLabel={`${actionTitle(current.kind)} ${t("opslaan","save")}`} />}<button type="button" className="os-text-button os-assistant-skip" disabled={saving} onClick={() => { const omitted = [...skipped, activeAction]; setSkipped(omitted); nextAction(activeAction, saved, omitted); }}>{t("Deze actie overslaan","Skip this action")}</button></>}</section>}
          {completedCount > 0 && <div className="os-assistant-completed"><strong>{t("Opgeslagen in Coffa OS","Saved in Coffa OS")}</strong>{Object.entries(saved).map(([index, item]) => <Link key={index} href={item.href} onClick={onClose}>✓ {item.label} {t("openen","open")} ↗</Link>)}</div>}
        </div>}
        <p className="os-assistant-footnote">{t("Offertes en facturen worden als concept opgeslagen. Definitief uitgeven, verzenden en toegang verlenen doe je afzonderlijk in Coffa OS.","Quotes and invoices are saved as drafts. Issue final documents, send them and grant access separately in Coffa OS.")}</p>
      </>}
    </div>
  </Modal>;
}
