import Link from "next/link";
export default function NotFound() { return <section className="not-found shell"><span className="eyebrow">404 / NIET GEVONDEN</span><h1>Deze pagina is<br /><em>even offline.</em></h1><p>Of misschien bestaat hij nog niet. De homepage gelukkig wel.</p><Link className="button button-dark" href="/">Terug naar home <span>↗</span></Link></section>; }
