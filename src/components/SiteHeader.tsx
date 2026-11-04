"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { useEffect, useRef, useState } from "react";
import { useLanguage } from "@/components/LanguageProvider";

const items = [
  { label: "Automatiseringen", href: "/automatiseringen" },
  { label: "Diensten", href: "/diensten" },
  { label: "Projecten", href: "/projecten" },
  { label: "Blog", href: "/blog" },
  { label: "Over mij", href: "/over" },
];

export default function SiteHeader() {
  const [open, setOpen] = useState(false);
  const { language, setLanguage } = useLanguage();
  const menuButton = useRef<HTMLButtonElement>(null);
  const pathname = usePathname();

  useEffect(() => { setOpen(false); }, [pathname]);
  useEffect(() => {
    function keyHandler(event: KeyboardEvent) {
      if (event.key === "Escape" && open) {
        setOpen(false);
        menuButton.current?.focus();
      }
    }
    window.addEventListener("keydown", keyHandler);
    return () => window.removeEventListener("keydown", keyHandler);
  }, [open]);

  return (
    <header className="site-header">
      <div className="contact-bar">
        <div className="shell contact-bar-inner" aria-label="Direct contact">
          <a href="https://www.linkedin.com/in/davidcoffa" target="_blank" rel="noopener noreferrer" aria-label="David Coffa op LinkedIn">
            <svg viewBox="0 0 24 24" aria-hidden="true"><path d="M5.2 8.5H2V22h3.2V8.5ZM3.6 2a1.9 1.9 0 1 0 0 3.8 1.9 1.9 0 0 0 0-3.8ZM22 13.3c0-4.1-2.2-6-5.1-6a4.4 4.4 0 0 0-4 2.2V8.5H9.7V22H13v-6.7c0-1.8.3-3.6 2.6-3.6s2.3 2.1 2.3 3.7V22H22v-8.7Z"/></svg>
            <span>LinkedIn</span>
          </a>
          <a href="tel:+31644109856" aria-label="Bel David op +31 6 44 10 98 56">
            <svg viewBox="0 0 24 24" aria-hidden="true"><path d="M6.6 2h3.1l1.5 5.2-2 1.8a16 16 0 0 0 5.8 5.8l1.8-2 5.2 1.5v3.1c0 1.3-1.1 2.4-2.4 2.4C10.7 19.8 4.2 13.3 4.2 4.4 4.2 3.1 5.3 2 6.6 2Z"/></svg>
            <span>+31 6 44 10 98 56</span>
          </a>
          <a href="mailto:info@davidcoffa.nl" aria-label="E-mail info@davidcoffa.nl">
            <svg viewBox="0 0 24 24" aria-hidden="true"><path d="M3 5h18a2 2 0 0 1 2 2v10a2 2 0 0 1-2 2H3a2 2 0 0 1-2-2V7a2 2 0 0 1 2-2Zm9 7.2L3.8 7h-.3v.2l8.4 5.6 8.6-5.6V7h-.3L12 12.2Z"/></svg>
            <span>info@davidcoffa.nl</span>
          </a>
          <a className="contact-bar-whatsapp" href="https://wa.me/31644109856" target="_blank" rel="noopener noreferrer" aria-label="Stuur David een WhatsApp-bericht">
            <svg viewBox="0 0 24 24" aria-hidden="true"><path d="M12 2a9.8 9.8 0 0 0-8.4 14.8L2 22l5.4-1.4A10 10 0 1 0 12 2Zm0 17.8a8 8 0 0 1-4.1-1.1l-.3-.2-3.2.8.9-3.1-.2-.3A8 8 0 1 1 12 19.8Zm4.4-6c-.2-.1-1.4-.7-1.6-.8s-.4-.1-.5.1-.6.8-.8.9-.3.2-.5.1a6.6 6.6 0 0 1-1.9-1.2 7.1 7.1 0 0 1-1.3-1.6c-.1-.2 0-.3.1-.5l.4-.4.2-.4v-.4c-.1-.1-.5-1.3-.7-1.8-.2-.4-.4-.4-.6-.4h-.5c-.2 0-.4.1-.6.3s-.8.8-.8 1.9.8 2.2.9 2.3a9 9 0 0 0 3.5 3.1c.5.2.9.4 1.2.5.5.2 1 .2 1.3.1.4-.1 1.4-.6 1.6-1.1.2-.6.2-1 .2-1.1s-.2-.2-.4-.3Z"/></svg>
            <span>WhatsApp</span>
          </a>
        </div>
      </div>
      <div className="shell header-inner">
        <Link href="/" className="brand" aria-label="David Coffa - homepage">
          <span className="brand-mark" aria-hidden="true">dc<span>.</span></span>
          <span className="brand-caption">David Coffa<small>Developer voor het mkb</small></span>
        </Link>
        <nav id="primary-menu" className={`main-nav ${open ? "is-open" : ""}`} aria-label="Hoofdnavigatie">
          {items.map((item) => <Link key={item.href} href={item.href} className={pathname === item.href || pathname.startsWith(`${item.href}/`) ? "active" : ""}>{item.label}</Link>)}
          <Link href="/contact" className="mobile-contact">Bespreek jouw uitdaging ↗</Link>
          <Link href="/portaal/inloggen" className="mobile-portal">{language === "en" ? "Client portal log in ↗" : "Inloggen klantenportaal ↗"}</Link>
        </nav>
        <div className="nav-right">
          <div className="language-toggle" role="group" aria-label="Taalkeuze">
            <button type="button" onClick={() => setLanguage("nl")} aria-pressed={language === "nl"}>NL</button>
            <button type="button" onClick={() => setLanguage("en")} aria-pressed={language === "en"}>EN</button>
          </div>
          <a className="nav-linkedin" href="https://www.linkedin.com/in/davidcoffa" target="_blank" rel="noopener noreferrer" aria-label="David Coffa op LinkedIn" title="LinkedIn">in</a>
          <Link className="nav-portal" href="/portaal/inloggen">{language === "en" ? "Client login" : "Klantlogin"} <span aria-hidden="true">↗</span></Link>
          <Link className="nav-contact" href="/contact" data-cta="header">Bespreek jouw uitdaging <span aria-hidden="true">↗</span></Link>
          <button ref={menuButton} className="menu-toggle" type="button" onClick={() => setOpen(!open)} aria-label={open ? "Sluit menu" : "Open menu"} aria-controls="primary-menu" aria-expanded={open}>
            <span></span><span></span>
          </button>
        </div>
      </div>
    </header>
  );
}
