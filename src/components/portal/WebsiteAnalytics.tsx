"use client";

import { useEffect, useState } from "react";
import { api, errorMessage } from "@/lib/portal/api";
import { Loading, Notice } from "./Primitives";
import { useLanguage } from "@/components/LanguageProvider";

type Period = "7d" | "30d" | "90d" | "12m";
type Status = { configured: boolean; searchConsoleConfigured: boolean };
type Trend = { date: string; visitors: number; sessions: number; pageViews: number };
type Page = { path: string; views: number; visitors: number };
type Source = { source: string; sessions: number; visitors: number };
type SearchQuery = { query: string; clicks: number; impressions: number; clickThroughRate: number; averagePosition: number };
type SearchPage = { page: string; clicks: number; impressions: number; clickThroughRate: number; averagePosition: number };
type Report = {
  period: Period; startDate: string; endDate: string; visitors: number; sessions: number; pageViews: number;
  trend: Trend[]; topPages: Page[]; topSources: Source[];
  searchConsole: { configured: boolean; clicks: number; impressions: number; clickThroughRate: number; averagePosition: number; topQueries: SearchQuery[]; topPages: SearchPage[]; message: string | null };
  fetchedAt: string;
};

const number = (value: number, language: string) => new Intl.NumberFormat(language === "en" ? "en-GB" : "nl-NL").format(value);
const date = (value: string, language: string) => new Intl.DateTimeFormat(language === "en" ? "en-GB" : "nl-NL", { day: "numeric", month: "short", timeZone: "UTC" }).format(new Date(`${value}T12:00:00Z`));

export function WebsiteAnalytics() {
  const { language } = useLanguage();
  const tx = (nl: string, en: string) => language === "en" ? en : nl;
  const [period, setPeriod] = useState<Period>("30d");
  const [status, setStatus] = useState<Status | null>(null);
  const [report, setReport] = useState<Report | null>(null);
  const [problem, setProblem] = useState("");
  const [loading, setLoading] = useState(true);
  const [revision, setRevision] = useState(0);

  useEffect(() => {
    let active = true;
    const controller = new AbortController();
    setLoading(true); setProblem(""); setReport(null);
    api<Status>("admin/analytics/status", { signal: controller.signal }).then(async configuration => {
      if (!active) return;
      setStatus(configuration);
      if (!configuration.configured) return;
      const data = await api<Report>(`admin/analytics/report?period=${period}`, { signal: controller.signal });
      if (active) setReport(data);
    }).catch(error => {
      if (active && !controller.signal.aborted) setProblem(errorMessage(error));
    }).finally(() => { if (active) setLoading(false); });
    return () => { active = false; controller.abort(); };
  }, [period, revision]);

  const periodOptions: { key: Period; nl: string; en: string }[] = [
    { key: "7d", nl: "7 dagen", en: "7 days" },
    { key: "30d", nl: "30 dagen", en: "30 days" },
    { key: "90d", nl: "90 dagen", en: "90 days" },
    { key: "12m", nl: "12 maanden", en: "12 months" },
  ];
  const trend = report?.trend ?? [];
  const peak = Math.max(1, ...trend.map(item => item.visitors));

  return <section className="os-website-analytics">
    <div className="os-page-heading"><div><span className="os-eyebrow">COFFA OS / {tx("MARKETING","MARKETING")}</span><h1>{tx("Website-statistieken","Website analytics")}<span>.</span></h1><p>{tx("Bezoekers, verkeersbronnen, best gelezen pagina’s en prestaties in Google Zoeken.", "Visitors, traffic sources, most-read pages, and Google Search performance.")}</p></div><button className="os-button secondary" type="button" disabled={loading} onClick={() => setRevision(value => value + 1)}>{tx("Vernieuwen","Refresh")} ↻</button></div>
    <Notice error>{problem}</Notice>
    <div className="os-analytics-toolbar"><div className="os-period" role="group" aria-label={tx("Periode selecteren","Select period")}>{periodOptions.map(option => <button key={option.key} type="button" aria-pressed={period === option.key} onClick={() => setPeriod(option.key)}>{tx(option.nl, option.en)}</button>)}</div>{report&&<span className="os-muted">{tx("Gegevens bijgewerkt:","Updated:")} {new Intl.DateTimeFormat(language==="en"?"en-GB":"nl-NL",{dateStyle:"medium",timeStyle:"short"}).format(new Date(report.fetchedAt))}</span>}</div>
    {loading ? <Loading /> : !status?.configured
      ? <div className="os-panel os-analytics-setup"><span className="os-eyebrow">{tx("EENMALIGE KOPPELING","ONE-TIME SETUP")}</span><h2>{tx("Koppel Google Analytics","Connect Google Analytics")}</h2><p>{tx("De statistieken verschijnen hier zodra de GA4-property is gekoppeld. De website laadt de Google Analytics-tag alleen nadat een bezoeker daar toestemming voor geeft.", "Analytics will appear here once a GA4 property is connected. The website loads the Google Analytics tag only after a visitor gives consent.")}</p><ol>
        <li>{tx("Maak of kies een GA4-property en voeg de meet-ID (G-...) toe aan NEXT_PUBLIC_GA_MEASUREMENT_ID in de websiteomgeving.", "Create or select a GA4 property and add its measurement ID (G-...) as NEXT_PUBLIC_GA_MEASUREMENT_ID in the website environment.")}</li>
        <li>{tx("Maak een Google Cloud-serviceaccount, schakel de Google Analytics Data API in en deel de Analytics-property met het e-mailadres van dat serviceaccount als Viewer.", "Create a Google Cloud service account, enable the Google Analytics Data API, and share the Analytics property with the service-account email as Viewer.")}</li>
        <li>{tx("Stel op de API-server Analytics__Ga4PropertyId en Analytics__GoogleServiceAccountJson in. Bewaar de JSON-key uitsluitend als servergeheim.", "Set Analytics__Ga4PropertyId and Analytics__GoogleServiceAccountJson on the API server. Store the JSON key only as a server secret.")}</li>
        <li>{tx("Optioneel: schakel ook de Search Console API in, verifieer de site in Search Console, voeg hetzelfde serviceaccount als gebruiker toe en stel Analytics__SearchConsoleSiteUrl in voor Google-zoekopdrachten en vertoningen.", "Optional: enable the Search Console API too, verify the site in Search Console, add the same service account as a user, and set Analytics__SearchConsoleSiteUrl for Google search queries and impressions.")}</li>
      </ol>      <p className="os-muted">{tx("GA4 kan alleen eerder verzamelde data tonen; bezoeken van vóór een werkende meettag zijn niet terug te halen. Search Console kan wel eerder verzamelde zoekgegevens tonen.", "GA4 can only show data it has already collected; visits from before a working tag cannot be reconstructed. Search Console may have historical search data available.")}</p></div>
      : report && <>
        <div className="os-kpi-grid os-analytics-kpis">{[
          [tx("Bezoekers","Visitors"), report.visitors, tx("unieke gebruikers volgens GA4","unique users reported by GA4")],
          [tx("Sessies","Sessions"), report.sessions, tx("bezoeken in deze periode","visits during this period")],
          [tx("Paginaweergaven","Page views"), report.pageViews, tx("bekeken pagina’s","pages viewed")],
        ].map(([label, value, note]) => <div className="os-kpi" key={String(label)}><span>{label}</span><strong>{number(Number(value),language)}</strong><small>{note}</small></div>)}</div>
        <div className="os-analytics-grid">
          <div className="os-panel"><div className="os-panel-heading"><h2>{tx("Bezoekers in de tijd","Visitors over time")}</h2><span className="os-muted">{period==="90d"?tx("per week","weekly"):period==="12m"?tx("per maand","monthly"):tx("per dag","daily")}</span></div>
            {!trend.length?<p className="os-muted">{tx("Nog geen bezoekersgegevens voor deze periode.","No visitor data for this period yet.")}</p>:<div className="os-analytics-chart">{trend.map(point=><div className="os-analytics-bar-row" key={point.date} title={`${point.date}: ${number(point.visitors,language)} ${tx("bezoekers","visitors")}`}>
              <span>{period==="7d"||period==="30d"?date(point.date,language):point.date}</span><div className="os-analytics-bar-track"><i style={{width:`${Math.max(point.visitors?2:0,point.visitors/peak*100)}%`}} /></div><strong>{number(point.visitors,language)}</strong>
            </div>)}</div>}
          </div>
          <div className="os-panel"><h2>{tx("Waar komen bezoekers vandaan?","Where visitors come from")}</h2>
            {!report.topSources.length?<p className="os-muted">{tx("Nog geen verkeersbronnen beschikbaar.","No traffic sources available yet.")}</p>:<div className="os-analytics-list">{report.topSources.map(item=><div className="os-analytics-list-row" key={item.source}><div><strong>{item.source==="(direct) / (none)"?tx("Direct / onbekend","Direct / unknown"):item.source}</strong><span>{number(item.visitors,language)} {tx("bezoekers","visitors")}</span></div><b>{number(item.sessions,language)} {tx("sessies","sessions")}</b></div>)}</div>}
          </div>
          <div className="os-panel"><h2>{tx("Populairste pagina’s","Most popular pages")}</h2>
            {!report.topPages.length?<p className="os-muted">{tx("Nog geen paginaweergaven beschikbaar.","No page views available yet.")}</p>:<div className="os-analytics-list">{report.topPages.map(item=><div className="os-analytics-list-row" key={item.path}><div><strong>{item.path}</strong><span>{number(item.visitors,language)} {tx("bezoekers","visitors")}</span></div><b>{number(item.views,language)} {tx("weergaven","views")}</b></div>)}</div>}
          </div>
          <div className="os-panel"><div className="os-panel-heading"><h2>{tx("Zoekverkeer via Google","Google Search traffic")}</h2>{report.searchConsole.configured&&<span className="os-badge status-active">Search Console</span>}</div>
            {!report.searchConsole.configured?<><p>{tx("Koppel Google Search Console om zoekklikken, vertoningen en zoekopdrachten te bekijken.", "Connect Google Search Console to see search clicks, impressions, and queries.")}</p><p className="os-muted">{report.searchConsole.message}</p></>
              :<><div className="os-search-summary"><div><span>{tx("Klikken","Clicks")}</span><strong>{number(report.searchConsole.clicks,language)}</strong></div><div><span>{tx("Vertoningen","Impressions")}</span><strong>{number(report.searchConsole.impressions,language)}</strong></div><div><span>CTR</span><strong>{(report.searchConsole.clickThroughRate*100).toLocaleString(language==="en"?"en-GB":"nl-NL",{maximumFractionDigits:1})}%</strong></div><div><span>{tx("Gem. positie","Avg. position")}</span><strong>{report.searchConsole.averagePosition.toLocaleString(language==="en"?"en-GB":"nl-NL",{maximumFractionDigits:1})}</strong></div></div>
                {report.searchConsole.message?<p className="os-muted">{report.searchConsole.message}</p>:<>
                  <h3 className="os-analytics-subheading">{tx("Zoekopdrachten","Search queries")}</h3>
                  {!report.searchConsole.topQueries.length?<p className="os-muted">{tx("Geen zoekopdrachten gerapporteerd in deze periode.","No search queries reported for this period.")}</p>:<div className="os-analytics-list">{report.searchConsole.topQueries.map(item=><div className="os-analytics-list-row" key={item.query}><div><strong>{item.query}</strong><span>{number(item.impressions,language)} {tx("vertoningen","impressions")} · positie {item.averagePosition.toLocaleString(language==="en"?"en-GB":"nl-NL",{maximumFractionDigits:1})}</span></div><b>{number(item.clicks,language)} {tx("klikken","clicks")}</b></div>)}</div>}
                  <h3 className="os-analytics-subheading">{tx("Pagina’s vanuit Google Zoeken","Pages from Google Search")}</h3>
                  {!report.searchConsole.topPages.length?<p className="os-muted">{tx("Geen pagina’s gerapporteerd in deze periode.","No pages reported for this period.")}</p>:<div className="os-analytics-list">{report.searchConsole.topPages.map(item=><div className="os-analytics-list-row" key={item.page}><div><strong>{item.page.replace(/^https?:\/\/[^/]+/,"")||"/"}</strong><span>{number(item.impressions,language)} {tx("vertoningen","impressions")} · positie {item.averagePosition.toLocaleString(language==="en"?"en-GB":"nl-NL",{maximumFractionDigits:1})}</span></div><b>{number(item.clicks,language)} {tx("klikken","clicks")}</b></div>)}</div>}
                </>}
              </>}
          </div>
        </div>
        <p className="os-muted os-analytics-footnote">{tx("Bezoekers en sessies zijn GA4-metingen van websitebezoekers die analytics toestemming gaven. Google Search Console-gegevens lopen doorgaans enkele dagen achter; klikken zijn geen volledige telling van alle websitebezoeken.", "Visitors and sessions are GA4 measurements of visitors who consented to analytics. Google Search Console data is typically delayed by a few days; clicks are not a complete count of all website visits.")}</p>
      </>}
  </section>;
}
