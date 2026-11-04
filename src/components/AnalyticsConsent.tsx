"use client";

import { useEffect, useRef, useState } from "react";
import { usePathname } from "next/navigation";
import { useLanguage } from "./LanguageProvider";

declare global {
  interface Window {
    dataLayer?: unknown[];
    gtag?: (...args: unknown[]) => void;
    __davidCoffaAnalyticsId?: string;
  }
}

const storageKey = "davidcoffa-analytics-consent";
const measurementId = process.env.NEXT_PUBLIC_GA_MEASUREMENT_ID;

function enableAnalytics(id: string, pagePath: string) {
  if (!window.gtag) {
    window.dataLayer = window.dataLayer ?? [];
    window.gtag = (...args: unknown[]) => { window.dataLayer?.push(args); };
  }
  if (window.__davidCoffaAnalyticsId !== id) {
    window.__davidCoffaAnalyticsId = id;
    window.gtag("js", new Date());
    window.gtag("config", id, { send_page_view: false, anonymize_ip: true });
    const script = document.createElement("script");
    script.async = true;
    script.src = `https://www.googletagmanager.com/gtag/js?id=${encodeURIComponent(id)}`;
    script.dataset.analyticsId = id;
    document.head.appendChild(script);
  }
  window.gtag("event", "page_view", { page_path: pagePath, page_location: window.location.origin + pagePath });
}

function disableAnalytics() {
  const id = window.__davidCoffaAnalyticsId;
  if (!id) return;
  document.querySelectorAll<HTMLScriptElement>("script[data-analytics-id]").forEach(script => { if (script.dataset.analyticsId === id) script.remove(); });
  delete window.__davidCoffaAnalyticsId;
  delete window.gtag;
  delete window.dataLayer;
  document.cookie.split(";").forEach(cookie => {
    const name = cookie.trim().split("=")[0];
    if (name === "_ga" || name.startsWith("_ga_")) document.cookie = `${name}=; Max-Age=0; Path=/; SameSite=Lax`;
  });
}

export default function AnalyticsConsent() {
  const { language } = useLanguage();
  const pathname = usePathname();
  const [consent, setConsent] = useState<"pending" | "accepted" | "rejected">("pending");
  const [ready, setReady] = useState(false);
  const [showBanner, setShowBanner] = useState(false);
  const lastPath = useRef("");
  const isEnglish = language === "en";

  useEffect(() => {
    try {
      const saved = localStorage.getItem(storageKey);
      if (saved === "accepted" || saved === "rejected") {
        setConsent(saved);
        setShowBanner(false);
      } else setShowBanner(true);
    } catch { setShowBanner(true); }
    setReady(true);
  }, []);

  useEffect(() => {
    if (!ready || !measurementId || consent !== "accepted" || pathname.startsWith("/portaal") || pathname === lastPath.current) return;
    lastPath.current = pathname;
    enableAnalytics(measurementId, pathname);
  }, [consent, pathname, ready]);

  if (!measurementId || !ready) return null;

  function choose(value: "accepted" | "rejected") {
    try { localStorage.setItem(storageKey, value); } catch { /* The choice remains active for this page session. */ }
    setConsent(value);
    setShowBanner(false);
    if (value === "rejected") {
      disableAnalytics();
      lastPath.current = "";
    }
  }

  return <div className="analytics-consent" data-no-translate>
    {!showBanner
      ? <button className="analytics-preferences" type="button" onClick={() => setShowBanner(true)}>{isEnglish ? "Cookie preferences" : "Cookievoorkeuren"}</button>
      : <section className="analytics-consent-panel" role="dialog" aria-labelledby="analytics-consent-title" aria-describedby="analytics-consent-description">
        <div>
          <strong id="analytics-consent-title">{isEnglish ? "Optional website statistics" : "Optionele websittestatistieken"}</strong>
          <p id="analytics-consent-description">{isEnglish
            ? "With your permission, Google Analytics measures visits, popular pages and traffic sources. It is not loaded if you decline; you can change your choice later."
            : "Met je toestemming meet Google Analytics bezoeken, populaire pagina’s en verkeersbronnen. Bij weigeren wordt de analytics-tag niet geladen. Je kunt je keuze later wijzigen."}</p>
          <a className="analytics-consent-privacy" href="https://policies.google.com/privacy" target="_blank" rel="noopener noreferrer">{isEnglish ? "Google privacy information ↗" : "Privacyinformatie van Google ↗"}</a>
        </div>
        <div className="analytics-consent-actions">
          <button type="button" onClick={() => choose("rejected")}>{isEnglish ? "Decline" : "Weigeren"}</button>
          <button type="button" className="analytics-accept" onClick={() => choose("accepted")}>{isEnglish ? "Allow analytics" : "Analytics toestaan"}</button>
        </div>
      </section>}
  </div>;
}
