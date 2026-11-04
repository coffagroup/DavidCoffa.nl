import Link from "next/link";

export default function SiteFooter() {
  return (
    <footer className="site-footer">
      <div className="shell footer-top">
        <div>
          <span className="footer-mark">dc<span>.</span></span>
          <p>Minder handwerk.<br />Meer ruimte voor jouw bedrijf.</p>
        </div>
        <div className="footer-links">
          <div><span>DIENSTEN & VOORBEELDEN</span><Link href="/diensten#websites">Website laten maken voor mkb</Link><Link href="/automatiseringen">Bedrijfsprocessen automatiseren</Link><Link href="/diensten#ai">AI voor mkb-bedrijven</Link><Link href="/diensten#platforms">Dashboards & klantportalen</Link><Link href="/projecten">Projecten</Link></div>
          <div><span>WERKGEBIED</span><p className="footer-area">Vanuit Voorburg werk ik voor mkb-bedrijven in Den Haag, Rotterdam en Amsterdam, en op afstand door heel Nederland.</p></div>
          <div><span>CONTACT</span><Link href="/contact">Bespreek jouw uitdaging</Link><a href="mailto:info@davidcoffa.nl">info@davidcoffa.nl</a><a href="tel:+31683439000">+31 6 83 43 90 00</a><a href="https://www.linkedin.com/in/davidcoffa" target="_blank" rel="noopener noreferrer">LinkedIn ↗</a></div>
        </div>
      </div>
      <div className="shell footer-bottom"><span>© {new Date().getFullYear()} David Coffa. Met aandacht gebouwd.</span><Link href="/portaal">Klantenportaal</Link><span>SOFTWARE · WEB · DATA · AUTOMATION</span><a href="#top">Terug naar boven ↑</a></div>
    </footer>
  );
}
