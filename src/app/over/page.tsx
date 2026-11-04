import type { Metadata } from "next";
import Image from "next/image";
import Link from "next/link";
import CTA from "@/components/CTA";

export const metadata: Metadata = {
  title: "Over David Coffa: software engineer & AI developer",
  description: "David Coffa is software engineer bij de Tweede Kamer en bouwt al 7 jaar aan zijn softwarebedrijf. Sinds 2023 ontwikkelt hij AI-automatiseringen voor bedrijven.",
  alternates: { canonical: "/over" },
};

export default function OverPage() {
  return <>
    <section className="page-hero shell">
      <p className="eyebrow">DE PERSOON ACHTER DE CODE</p>
      <h1>Jouw aanspreekpunt.<br /><em>Ook jouw developer.</em></h1>
      <p>Hoi, ik ben David. Ik denk mee over jouw vraag en bouw de software die erbij past. Van het eerste gesprek tot de website, het portaal of de koppeling die je gebruikt.</p>
      <div className="hero-ctas">
        <Link href="/contact" className="button button-primary">Bespreek jouw uitdaging <span aria-hidden="true">↗</span></Link>
        <Link href="/projecten" className="button-text">Bekijk mijn werk <span aria-hidden="true">↗</span></Link>
      </div>
    </section>

    <section className="section shell">
      <div className="about-grid">
        <aside className="about-profile-card">
          <Image className="about-profile-photo" src="/david-coffa.PNG" alt="David Coffa" width={200} height={200} priority />
          <p className="about-profile-name">David Coffa</p>
          <p className="about-profile-role">Software engineer · ondernemer · AI</p>
          <a href="https://www.linkedin.com/in/davidcoffa" target="_blank" rel="noopener noreferrer">Bekijk mijn LinkedIn-profiel <span aria-hidden="true">↗</span></a>
        </aside>
        <div className="about-body">
          <p className="eyebrow">EVEN VOORSTELLEN</p>
          <h2>Software bouwen.<br /><em>Blijven doorleren.</em></h2>
          <p>Ik ben Software Engineer bij de Tweede Kamer der Staten-Generaal en daarnaast ondernemer. Al 7 jaar bouw ik aan mijn softwarebedrijf. Ik help organisaties met websites, slimme dashboards en software die aansluit op hun dagelijkse werk.</p>
          <p>Sinds 2023 ontwikkel ik AI-automatiseringen voor bedrijven. Ik onderzoek hoe AI processen kan ondersteunen — bijvoorbeeld door informatie te verwerken of terugkerend werk voor te bereiden — met aandacht voor kwaliteit, privacy en menselijke controle.</p>
          <p>In projecten werk ik rechtstreeks met de mensen die de oplossing gaan gebruiken. Eerst begrijp ik het proces, daarna maak ik de techniek praktisch en toets ik samen of die echt helpt.</p>
          <div className="about-tech"><span>IN MIJN EDITOR</span><p>Next.js · React · TypeScript · C# / .NET · Python · PostgreSQL</p></div>
        </div>
      </div>
    </section>

    <section className="section section-tinted education-section">
      <div className="shell">
        <div className="education-heading">
          <p className="eyebrow">WERK & OPLEIDING</p>
          <h2>Gebouwd in de praktijk.<br /><em>Verdiept in AI.</em></h2>
          <p>Van softwareontwikkeling en ondernemerschap naar een verdere specialisatie in kunstmatige intelligentie.</p>
        </div>
        <ol className="career-timeline">
          <li className="timeline-item">
            <span className="timeline-period">2026–HEDEN</span>
            <div><h3>Software Engineer</h3><p>Tweede Kamer der Staten-Generaal</p></div>
            <span className="timeline-type">WERK</span>
          </li>
          <li className="timeline-item">
            <span className="timeline-period">2026–HEDEN</span>
            <div><h3>Master Artificial Intelligence</h3><p>Open Universiteit</p><p>Verdieping in kunstmatige intelligentie naast mijn werk en projecten.</p></div>
            <span className="timeline-type">STUDIE</span>
          </li>
          <li className="timeline-item">
            <span className="timeline-period">2024–2026</span>
            <div><h3>Bachelor ICT — specialisatie</h3><p>Fontys ICT Lab, Eindhoven</p><p>De laatste twee jaar van mijn bachelor, met meer ruimte voor specialisatie in IT.</p></div>
            <span className="timeline-type">STUDIE</span>
          </li>
          <li className="timeline-item">
            <span className="timeline-period">2023–JAN. 2026</span>
            <div><h3>Werkzaam bij Uniware</h3><p>Uniware Computer Systems B.V.</p></div>
            <span className="timeline-type">WERK</span>
          </li>
          <li className="timeline-item">
            <span className="timeline-period">2022–2024</span>
            <div><h3>Bachelor ICT — eerste twee jaar</h3><p>Hogeschool Rotterdam</p></div>
            <span className="timeline-type">STUDIE</span>
          </li>
          <li className="timeline-item">
            <span className="timeline-period">2019–HEDEN</span>
            <div><h3>Eigen softwarebedrijf</h3><p>Ondernemer en software developer</p><p>Sinds 2023 ook gericht op AI-automatiseringen voor bedrijven.</p></div>
            <span className="timeline-type">ONDERNEMEN</span>
          </li>
        </ol>
      </div>
    </section>
    <CTA />
  </>;
}
