"use client";

import { useRef, useState, type FormEvent } from "react";
import { validateContact, type ContactValues, type ContactErrors } from "@/lib/contact-validation";

const initialValues: ContactValues = { name: "", email: "", subject: "", message: "", website: "" };
class ContactRequestError extends Error {}

export default function ContactForm({ initialSubject = "" }: { initialSubject?: string }) {
  const [values, setValues] = useState({ ...initialValues, subject: initialSubject });
  const [errors, setErrors] = useState<ContactErrors>({});
  const [status, setStatus] = useState<"idle" | "sending" | "error" | "success">("idle");
  const [feedback, setFeedback] = useState("");
  const submissionId = useRef<string | null>(null);
  const feedbackRef = useRef<HTMLDivElement>(null);
  const formRef = useRef<HTMLFormElement>(null);
  const sending = status === "sending";

  function update(field: keyof ContactValues, value: string) {
    setValues(previous => ({ ...previous, [field]: value }));
    setErrors(previous => ({ ...previous, [field]: undefined }));
    submissionId.current = null;
  }

  function showFeedback(message: string) {
    setFeedback(message);
    requestAnimationFrame(() => feedbackRef.current?.focus());
  }

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (sending) return;
    const result = validateContact(values);
    setErrors(result.errors);
    if (!result.data) {
      setStatus("error");
      setFeedback("Controleer de gemarkeerde velden.");
      const firstField = Object.keys(result.errors)[0];
      formRef.current?.querySelector<HTMLElement>(`[name="${firstField}"]`)?.focus();
      return;
    }

    setStatus("sending");
    setFeedback("Je bericht wordt verstuurd…");
    const controller = new AbortController();
    const timeout = window.setTimeout(() => controller.abort(), 15000);
    try {
      submissionId.current ??= crypto.randomUUID();
      const response = await fetch("/api/contact", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ ...result.data, submissionId: submissionId.current }),
        signal: controller.signal,
      });
      const data = await response.json();
      if (!response.ok || data.ok !== true) {
        if (data.errors && typeof data.errors === "object") setErrors(data.errors);
        throw new ContactRequestError(typeof data.message === "string" ? data.message : "Versturen lukt op dit moment niet. Probeer het opnieuw of mail me rechtstreeks.");
      }
      setStatus("success");
      showFeedback("Bedankt! Je bericht is ontvangen. Ik lees je vraag en neem contact met je op via het opgegeven e-mailadres.");
    } catch (error) {
      setStatus("error");
      showFeedback(error instanceof ContactRequestError
        ? error.message
        : "Versturen lukt nu niet. Probeer het opnieuw of mail me rechtstreeks. Je ingevulde bericht blijft hier staan.");
    } finally {
      window.clearTimeout(timeout);
    }
  }

  if (status === "success") return <div className="contact-form form-success" ref={feedbackRef} tabIndex={-1} role="status">
    <p className="eyebrow">BERICHT ONTVANGEN</p>
    <h2>Dank je wel<br /><em>voor je bericht.</em></h2>
    <p>{feedback}</p>
    <p>Wil je nog iets aanvullen? Mail naar <a href="mailto:info@davidcoffa.nl">info@davidcoffa.nl</a>.</p>
    <button type="button" className="button-text" onClick={() => {
      setValues({ ...initialValues, subject: initialSubject });
      submissionId.current = null;
      setStatus("idle");
      setFeedback("");
      requestAnimationFrame(() => formRef.current?.querySelector<HTMLInputElement>("input[name=name]")?.focus());
    }}>Nog een bericht sturen <span aria-hidden="true">↗</span></button>
  </div>;

  return <form className="contact-form" method="post" action="/api/contact" ref={formRef} onSubmit={submit} noValidate aria-busy={sending}>
    <noscript><p>Mail je vraag rechtstreeks naar <a href="mailto:info@davidcoffa.nl">info@davidcoffa.nl</a> om contact op te nemen.</p></noscript>
    <div className="form-heading"><h2>Vertel waar je tegenaan loopt.</h2><p>Een paar zinnen is genoeg om te beginnen.</p></div>
    <div className="form-row">
      <label htmlFor="contact-name">Je naam
        <input id="contact-name" name="name" type="text" required autoComplete="name" maxLength={120} disabled={sending} value={values.name} onChange={event => update("name", event.target.value)} placeholder="Hoe heet je?" aria-invalid={Boolean(errors.name)} aria-describedby={errors.name ? "contact-name-error" : undefined} />
        {errors.name && <span className="field-error" id="contact-name-error">{errors.name}</span>}
      </label>
      <label htmlFor="contact-email">Je e-mail
        <input id="contact-email" name="email" type="email" required autoComplete="email" maxLength={160} disabled={sending} value={values.email} onChange={event => update("email", event.target.value)} placeholder="jij@bedrijf.nl" aria-invalid={Boolean(errors.email)} aria-describedby={errors.email ? "contact-email-error" : undefined} />
        {errors.email && <span className="field-error" id="contact-email-error">{errors.email}</span>}
      </label>
    </div>
    <label htmlFor="contact-message">Waar kan ik je mee helpen?
      <textarea id="contact-message" name="message" required minLength={10} maxLength={4000} rows={5} disabled={sending} value={values.message} onChange={event => update("message", event.target.value)} placeholder="Wat doe je nu, waar loop je vast en wat zou je graag anders zien?" aria-invalid={Boolean(errors.message)} aria-describedby={errors.message ? "contact-message-error" : "contact-message-hint"} />
      {errors.message ? <span className="field-error" id="contact-message-error">{errors.message}</span> : <span className="field-hint" id="contact-message-hint">Minimaal 10 tekens. Een uitgewerkt plan is niet nodig.</span>}
    </label>
    <label htmlFor="contact-subject">Onderwerp <span className="field-optional">(optioneel)</span>
      <input id="contact-subject" name="subject" type="text" maxLength={160} disabled={sending} value={values.subject} onChange={event => update("subject", event.target.value)} placeholder="Bijvoorbeeld: website, automatisering of klantportaal" aria-invalid={Boolean(errors.subject)} aria-describedby={errors.subject ? "contact-subject-error" : undefined} />
      {errors.subject && <span className="field-error" id="contact-subject-error">{errors.subject}</span>}
    </label>
    <div className="form-honeypot" aria-hidden="true"><label htmlFor="contact-website">Website<input id="contact-website" name="website" type="text" tabIndex={-1} autoComplete="off" value={values.website} onChange={event => update("website", event.target.value)} /></label></div>
    <div className={`form-feedback${status === "error" ? " form-error" : ""}`} role={status === "error" ? "alert" : "status"} ref={feedbackRef} tabIndex={-1}>{feedback}</div>
    <div className="form-actions"><button type="submit" className="button button-primary" disabled={sending}>{sending ? "Bericht versturen…" : "Verstuur mijn bericht"} <span aria-hidden="true">↗</span></button><p className="form-disclaimer">Liever rechtstreeks? <a href="mailto:info@davidcoffa.nl">Mail mij</a> of <a href="tel:+31683439000">bel mij</a>.</p></div>
    <p className="form-disclaimer">Ik gebruik je gegevens om je vraag te beantwoorden.</p>
  </form>;
}
