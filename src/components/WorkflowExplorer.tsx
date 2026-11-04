"use client";

import { useState } from "react";
import { workflows } from "@/lib/workflows";

export default function WorkflowExplorer({ showHeading = true }: { showHeading?: boolean }) {
  const [selected, setSelected] = useState(workflows[0].id);
  const workflow = workflows.find((item) => item.id === selected) ?? workflows[0];
  const [perWeek, setPerWeek] = useState(workflow.defaultWeekly);
  const originalWeekly = perWeek * workflow.originalMinutes;
  const improvedWeekly = perWeek * workflow.improvedMinutes;
  const savedWeekly = originalWeekly - improvedWeekly;
  const savedMonthly = savedWeekly * (52 / 12) / 60;
  const savedPct = Math.round((savedWeekly / originalWeekly) * 100);
  const hours = new Intl.NumberFormat("nl-NL", { maximumFractionDigits: 1 }).format(savedMonthly);

  function selectFlow(id: string) {
    const next = workflows.find((item) => item.id === id);
    if (!next) return;
    setSelected(id);
    setPerWeek(next.defaultWeekly);
  }

  return (
    <div className="workflow-explorer">
      {showHeading && <div className="workflow-intro"><div><p className="eyebrow">INTERACTIEVE VOORBEELDEN</p><h3>Wat zou jij kunnen automatiseren?</h3></div><p>Dit zijn geen ingewikkelde toekomstscenario’s. Het zijn terugkerende handelingen die je eerst structureert en daarna kunt versnellen.</p></div>}
      <div className="workflow-tabs" role="tablist" aria-label="Kies een voorbeeldproces">
        {workflows.map((item, i) => <button type="button" role="tab" aria-selected={workflow.id === item.id} className={workflow.id === item.id ? "selected" : ""} key={item.id} onClick={() => selectFlow(item.id)}><span>{String(i + 1).padStart(2, "0")}</span>{item.label}</button>)}
      </div>
      <div className="workflow-panel" role="tabpanel" aria-label={`${workflow.label} procesvoorbeeld`}>
        <div className="workflow-panel-head"><div><span className="small-label">VOORBEELD / {workflow.label.toUpperCase()}</span><h3>{workflow.title}</h3><p>{workflow.problem}</p></div><span className="demo-indicator"><i></i> INTERACTIEVE DEMO</span></div>
        <div className="workflow-stages">
          <div className="workflow-stage"><div className="stage-number">01 <span>BRONNEN</span></div><strong>Wat komt er binnen?</strong><div className="stage-content">{workflow.sources.map((source) => <div className="stage-source" key={source}><span>◈</span>{source}<small>↗</small></div>)}</div></div>
          <div className="stage-arrow" aria-hidden="true">→</div>
          <div className="workflow-stage structurize"><div className="stage-number">02 <span>DATA MODEL</span></div><strong>Maak er structuur van.</strong><div className="schema-mini"><div className="schema-mini-bar"><span>record.json</span><span>● ● ●</span></div>{workflow.fields.map((field, i) => <div key={field}><span>{field}</span><b>{i === 0 ? "ID_001" : i === 1 ? "gekoppeld" : i === 2 ? "actief" : "bekend"}</b></div>)}</div></div>
          <div className="stage-arrow" aria-hidden="true">→</div>
          <div className="workflow-stage"><div className="stage-number">03 <span>ACTIE</span></div><strong>Laat software het werk doen.</strong><div className="stage-content">{workflow.actions.map((action) => <div className="stage-result" key={action}><span>✓</span>{action}</div>)}</div></div>
        </div>
        <div className="workflow-outcome"><div><span>HET RESULTAAT</span><strong>{workflow.result}</strong></div><span>✓ ROUTINEWERK VERMINDERD</span></div>
      </div>
      <div className="savings-panel">
        <div className="savings-copy"><p className="eyebrow">WAT LEVERT DAT OP?</p><h3>Maak de tijdwinst <em>concreet.</em></h3><p>Pas het aantal handelingen aan. De tijden zijn rekenvoorbeelden, geen metingen of garanties. Je werkelijke besparing hangt af van je bedrijf en het proces.</p><label htmlFor="weekly-range">Hoe vaak doe je dit per week? <strong>{perWeek}×</strong></label><input id="weekly-range" type="range" min="5" max="100" step="5" value={perWeek} onChange={(e) => setPerWeek(Number(e.target.value))}/><div className="range-ends"><span>5 keer</span><span>100 keer</span></div></div>
        <div className="savings-visual"><div className="savings-kicker">POTENTIËLE TIJDWINST / INDICATIE</div><div className="savings-number"><span>{hours}</span><div>uur<br />per maand*</div></div><div className="saving-bars"><div><div className="bar-label"><span>Voorheen</span><b>{Math.round(originalWeekly / 60 * 10) / 10} u / week</b></div><div className="bar-track"><div className="bar-before"></div></div></div><div><div className="bar-label"><span>Na automatisering</span><b>{Math.round(improvedWeekly / 60 * 10) / 10} u / week</b></div><div className="bar-track"><div className="bar-after" style={{width: `${100 - savedPct}%`}}></div></div></div></div><div className="saving-caption">* 52 weken / 12 maanden; exclusief ontwikkel-, controle- en onderhoudstijd.</div></div>
      </div>
    </div>
  );
}
