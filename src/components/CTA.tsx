import Link from "next/link";

export default function CTA() {
  return <section className="cta-section" aria-labelledby="cta-heading">
    <div className="shell cta-inner">
      <div><p className="eyebrow">DE EERSTE STAP IS EEN GESPREK</p><h2 id="cta-heading">Wat zou jij graag<br /><em>makkelijker maken?</em></h2><p>Vertel waar je tegenaan loopt. Samen verkennen we wat kan helpen en wat een logische eerste stap is.</p></div>
      <div className="cta-action"><Link href="/contact" className="button button-primary" data-cta="footer">Bespreek jouw uitdaging <span aria-hidden="true">↗</span></Link><p>Rechtstreeks met David.<br />Een vraag is genoeg om te beginnen.</p></div>
    </div>
  </section>;
}
