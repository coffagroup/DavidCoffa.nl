export default function ProcessMap({ compact = false }: { compact?: boolean }) {
  return (
    <div className={`process-map ${compact ? "compact" : ""}`} aria-label="Schema: databronnen worden gestructureerd en leiden tot automatische acties">
      <div className="map-topline"><span className="dot-live"></span> <span>INZICHT IN JE BEDRIJF</span><span className="map-version">DATA FLOW / 001</span></div>
      <div className="map-title">Van losse informatie naar één slim proces<span>.</span></div>
      <div className="map-canvas">
        <div className="map-column map-sources">
          <div className="map-label">01 / BRONNEN</div>
          <div className="source-node"><span className="node-icon">@</span><div><b>E-mail</b><small>Nieuwe aanvragen</small></div><span className="node-dots">···</span></div>
          <div className="source-node"><span className="node-icon">▤</span><div><b>Excel</b><small>Losse lijsten</small></div><span className="node-dots">···</span></div>
          <div className="source-node"><span className="node-icon">⌘</span><div><b>Website</b><small>Formulieren</small></div><span className="node-dots">···</span></div>
          <div className="source-node"><span className="node-icon">◫</span><div><b>CRM / API</b><small>Klantgegevens</small></div><span className="node-dots">···</span></div>
        </div>
        <div className="map-connector"><span></span><b>→</b><span></span></div>
        <div className="map-column map-core">
          <div className="map-label">02 / STRUCTUUR</div>
          <div className="core-card"><div className="core-icon">▦</div><b>Eén gegevensmodel</b><p>Opschonen, combineren & valideren</p><div className="core-table"><div><span>klant_id</span><em>1024</em></div><div><span>status</span><em>in behandeling</em></div><div><span>eigenaar</span><em>team sales</em></div></div><div className="core-check"><span>✓</span> BRONNEN VERBONDEN</div></div>
        </div>
        <div className="map-connector"><span></span><b>→</b><span></span></div>
        <div className="map-column map-actions">
          <div className="map-label">03 / AUTOMATISCH</div>
          <div className="action-node"><span>✓</span><div><b>Taak aangemaakt</b><small>Direct toegewezen</small></div></div>
          <div className="action-node"><span>✓</span><div><b>Klant bevestigd</b><small>Geen handwerk</small></div></div>
          <div className="action-node"><span>✓</span><div><b>Overzicht bijgewerkt</b><small>Altijd actueel</small></div></div>
          <div className="action-time">Minder kopiëren.<br /><strong>Meer tijd voor je werk.</strong></div>
        </div>
      </div>
      <div className="map-bottom"><span>DATA EERST. AUTOMATISERING DAARNA.</span><span>SCHEMATISCHE WEERGAVE</span></div>
    </div>
  );
}
