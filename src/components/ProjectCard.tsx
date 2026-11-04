import Link from "next/link";
import type { Project } from "@/lib/content";
import ProjectArtwork from "./ProjectArtwork";

export default function ProjectCard({ project, index }: { project: Project; index: number }) {
  return <article className="project-card"><ProjectArtwork kind={project.artwork}/><div className="project-info"><div className="project-meta"><span>PROJECT {String(index + 1).padStart(2, "0")}</span><span>{project.state}</span></div><h3>{project.title}</h3><span className="project-kind">{project.kind}</span><p>{project.description}</p><div className="project-bottom"><span>{project.stack}</span>{project.href ? <a href={project.href} target="_blank" rel="noopener noreferrer" aria-label={`Bekijk ${project.title}`}>↗</a> : <Link href="/contact" aria-label={`Vraag over ${project.title}`}>↗</Link>}</div></div></article>;
}
