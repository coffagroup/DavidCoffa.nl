"use client";

import { useMemo, useState } from "react";
import Link from "next/link";
import { useLanguage } from "@/components/LanguageProvider";
import { englishCopy } from "@/lib/english-copy";
import {
  automationCases, automationCategories, estimatedMonthlyHours,
  type AutomationCategory, type AutomationCase,
} from "@/lib/automation-cases";

function CaseCard({ item, index, language }: { item: AutomationCase; index: number; language: "nl" | "en" }) {
  const format = (value: number) => new Intl.NumberFormat(language === "en" ? "en-US" : "nl-NL", { maximumFractionDigits: 1 }).format(value);
  const hours = estimatedMonthlyHours(item);
  const afterPct = Math.max(5, Math.round(item.afterMinutes / item.beforeMinutes * 100));
  return <article className="automation-card">
    <div className="automation-card-top"><span className="automation-tag">{item.category}</span><span className="automation-index">/ {String(index + 1).padStart(2, "0")}</span></div>
    <h3>{item.title}</h3>
    <p className="automation-problem">{item.problem}</p>
    <div className="automation-mini-flow" aria-label={`Datastroom van ${item.title}`}>
      <div className="automation-flow-step"><small>01 / BRONNEN</small><div className="automation-source-pills">{item.sources.map(source => <span key={source}>{source}</span>)}</div></div>
      <span className="automation-flow-arrow" aria-hidden="true">→</span>
      <div className="automation-flow-step automation-data-step"><small>02 / STRUCTUUR</small><b>{item.model}</b></div>
      <span className="automation-flow-arrow" aria-hidden="true">→</span>
      <div className="automation-flow-step"><small>03 / ACTIE</small>{item.actions.map(action => <span key={action} className="automation-action">✓ {action}</span>)}</div>
    </div>
    <div className="automation-card-bottom">
      <div><div className="automation-quick-benefit">{item.outcome}</div><span className="automation-assumption">Voorbeeld: {item.weeklyVolume}× per week · {item.beforeMinutes} → {item.afterMinutes} min</span></div>
      <div className="automation-hours" title="Indicatieve besparing bij het genoemde aantal handelingen per week"><strong>≈ {format(hours)} u</strong><span>per maand*</span></div>
    </div>
    <div className="automation-bar" aria-hidden="true"><span style={{ width: `${afterPct}%` }}/></div>
  </article>;
}

export default function AutomationCatalog({ limit, title }: { limit?: number; title?: string }) {
  const { language } = useLanguage();
  const [category, setCategory] = useState<AutomationCategory | "Alles">("Alles");
  const [query, setQuery] = useState("");
  const featuredIds = ["leads", "inkoopfacturen", "planning", "klantvragen", "voorraad", "kpi"];
  const shown = useMemo(() => {
    const sourceItems = limit ? featuredIds.map(id => automationCases.find(item => item.id === id)!).filter(Boolean) : automationCases;
    const locale = language === "en" ? "en-US" : "nl-NL";
    const clean = query.trim().toLocaleLowerCase(locale);
    return sourceItems.filter(item => (category === "Alles" || item.category === category)
      && (!clean || [item.title, item.problem, item.category, item.model, item.outcome, ...item.sources, ...item.actions]
        .flatMap(value => [value, englishCopy[value] ?? value]).join(" ").toLocaleLowerCase(locale).includes(clean)));
  }, [category, query, limit, language]);
  const visible = limit ? shown.slice(0, limit) : shown;

  return <div className="automation-library">
    <div className="automation-catalog-intro">
      <div><p className="eyebrow">PRAKTISCHE TOEPASSINGEN</p><h2>{title ?? "Wat kan er eigenlijk allemaal automatisch?"}</h2><p>Herkenbare taken uit vrijwel iedere mkb-onderneming. Niet beginnen met de tool, maar met de bron, de structuur en de actie die nu nog handmatig gebeurt.</p></div>
      <div className="automation-count"><strong>{automationCases.length}</strong><span>voorbeelden uit<br />9 bedrijfsgebieden</span></div>
    </div>
    {!limit && <div className="automation-controls">
      <div className="automation-filter-row" role="group" aria-label="Filter op bedrijfsgebied">
        {(["Alles", ...automationCategories] as const).map(value => <button key={value} type="button" className={category === value ? "active" : ""} onClick={() => setCategory(value)}>{value}</button>)}
      </div>
      <label className="automation-search"><span className="automation-search-icon" aria-hidden="true">⌕</span><span className="sr-only">Zoeken in automatiseringen</span><input type="search" value={query} placeholder="Zoek: offertes, e-mail, voorraad…" onChange={event => setQuery(event.target.value)} /></label>
    </div>}
    {!limit && <div className="automation-results-label"><span>{shown.length} voorbeelden gevonden</span><span>BRON → STRUCTUUR → ACTIE → TIJD</span></div>}
    {visible.length > 0 ? <div className="automation-grid">{visible.map((item, i) => <CaseCard key={item.id} item={item} index={i} language={language}/>)}</div> : <div className="automation-empty"><strong>Geen voorbeelden gevonden.</strong><p>Probeer een andere zoekterm of kies ‘Alles’.</p><button type="button" onClick={() => {setQuery("");setCategory("Alles");}}>Toon alle voorbeelden</button></div>}
    <p className="automation-disclaimer">* Dit zijn fictieve rekenvoorbeelden, geen gemeten klantresultaten of garanties. Tijden per handeling, volumes en besparingen verschillen per bedrijf. Controle, uitzonderingen en ontwikkel- en onderhoudstijd zijn niet meegerekend.</p>
    {limit && <Link className="automation-more" href="/automatiseringen">Bekijk alle {automationCases.length} automatiseringen <span aria-hidden="true">↗</span></Link>}
  </div>;
}
