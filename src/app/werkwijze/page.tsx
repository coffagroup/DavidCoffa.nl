import type { Metadata } from "next";
import Link from "next/link";
import SectionHeading from "@/components/SectionHeading";
import WorkflowExplorer from "@/components/WorkflowExplorer";
import ProcessMap from "@/components/ProcessMap";
import CTA from "@/components/CTA";

export const metadata: Metadata = {
  title: "Zo bouw ik websites & automatisering voor mkb",
  description: "Bekijk hoe David Coffa samen met mkb-bedrijven websites, portalen en automatiseringen ontwikkelt: van eerste vraag tot werkende oplossing.",
  alternates: { canonical: "/werkwijze" },
};

const phases = [
  { n: "01", name: "Jouw vraag begrijpen", subtitle: "Doel & dagelijkse praktijk", description: "Wat wil je verbeteren voor je klanten of je team? We bekijken hoe je nu werkt, waar informatie vandaan komt en welke stappen aandacht vragen.", outcome: "Doel + knelpunten" },
  { n: "02", name: "De oplossing concreet maken", subtitle: "Structuur & afspraken", description: "We bepalen wat de website of software moet kunnen, welke informatie nodig is en wat de eerste versie bevat. Zo wordt duidelijk wat we gaan bouwen.", outcome: "Aanpak + afbakening" },
  { n: "03", name: "Bouwen en samen bekijken", subtitle: "Ontwikkeling & controle", description: "Ik bouw de afgesproken oplossing. We bekijken of die aansluit op het gebruik: van een aanvraag op je website tot de uitzonderingen in een werkproces.", outcome: "Werkende oplossing" },
  { n: "04", name: "Gebruiken en verbeteren", subtitle: "Praktijk & vervolg", description: "We kijken hoe de oplossing in de praktijk werkt, wat beter kan en of een volgende stap waarde toevoegt. Bij automatisering vergelijken we ook tijd en kwaliteit.", outcome: "Inzicht + volgende stappen" },
];

export default function WerkwijzePage() {
  return <>
    <section className="page-hero shell">
      <p className="eyebrow">WERKWIJZE / RECHTSTREEKS MET DE BOUWER</p>
      <h1>Van eerste vraag<br />naar <em>software die past.</em></h1>
      <p>Je hoeft nog geen technische oplossing te kennen. Vertel wat je wilt verbeteren. Samen maken we de vraag concreet, waarna ik de website, het portaal of de automatisering bouw.</p>
      <div className="hero-ctas">
        <Link className="button button-primary" href="/contact">Bespreek jouw uitdaging <span aria-hidden="true">↗</span></Link>
        <Link className="button-text" href="#stappen">Bekijk de vier stappen <span aria-hidden="true">↓</span></Link>
      </div>
    </section>

    <section className="section shell" id="stappen">
      <SectionHeading eyebrow="VIER OVERZICHTELIJKE STAPPEN" title={<>Begrijpen. Afspreken.<br /><em>Bouwen. Verbeteren.</em></>} aside="Je spreekt bij elke stap met mij. We maken duidelijk wat er gebeurt en wat het oplevert, ook zonder technische achtergrond." />
      <div className="phases">
        {phases.map((phase) => <article className="phase" key={phase.n}>
          <div className="phase-id">{phase.n}</div>
          <div><span className="phase-subtitle">{phase.subtitle}</span><h3>{phase.name}</h3></div>
          <p>{phase.description}</p>
          <div className="phase-output"><span>OP TE LEVEREN</span><b>{phase.outcome}</b></div>
        </article>)}
      </div>
    </section>

    <section className="section section-tinted">
      <div className="shell">
        <SectionHeading eyebrow="BIJ DATA & AUTOMATISERING" title={<>Eerst overzicht.<br /><em>Dan verbinden.</em></>} aside="Bij automatisering brengen we eerst bronnen en afspraken in kaart. Daarna bepalen we welke stappen software kan overnemen en waar menselijke controle nodig blijft." />
        <ProcessMap />
      </div>
    </section>

    <section className="section shell">
      <SectionHeading eyebrow="REKEN EEN VOORBEELD DOOR" title={<>Wat kan één<br /><em>slimmer proces opleveren?</em></>} aside="Kies een situatie en pas het aantal handelingen aan. De besparingen zijn illustratieve berekeningen die helpen om jouw vraag concreet te maken." />
      <WorkflowExplorer showHeading={false} />
    </section>

    <section className="section shell start-small">
      <h2>Je eerste stap mag klein zijn.</h2>
      <p>Vertel waar je website of je werkproces vastloopt. We verkennen wat je wilt bereiken en welke eerste verbetering daarbij past.</p>
      <Link className="button button-primary" href="/contact">Bespreek jouw uitdaging <span aria-hidden="true">↗</span></Link>
    </section>
    <CTA />
  </>;
}
