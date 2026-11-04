"use client";

import { useEffect, useState, type FormEvent } from "react";
import Link from "next/link";
import { posts } from "@/lib/blog";
import { api, errorMessage } from "@/lib/portal/api";
import { Empty, Loading, Notice } from "./Primitives";
import { useLanguage } from "@/components/LanguageProvider";

type Opportunity = { keyword: string; intent: string; priority: string; rationale: string; suggestedTitle: string; format: string };
type Section = { heading: string; paragraphs: string[]; points: string[] };
type Research = { id: string; topic: string; targetAudience: string; targetRegion: string; summary: string; opportunities: Opportunity[]; createdAt: string };
type Article = {
  id: string; researchId: string | null; slug: string; category: string; focusKeyword: string; relatedKeywords: string[];
  searchIntent: string; title: string; metaTitle: string; metaDescription: string; excerpt: string; intro: string;
  sections: Section[]; internalLinks: string[]; status: "draft" | "published"; createdAt: string; updatedAt: string; publishedAt: string | null;
};
type State = { articles: Article[]; research: Research[] };

const blank = (): Article => ({
  id: "", researchId: null, slug: "", category: "", focusKeyword: "", relatedKeywords: [],
  searchIntent: "", title: "", metaTitle: "", metaDescription: "", excerpt: "", intro: "",
  sections: [], internalLinks: [], status: "draft", createdAt: "", updatedAt: "", publishedAt: null,
});
const safeArray = (value: string) => value.split(/\r?\n/).map(item => item.trim()).filter(Boolean);

export function BlogManager() {
  const { language } = useLanguage();
  const tx = (nl: string, en: string) => language === "en" ? en : nl;
  const [state, setState] = useState<State>({ articles: [], research: [] });
  const [article, setArticle] = useState<Article>(blank);
  const [topic, setTopic] = useState("");
  const [audience, setAudience] = useState("Nederlandse mkb-ondernemers");
  const [region, setRegion] = useState("Nederland");
  const [busy, setBusy] = useState(false);
  const [loading, setLoading] = useState(true);
  const [problem, setProblem] = useState("");
  const [notice, setNotice] = useState("");
  const [revision, setRevision] = useState(0);

  useEffect(() => {
    let active = true;
    api<State>("admin/blog").then(value => { if (active) setState(value); })
      .catch(error => { if (active) setProblem(errorMessage(error)); })
      .finally(() => { if (active) setLoading(false); });
    return () => { active = false; };
  }, [revision]);

  async function research(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); setProblem(""); setNotice(""); setBusy(true);
    try {
      const current = [
        ...posts.map(({ slug, title, excerpt, category }) => ({ slug, title, excerpt, category })),
        ...state.articles.map(({ slug, title, excerpt, category }) => ({ slug, title, excerpt, category })),
      ];
      const result = await api<Research>("admin/blog/research", { method: "POST", body: { topic, targetAudience: audience, targetRegion: region, existingArticles: current } });
      setState(value => ({ ...value, research: [result, ...value.research] }));
      setNotice(tx("Onderzoek opgeslagen. Prioriteiten zijn kwalitatief; zoekvolumes zijn niet gemeten.", "Research saved. Priorities are qualitative; search volumes have not been measured."));
    } catch (error) { setProblem(errorMessage(error)); }
    finally { setBusy(false); }
  }

  async function generate(report: Research, opportunity: Opportunity) {
    setProblem(""); setNotice(""); setBusy(true);
    try {
      const generated = await api<Article>("admin/blog/draft", { method: "POST", body: { researchId: report.id, keyword: opportunity.keyword } });
      setArticle(generated);
      setState(value => ({ ...value, articles: [generated, ...value.articles] }));
      setNotice(tx("Concept gemaakt. Controleer feiten, zoekintentie en tekst voordat je het publiceert.", "Draft created. Review facts, search intent, and copy before publishing."));
    } catch (error) { setProblem(errorMessage(error)); }
    finally { setBusy(false); }
  }

  async function saveDraft(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); setProblem(""); setNotice(""); setBusy(true);
    try {
      const saved = await api<Article>(`admin/blog/${article.id}`, {
        method: "PUT",
        body: article,
      });
      setArticle(saved); setRevision(value => value + 1); setNotice(tx("Concept opgeslagen.", "Draft saved."));
    } catch (error) { setProblem(errorMessage(error)); }
    finally { setBusy(false); }
  }

  async function publish(id: string) {
    setProblem(""); setNotice(""); setBusy(true);
    try {
      if (article.id === id) {
        const saved = await api<Article>(`admin/blog/${id}`, { method: "PUT", body: article });
        setArticle(saved);
      }
      await api<Article>(`admin/blog/${id}/publish`, { method: "POST", body: {} });
      setRevision(value => value + 1); setArticle(blank()); setNotice(tx("Artikel staat nu op de website.", "Article is now live on the website."));
    }
    catch (error) { setProblem(errorMessage(error)); }
    finally { setBusy(false); }
  }

  async function unpublish(id: string) {
    setProblem(""); setNotice(""); setBusy(true);
    try { await api<Article>(`admin/blog/${id}/unpublish`, { method: "POST", body: {} }); setRevision(value => value + 1); setArticle(blank()); setNotice(tx("Artikel offline gehaald.", "Article taken offline.")); }
    catch (error) { setProblem(errorMessage(error)); }
    finally { setBusy(false); }
  }

  const update = (key: keyof Article, value: string) => setArticle(current => ({ ...current, [key]: value }));
  const updateSection = (index: number, key: "heading" | "paragraphs" | "points", value: string) => setArticle(current => ({
    ...current,
    sections: current.sections.map((section, i) => i !== index ? section : {
      ...section,
      [key]: key === "heading" ? value : safeArray(value),
    }),
  }));
  const articleInput = (key: keyof Article, label: string, maxLength: number) => (
    <label key={key}>{label}<input required maxLength={maxLength} value={String(article[key] ?? "")} onChange={event => update(key, event.target.value)} /></label>
  );
  const articleTextarea = (key: keyof Article, label: string, maxLength: number) => (
    <label key={key}>{label}<textarea required maxLength={maxLength} rows={key === "intro" ? 4 : 3} value={String(article[key] ?? "")} onChange={event => update(key, event.target.value)} /></label>
  );

  return <section className="os-blog-manager">
    <div className="os-page-heading"><div><span className="os-eyebrow">COFFA OS / {tx("CONTENT","CONTENT")}</span><h1>{tx("SEO-blogstudio","SEO blog studio")}<span>.</span></h1><p>{tx("Onderzoek onderwerpen, maak bewerkbare concepten en publiceer pas na je eigen controle.", "Research topics, create editable drafts, and publish only after your own review.")}</p></div></div>
    <Notice error>{problem}</Notice><Notice>{notice}</Notice>
    <div className="os-blog-note"><strong>{tx("Eerlijk over zoekdata","Search data, accurately labeled")}</strong><p>{tx("Zoekvolumes, trends en concurrentiescores zijn niet gekoppeld. De AI geeft daarom alleen kwalitatieve hypotheses en prioriteiten, geen gemeten cijfers of rankingbeloftes.", "Search volumes, trends, and competition scores are not connected. AI therefore provides qualitative hypotheses and priorities only—not measured numbers or ranking promises.")}</p></div>
    <div className="os-panel">
      <h2>{tx("Zoekwoordenonderzoek","Keyword research")}</h2>
      <form className="os-form" onSubmit={research}>
        <label>{tx("Onderwerp of klantvraag","Topic or customer question")}<input required maxLength={300} value={topic} onChange={event => setTopic(event.target.value)} placeholder={tx("Bijv. AI inzetten in een klein administratiekantoor", "e.g. Using AI in a small accounting firm")} /></label>
        <div className="os-form-grid">
          <label>{tx("Doelgroep","Target audience")}<input required maxLength={200} value={audience} onChange={event => setAudience(event.target.value)} /></label>
          <label>{tx("Regio","Region")}<input required maxLength={200} value={region} onChange={event => setRegion(event.target.value)} /></label>
        </div>
        <p className="os-muted">{tx("Bestaande artikelen, inclusief concepten, worden meegestuurd om overlap en kannibalisatie te beperken.", "Existing articles, including drafts, are included to reduce topic overlap and cannibalization.")}</p>
        <button className="os-button primary" disabled={busy}>{busy ? tx("Bezig…","Working…") : tx("Onderzoek onderwerpen","Research topics")} ↗</button>
      </form>
    </div>
    {loading ? <Loading /> : <div className="os-blog-columns">
      <div className="os-panel">
        <h2>{tx("Onderzoek en kansen","Research and opportunities")}</h2>
        {!state.research.length ? <Empty>{tx("Je opgeslagen onderzoek verschijnt hier.","Your saved research will appear here.")}</Empty> : state.research.map(report => <div className="os-blog-report" key={report.id}>
          <h3>{report.topic}</h3><p className="os-muted">{report.targetAudience} · {report.targetRegion}</p><p>{report.summary}</p>
          {report.opportunities.map(item => <article className="os-blog-opportunity" key={item.keyword}>
            <div className="os-panel-heading"><strong>{item.keyword}</strong><span className="os-badge">{tx(item.priority==="high"?"Hoge kwalitatieve prioriteit":item.priority==="medium"?"Gemiddelde kwalitatieve prioriteit":"Lage kwalitatieve prioriteit",item.priority==="high"?"High qualitative priority":item.priority==="medium"?"Medium qualitative priority":"Low qualitative priority")}</span></div>
            <p>{item.suggestedTitle}</p><p className="os-muted">{item.intent} · {item.format}<br />{item.rationale}</p>
            <button type="button" className="os-button secondary" disabled={busy} onClick={() => generate(report, item)}>{tx("Maak concept","Create draft")} ↗</button>
          </article>)}
        </div>)}
      </div>
      <div className="os-panel">
        <h2>{tx("Artikelen","Articles")}</h2>
        {!state.articles.length ? <Empty>{tx("Nog geen concepten of gepubliceerde artikelen.","No drafts or published articles yet.")}</Empty> : state.articles.map(item => <article className="os-blog-row" key={item.id}>
          <div><span className={`os-badge status-${item.status}`}>{item.status==="published"?tx("Online","Published"):tx("Concept","Draft")}</span><strong>{item.title}</strong><p className="os-muted">{item.focusKeyword} · {item.status==="published"?<Link href={`/blog/${item.slug}`} target="_blank" rel="noreferrer">/{item.slug} ↗</Link>:`/${item.slug}`}</p></div>
          <div className="os-blog-actions">{item.status==="draft"
            ? <><button className="os-button secondary" onClick={() => setArticle(item)}>{tx("Bewerken","Edit")}</button><button className="os-button primary" disabled={busy} onClick={() => publish(item.id)}>{tx("Publiceren","Publish")}</button></>
            : <button className="os-button secondary" disabled={busy} onClick={() => unpublish(item.id)}>{tx("Offline halen","Unpublish")}</button>}
          </div>
        </article>)}
      </div>
    </div>}
    {article.id && article.status === "draft" && <div className="os-panel os-blog-editor">
      <div className="os-panel-heading"><div><span className="os-eyebrow">{tx("CONCEPT","DRAFT")}</span><h2>{tx("Artikel controleren en bewerken","Review and edit article")}</h2></div><button className="os-text-button" type="button" onClick={() => setArticle(blank())}>{tx("Sluiten","Close")}</button></div>
      <Notice>{tx("Publiceren is een aparte, handmatige stap. Controleer de inhoud en de preview zorgvuldig.", "Publishing is a separate, manual step. Review the content and preview carefully.")}</Notice>
      <form className="os-form" onSubmit={saveDraft}>
        <div className="os-form-grid">{articleInput("title", tx("Titel","Title"), 200)}{articleInput("slug", tx("URL-slug","URL slug"), 120)}</div>
        <div className="os-form-grid">{articleInput("category", tx("Categorie","Category"), 100)}{articleInput("focusKeyword", tx("Focuszoekwoord","Focus keyword"), 200)}</div>
        {articleInput("metaTitle", tx("Metatitel","Meta title"), 200)}
        {articleTextarea("metaDescription", tx("Metabeschrijving (max. 160 tekens aanbevolen)","Meta description (160 characters recommended)"), 320)}
        <p className="os-muted">{article.metaDescription.length}/160</p>
        {articleTextarea("excerpt", tx("Samenvatting","Excerpt"), 500)}
        {articleTextarea("intro", tx("Inleiding","Introduction"), 2000)}
        <label>{tx("Gerelateerde zoekwoorden (één per regel)","Related keywords (one per line)")}<textarea rows={3} value={article.relatedKeywords.join("\n")} onChange={event => setArticle(current => ({ ...current, relatedKeywords: safeArray(event.target.value) }))} /></label>
        {article.sections.map((section, index) => <fieldset className="os-blog-section" key={`${article.id}-${index}`}><legend>{tx("Sectie","Section")} {index + 1}</legend>
          <label>{tx("Kop","Heading")}<input required maxLength={200} value={section.heading} onChange={event => updateSection(index, "heading", event.target.value)} /></label>
          <label>{tx("Alinea's (één per regel)","Paragraphs (one per line)")}<textarea required rows={7} value={section.paragraphs.join("\n")} onChange={event => updateSection(index, "paragraphs", event.target.value)} /></label>
          <label>{tx("Opsommingen (één per regel)","Bullets (one per line)")}<textarea rows={3} value={(section.points ?? []).join("\n")} onChange={event => updateSection(index, "points", event.target.value)} /></label>
        </fieldset>)}
        <label>{tx("Interne links (één sitepad per regel)","Internal links (one site path per line)")}<textarea rows={3} value={article.internalLinks.join("\n")} onChange={event => setArticle(current => ({ ...current, internalLinks: safeArray(event.target.value) }))} /></label>
        <div className="os-blog-actions"><button className="os-button secondary" disabled={busy}>{busy ? tx("Opslaan…","Saving…") : tx("Concept opslaan","Save draft")}</button><button type="button" className="os-button primary" disabled={busy} onClick={() => publish(article.id)}>{tx("Publiceren","Publish")} ↗</button></div>
      </form>
    </div>}
  </section>;
}
