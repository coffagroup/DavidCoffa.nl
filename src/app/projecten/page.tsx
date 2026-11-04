import type { Metadata } from "next";
import Link from "next/link";
import SectionHeading from "@/components/SectionHeading";
import ProjectCard from "@/components/ProjectCard";
import CTA from "@/components/CTA";
import { projects } from "@/lib/content";

export const metadata: Metadata = {
  title: "Projecten: AI, websites & automatisering voor mkb",
  description: "Bekijk projecten van David Coffa, waaronder de AI-vastgoed lead generator voor F&R Vastgoed, BouwWonders en OrangeGPT.",
  alternates: { canonical: "/projecten" },
};

export default function ProjectenPage() {
  return <>
    <section className="page-hero shell">
      <p className="eyebrow">PROJECTEN / IN DE PRAKTIJK</p>
      <h1>Van een bedrijfsvraag<br />naar <em>werkende software.</em></h1>
      <p>Bekijk drie projecten: de AI-vastgoed lead generator voor F&R Vastgoed, BouwWonders en OrangeGPT. Ik deel per project wat er over de oplossing bekend en openbaar is.</p>
      <div className="hero-ctas">
        <Link href="/contact" className="button button-primary">Bespreek jouw uitdaging <span aria-hidden="true">↗</span></Link>
        <Link href="#projecten" className="button-text">Bekijk de projecten <span aria-hidden="true">↓</span></Link>
      </div>
    </section>

    <section className="section shell" id="projecten">
      <SectionHeading eyebrow="DRIE PROJECTEN" title={<>AI. Websites.<br /><em>Software in de praktijk.</em></>} aside="De beelden zijn illustratieve visualisaties, geen screenshots van productieomgevingen. Niet-openbare projectdetails worden hier niet ingevuld." />
      <div className="project-grid">
        {projects.map((project, index) => <ProjectCard project={project} index={index} key={project.slug} />)}
      </div>
    </section>
    <CTA />
  </>;
}
