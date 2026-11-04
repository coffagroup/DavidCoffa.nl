import type { Metadata } from "next";
import Link from "next/link";
import AutomationCatalog from "@/components/AutomationCatalog";
import ProcessMap from "@/components/ProcessMap";
import WorkflowExplorer from "@/components/WorkflowExplorer";
import CTA from "@/components/CTA";

export const metadata: Metadata = {
  title: "Automatiseringen voor het mkb — concrete voorbeelden",
  description: "Bedrijfsprocessen automatiseren met AI of slimme software? Bekijk praktische voorbeelden voor administratie, verkoop, planning en klantcontact bij mkb-bedrijven.",
  alternates: { canonical: "/automatiseringen" },
};

export default function AutomatiseringenPage() {
  return <>
    <section className="page-hero shell automation-page-hero">
      <p className="eyebrow">MKB / MINDER HANDMATIG WERK</p>
      <h1>Bedrijfsprocessen automatiseren?<br /><em>Begin met herhaalwerk.</em></h1>
      <p>Gegevens overtypen, offertes opvolgen of steeds dezelfde informatie opzoeken: het telt op. Ik verbind je gegevens en systemen, zodat software terugkerend werk kan overnemen.</p>
      <div className="hero-ctas">
        <Link className="button button-primary" href="/contact">Bespreek jouw uitdaging <span aria-hidden="true">↗</span></Link>
        <Link className="button-text" href="#voorbeelden">Zoek een herkenbaar voorbeeld <span aria-hidden="true">↓</span></Link>
      </div>
      <div className="automation-hero-note"><span>01 / DATA IN KAART</span><span aria-hidden="true">→</span><span>02 / ORDENEN</span><span aria-hidden="true">→</span><span>03 / AUTOMATISEREN</span></div>
    </section>

    <section className="section shell automation-main" id="voorbeelden"><AutomationCatalog /></section>

    <section className="section section-tinted automation-map-section">
      <div className="shell">
        <div className="automation-two-column">
          <div>
            <p className="eyebrow">HOE DAT WERKT</p>
            <h2>Van losse gegevens naar <em>een logisch proces.</em></h2>
            <p>We brengen in kaart waar gegevens ontstaan, bepalen wat de juiste versie is en verbinden systemen. Daarna kan software terugkerend werk afhandelen, met mensen op de momenten waarop controle nodig is.</p>
            <Link className="button-text" href="/werkwijze">Bekijk mijn werkwijze <span aria-hidden="true">↗</span></Link>
          </div>
          <ProcessMap compact />
        </div>
      </div>
    </section>

    <section className="section shell">
      <div className="automation-example-heading">
        <div><p className="eyebrow">ZELF REKENEN</p><h2>Wat betekent één<br /><em>slimmer proces?</em></h2></div>
        <p>Bekijk een proces en pas het aantal handelingen aan. Het resultaat is een rekenvoorbeeld, geen gemeten klantbesparing.</p>
      </div>
      <WorkflowExplorer showHeading={false} />
    </section>
    <CTA />
  </>;
}
