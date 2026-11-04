import type { ReactNode } from "react";

export default function SectionHeading({ eyebrow, title, aside, id }: { eyebrow: string; title: ReactNode; aside?: string; id?: string }) {
  return <div className="section-heading" id={id}><div><p className="eyebrow">{eyebrow}</p><h2>{title}</h2></div>{aside && <p className="section-aside">{aside}</p>}</div>;
}
