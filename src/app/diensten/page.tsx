import type { Metadata } from "next";
import Link from "next/link";
import SectionHeading from "@/components/SectionHeading";
import CTA from "@/components/CTA";
import WorkflowExplorer from "@/components/WorkflowExplorer";
import { services } from "@/lib/content";

export const metadata: Metadata = {
  title: "Website laten maken, AI & automatisering voor mkb",
  description: "Laat een website maken of automatiseer bedrijfsprocessen met AI. David Coffa bouwt websites, portalen en slimme software voor mkb-bedrijven.",
  alternates: { canonical: "/diensten" },
};

export default function DienstenPage() {
  return <>
    <section className="page-hero shell">
      <p className="eyebrow">DIENSTEN / SOFTWARE VOOR JOUW BEDRIJF</p>
      <h1>Een website die werkt.<br /><em>Processen die meegaan.</em></h1>
      <p>Een website laten maken, terugkerend werk automatiseren of AI toepassen in je bedrijf? Ik help mkb-ondernemers met websites, portalen en software die passen bij hun dagelijkse praktijk.</p>
      <div className="hero-ctas">
        <Link href="/contact" className="button button-primary">Bespreek jouw uitdaging <span aria-hidden="true">↗</span></Link>
        <Link href="#diensten" className="button-text">Bekijk de mogelijkheden <span aria-hidden="true">↓</span></Link>
      </div>
      <div className="page-meta"><span>WEBSITES</span><span>AUTOMATISERING</span><span>PORTALEN</span><span>DATA</span></div>
    </section>

    <section className="section shell" id="diensten">
      <SectionHeading eyebrow="WAT WIL JE VERBETEREN?" title={<>Van jouw vraag<br />naar <em>een passende oplossing.</em></>} aside="We bepalen eerst wat je wilt bereiken. Daarna maken we duidelijk wat er gebouwd wordt en welke eerste stap daarbij past." />
      <div className="services-detail">
        {services.map((service) => <article className="service-detail-card" id={service.id} key={service.id}>
          <div className="service-detail-head"><span>{service.number} / DIENST</span><span className="detail-arrow" aria-hidden="true">↗</span></div>
          <h2>{service.title}</h2>
          <p>{service.summary}</p>
          <div className="deliver-list">{service.delivers.map((deliver) => <span key={deliver}>✓ {deliver}</span>)}</div>
          <Link href={`/contact?onderwerp=${encodeURIComponent(service.title)}`} className="card-contact" aria-label={`Bespreek jouw uitdaging: ${service.title}`}>Bespreek jouw uitdaging <span aria-hidden="true">↗</span></Link>
        </article>)}
      </div>
    </section>

    <section className="section section-tinted">
      <div className="shell">
        <SectionHeading eyebrow="EEN HERKENBAAR VOORBEELD" title={<>Zo kan terugkerend werk<br /><em>eenvoudiger worden.</em></>} aside="Kies een proces en bekijk de stappen. De getoonde tijdwinst is een rekenvoorbeeld; wat er mogelijk is, onderzoeken we voor jouw situatie." />
        <WorkflowExplorer showHeading={false} />
      </div>
    </section>

    <section className="section shell explore-more-automations">
      <p className="eyebrow">ZOEK JOUW SITUATIE OP</p>
      <h2>Van aanvragen tot planning.<br /><em>Waar zit jouw herhaalwerk?</em></h2>
      <p>Bekijk voorbeelden per bedrijfsgebied: welke informatie nodig is, wat software kan overnemen en hoe je de mogelijke tijdwinst berekent.</p>
      <Link className="button-text" href="/automatiseringen">Bekijk automatiseringsvoorbeelden <span aria-hidden="true">↗</span></Link>
    </section>
    <CTA />
  </>;
}
