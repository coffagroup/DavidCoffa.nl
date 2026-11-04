import type { Metadata } from "next";
import Image from "next/image";
import Link from "next/link";
import SectionHeading from "@/components/SectionHeading";
import ProcessMap from "@/components/ProcessMap";
import ProjectArtwork from "@/components/ProjectArtwork";
import IntegrationMarquee from "@/components/IntegrationMarquee";
import CTA from "@/components/CTA";

export const metadata: Metadata = {
  title: "Website laten maken voor mkb?",
  description: "Een website laten maken of bedrijfsprocessen automatiseren? David Coffa bouwt websites, AI-oplossingen en slimme dashboards voor mkb-bedrijven.",
  alternates: { canonical: "/" },
};

const challenges = [
  { number: "01", title: "Steeds dezelfde gegevens overtypen?", description: "Je werkt met mail, Excel en losse systemen. Dezelfde informatie invoeren kost elke keer opnieuw tijd.", outcome: "Laat je systemen samenwerken.", href: "/diensten#automation", link: "Ontdek automatisering" },
  { number: "02", title: "Het overzicht zit vooral in je hoofd?", description: "Klantgegevens, documenten en taken staan verspreid. Je bent aan het zoeken terwijl je verder wilt.", outcome: "Breng je werk samen op één plek.", href: "/diensten#platforms", link: "Bekijk portalen & dashboards" },
  { number: "03", title: "Je website vertelt niet het hele verhaal?", description: "Je levert goed werk. Nu nog een website die dat duidelijk laat zien en bezoekers helpt contact op te nemen.", outcome: "Geef je bedrijf een passende website.", href: "/diensten#websites", link: "Ontdek websites op maat" },
];

const steps = [
  { title: "Eerst jouw praktijk.", description: "Je vertelt waar je tegenaan loopt. Ik kijk naar je mensen, je systemen en het werk dat nu onnodig veel moeite kost." },
  { title: "Een heldere eerste stap.", description: "We bepalen wat het meeste helpt. Je krijgt een voorstel met een afgebakende aanpak, zodat je weet waar je aan begint." },
  { title: "Bouwen, testen, verbeteren.", description: "Ik bouw de oplossing en toets die aan jouw dagelijkse werk. Samen kijken we wat werkt en waar bijsturen nodig is." },
];

const questions = [
  { question: "Ik weet nog niet precies wat ik nodig heb. Kan ik al contact opnemen?", answer: "Zeker. Vertel wat je nu doet, waar je tegenaan loopt en wat je liever anders ziet. Van daaruit kunnen we bepalen of een website, een koppeling of een andere oplossing past." },
  { question: "Kun je aansluiten op de systemen die ik al gebruik?", answer: "Dat onderzoeken we eerst. Vaak kunnen bestaande systemen via een API of een export gegevens delen. Ik bekijk wat technisch mogelijk is en welke beperkingen er zijn voordat we een oplossing kiezen." },
  { question: "Bouw je ook alleen een website?", answer: "Ja. Ook voor een nieuwe website kun je bij mij terecht. We kijken naar je doelgroep, je verhaal en wat bezoekers op je website moeten kunnen doen." },
  { question: "Kan AI bedrijfsprocessen automatiseren?", answer: "Soms. We bekijken eerst of het proces en de beschikbare gegevens geschikt zijn. AI kan onderdelen ondersteunen, maar duidelijke controles en menselijke beoordeling blijven belangrijk." },
  { question: "Wat gaat een project kosten?", answer: "Dat hangt af van de omvang, de benodigde koppelingen en wat er al beschikbaar is. Na het bespreken van je vraag maak ik een passend voorstel. Zo is de eerste stap helder voordat de bouw begint." },
];

export default function HomePage() {
  return <>
    <section className="hero conversion-hero shell" aria-labelledby="home-title">
      <div className="hero-grid">
        <div className="hero-content">
          <div className="hero-meta"><span className="availability"><i aria-hidden="true" /> DAVID COFFA · DEVELOPER VOOR HET MKB</span><span>VOORBURG · NEDERLAND</span></div>
          <h1 id="home-title"><span className="hero-headline-line">Minder handwerk.</span><br /><em>Meer ondernemen.</em></h1>
          <p className="hero-description">Een website laten maken of bedrijfsprocessen automatiseren? Ik bouw websites, portalen en AI-oplossingen voor mkb-bedrijven en breng overzicht in je dagelijkse praktijk.</p>
          <div className="hero-ctas">
            <Link href="/contact" className="button button-primary" data-cta="hero">Bespreek jouw uitdaging <span aria-hidden="true">↗</span></Link>
            <Link href="#praktijk" className="button-text">Bekijk mijn werk <span aria-hidden="true">↓</span></Link>
          </div>
          <p className="cta-reassurance">Je hoeft nog geen uitgewerkt plan te hebben.</p>
          <div className="hero-personal"><Image className="personal-photo" src="/david-coffa.PNG" alt="" width={43} height={43} priority /><div><strong>Hoi, ik ben David.</strong><span>Je spreekt met degene die het ook bouwt.</span></div></div>
        </div>
        <div className="hero-proof">
          <div className="hero-proof-label"><span>VAN LOSSE EINDJES</span><span aria-hidden="true">↘</span></div>
          <ProcessMap compact />
          <div className="hero-proof-foot"><span aria-hidden="true">↳</span><p>Software die aansluit op<br /><strong>hoe jouw bedrijf werkt.</strong></p></div>
        </div>
      </div>
      <div className="benefit-strip"><span><b aria-hidden="true">✓</b> Rechtstreeks met de bouwer</span><span><b aria-hidden="true">✓</b> Een duidelijke eerste stap</span><span><b aria-hidden="true">✓</b> Gebouwd voor jouw praktijk</span></div>
    </section>

    <IntegrationMarquee />

    <section className="section section-tinted recognition-section" id="herkenbaar">
      <div className="shell">
        <SectionHeading eyebrow="HERKENBAAR?" title={<>Het kan makkelijker.<br /><em>Ook bij jou.</em></>} aside="Ik help mkb-ondernemers in Voorburg, Den Haag, Rotterdam, Amsterdam en op afstand door heel Nederland. Je hoeft niet te weten welke software je nodig hebt: weten waar het dagelijks schuurt, is een goed begin." />
        <div className="challenge-grid">
          {challenges.map(challenge => <article className="challenge-card" key={challenge.number}>
            <span className="challenge-number">{challenge.number} / HERKEN JE DIT?</span>
            <h3>{challenge.title}</h3>
            <p>{challenge.description}</p>
            <strong>{challenge.outcome}</strong>
            <Link href={challenge.href} className="challenge-link">{challenge.link} <span aria-hidden="true">↗</span></Link>
          </article>)}
        </div>
      </div>
    </section>

    <section className="section shell case-section">
      <SectionHeading id="praktijk" eyebrow="VASTGOED / SLIM DASHBOARD / AI" title={<>Meer overzicht.<br /><em>Meer ruimte voor mensen.</em></>} aside="Happy Home Helpers is mijn grootste klant. Voor hun vastgoedprocessen bouwde ik een slim dashboard met documentbeheer, klantbeheer en AI-ondersteuning." />
      <article className="featured-case">
        <div className="featured-case-art"><ProjectArtwork kind="portal" /></div>
        <div className="featured-case-content">
          <span className="eyebrow">HAPPY HOME HELPERS · DASHBOARD & AI</span>
          <h3>Een slimmer vastgoedproces<span>.</span></h3>
          <p>Voor Happy Home Helpers bouwde ik een slim dashboard voor documentbeheer en CRM-klantbeheer. AI kan processen ondersteunen, zoals de voorcontrole van documenten en salarisspecificaties en het helpen toewijzen van woningen. Medewerkers houden de controle over beoordelingen en beslissingen.</p>
          <div className="case-deliverables"><span>Documentbeheer</span><span>CRM & klantbeheer</span><span>AI-ondersteuning</span></div>
          <a href="https://portal.happyhomehelpers.nl/" className="button-text" target="_blank" rel="noopener noreferrer">Bekijk het Happy Home Helpers-portaal <span aria-hidden="true">↗</span><span className="sr-only"> (opent in een nieuw tabblad)</span></a>
        </div>
      </article>
      <div className="section-followup"><p>Benieuwd wat ik nog meer bouw?</p><Link href="/projecten" className="button-text">Bekijk alle projecten <span aria-hidden="true">↗</span></Link></div>
    </section>

    <section className="working-together section" aria-labelledby="approach-title">
      <div className="shell">
        <div className="approach-intro"><p className="eyebrow">ZO WERKEN WE SAMEN</p><h2 id="approach-title">Jouw vraag is het begin.<br /><em>Daarna maken we het concreet.</em></h2></div>
        <ol className="approach-grid">{steps.map((step, index) => <li key={step.title}><span className="approach-number">0{index + 1}</span><h3>{step.title}</h3><p>{step.description}</p></li>)}</ol>
        <div className="approach-actions"><Link href="/contact" className="button button-primary" data-cta="approach">Bespreek jouw uitdaging <span aria-hidden="true">↗</span></Link><Link href="/werkwijze" className="button-text">Meer over mijn werkwijze <span aria-hidden="true">↗</span></Link></div>
      </div>
    </section>

    <section className="section shell possibilities-section" aria-labelledby="possibilities-title">
      <div><p className="eyebrow">KLEIN BEGINNEN MAG</p><h2 id="possibilities-title">Soms maakt één koppeling<br /><em>al een verschil.</em></h2><p>Een aanvraag registreren. Gegevens overnemen. Een taak klaarzetten. Ontdek welke terugkerende werkzaamheden je zou kunnen vereenvoudigen.</p><Link href="/automatiseringen" className="button-text">Bekijk de automatiseringsvoorbeelden <span aria-hidden="true">↗</span></Link></div>
      <div className="possibility-list" aria-label="Voorbeelden van automatisering"><div><span>Nieuwe aanvraag</span><b aria-hidden="true">→</b><strong>Op één plek vastgelegd</strong></div><div><span>Klantgegevens</span><b aria-hidden="true">→</b><strong>Beschikbaar waar nodig</strong></div><div><span>Terugkerende taak</span><b aria-hidden="true">→</b><strong>Op tijd klaargezet</strong></div><p>Schematische voorbeelden. De mogelijkheden hangen af van jouw systemen en proces.</p></div>
    </section>

    <section className="section shell faq-section" aria-labelledby="faq-title">
      <div><p className="eyebrow">NOG EVEN DIT</p><h2 id="faq-title">Goede vragen.<br /><em>Heldere antwoorden.</em></h2><p>Staat jouw vraag er niet tussen?</p><Link href="/contact" className="button-text">Bespreek jouw uitdaging <span aria-hidden="true">↗</span></Link></div>
      <div className="faq-list">{questions.map(item => <details key={item.question}><summary>{item.question}<span aria-hidden="true">+</span></summary><p>{item.answer}</p></details>)}</div>
    </section>
    <CTA />
  </>;
}
