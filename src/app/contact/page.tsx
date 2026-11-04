import type { Metadata } from "next";
import ContactForm from "@/components/ContactForm";

export const metadata: Metadata = { title: "Website of automatisering nodig? Neem contact op", description: "Vertel David Coffa over jouw website, AI- of automatiseringsvraag. Je spreekt rechtstreeks met de developer voor mkb-bedrijven.", alternates: { canonical: "/contact" } };

export default async function ContactPage({ searchParams }: { searchParams: Promise<{ onderwerp?: string | string[] }> }) {
  const params = await searchParams;
  const initialSubject = typeof params.onderwerp === "string" ? params.onderwerp.slice(0, 160).replace(/[\r\n]/g, " ") : "";
  return <>
    <section className="page-hero shell"><p className="eyebrow">CONTACT / RECHTSTREEKS MET DAVID</p><h1>Bespreek jouw<br /><em>uitdaging.</em></h1><p>Een proces dat tijd kost, een website die beter kan of een idee voor een portaal. Vertel wat je wilt verbeteren, dan kijken we samen naar een logische eerste stap.</p></section>
    <section className="section shell contact-layout">
      <div className="contact-left"><h2>Het begint<br /><em>met jouw vraag.</em></h2><p>Je hoeft nog niet te weten welke software je nodig hebt. Ik denk mee vanuit de mensen die ermee werken en het probleem dat je wilt oplossen.</p>
        <div className="contact-note"><b>Wat gebeurt er na je bericht?</b><ol className="contact-next-steps"><li>Ik lees je vraag en neem persoonlijk contact met je op.</li><li>We bespreken hoe je nu werkt en wat je wilt bereiken.</li><li>Je krijgt duidelijkheid over een passende vervolgstap.</li></ol></div>
        <div className="contact-methods"><a className="contact-direct" href="mailto:info@davidcoffa.nl"><span><small>MAIL ME RECHTSTREEKS</small>info@davidcoffa.nl</span><b aria-hidden="true">↗</b></a><a className="contact-direct" href="tel:+31683439000"><span><small>BEL MIJ</small>+31 6 83 43 90 00</span><b aria-hidden="true">↗</b></a><a className="contact-direct" href="https://www.linkedin.com/in/davidcoffa" target="_blank" rel="noopener noreferrer"><span><small>BEKIJK MIJN PROFIEL</small>LinkedIn / davidcoffa</span><b aria-hidden="true">↗</b></a></div>
      </div>
      <ContactForm initialSubject={initialSubject} />
    </section>
  </>;
}
