import { cache } from "react";

export type BlogSection = { heading: string; paragraphs: string[]; points?: string[] };
export type BlogPost = {
  slug: string;
  category: string;
  date: string;
  readingTime: string;
  title: string;
  excerpt: string;
  intro: string;
  sections: BlogSection[];
  focusKeyword?: string;
  relatedKeywords?: string[];
  metaTitle?: string;
  metaDescription?: string;
  internalLinks?: string[];
  modifiedDate?: string;
};

// Bron voor de blog. Later kan deze interface vervangen worden door een CMS/database.
export const posts: BlogPost[] = [
  {
    slug: "automatiseren-begint-bij-data",
    category: "Data & automatisering",
    date: "2026-11-04",
    readingTime: "4 min",
    title: "Automatiseren begint niet bij AI. Het begint bij je data.",
    excerpt: "Waarom het vaak misgaat vóór de eerste workflow en hoe je wel een stevige basis legt.",
    intro: "Iedereen wil minder handmatig werk. Toch is een extra tool zelden de eerste stap. Als klantnamen, statussen en afspraken overal iets anders betekenen, maak je met automatisering vooral de chaos sneller.",
    sections: [
      {
        heading: "Eerst de bronnen zien",
        paragraphs: ["Schrijf op waar informatie binnenkomt: formulieren, e-mail, Excel, boekhouding, CRM en berichten. Teken vervolgens wie welk bestand bijwerkt en wanneer.", "Dat klinkt simpel. Maar vaak ontdek je hier al dubbele invoer, oude exports en onduidelijke verantwoordelijkheden."],
        points: ["Welke bron is leidend?", "Welke gegevens heb je echt nodig?", "Waar worden dezelfde velden opnieuw ingevuld?"],
      },
      {
        heading: "Maak één duidelijke taal",
        paragraphs: ["Leg een klein gegevensmodel vast. Bijvoorbeeld: een aanvraag heeft een uniek nummer, klant, onderwerp, status en eigenaar. Definieer wat ‘open’, ‘in behandeling’ en ‘afgerond’ betekenen.", "Pas dan wordt het interessant om systemen te koppelen. Nieuwe aanvragen kunnen direct op de juiste plek landen, zonder dat iemand hoeft over te typen."],
      },
      {
        heading: "Automatiseer één herhaalbaar stukje",
        paragraphs: ["Begin niet met twintig workflows tegelijk. Pak één taak die vaak terugkomt, voorspelbaar is en weinig uitzonderingen heeft. Meet hoeveel tijd die taak nu kost, bouw een kleine verbetering en kijk wat er daadwerkelijk verandert.", "Zo ontstaat software die helpt in plaats van een extra systeem dat iedereen moet onderhouden."],
      },
    ],
  },
  {
    slug: "vijf-processen-automatiseren",
    category: "Praktijkvoorbeelden",
    date: "2026-10-27",
    readingTime: "5 min",
    title: "Vijf processen waar bedrijven onnodig veel tijd aan kwijt zijn",
    excerpt: "Van offerte tot klantvraag: kleine stappen die je eerst kunt ordenen en daarna automatiseren.",
    intro: "Een groot softwareproject is niet altijd nodig. Vaak zit de winst in een paar terugkerende handelingen die iedereen ‘er even bij’ doet.",
    sections: [
      {
        heading: "1. Aanvragen verwerken",
        paragraphs: ["Een formulier wordt een e-mail, een collega kopieert gegevens naar Excel en iemand anders maakt een taak aan. Maak van al die acties één flow vanuit één gestructureerde aanvraag."],
      },
      {
        heading: "2. Offertes opstellen",
        paragraphs: ["Laat actuele klantgegevens en prijslijsten samenkomen. Genereer een concept dat een medewerker controleert en verstuurt. Je houdt de menselijke beslissing, maar schrapt dubbel invoerwerk."],
      },
      {
        heading: "3. Facturen opvolgen",
        paragraphs: ["Combineer factuurstatus en vervaldatum en bereid alleen een herinnering voor als die echt nodig is. Laat een medewerker de uitzonderingen beoordelen."],
      },
      {
        heading: "4. Klantvragen verdelen",
        paragraphs: ["E-mail, berichten en formulieren kunnen samenkomen in een centrale takenlijst. Met categorie, eigenaar en status ziet iedereen wat al opgepakt is."],
      },
      {
        heading: "5. Rapportages maken",
        paragraphs: ["In plaats van elke maand exports samenvoegen, kun je met een helder gegevensmodel dezelfde KPI's steeds opnieuw uitrekenen. De kwaliteit van de brondata blijft daarbij bepalend."],
      },
    ],
  },
  {
    slug: "maatwerk-of-bestaande-tool",
    category: "Softwarekeuzes",
    date: "2026-10-18",
    readingTime: "4 min",
    title: "Maatwerk bouwen of gewoon een bestaande tool gebruiken?",
    excerpt: "Niet elk probleem vraagt om nieuwe software. Soms levert een slimme koppeling veel meer op.",
    intro: "Ik schrijf graag code, maar een nieuwe applicatie is niet automatisch de slimste oplossing. De beste keuze is vaak de oplossing die het team echt gaat gebruiken.",
    sections: [
      {
        heading: "Kijk eerst wat je al hebt",
        paragraphs: ["Veel bedrijven betalen voor systemen die meer kunnen dan ze gebruiken. Onderzoek daarom eerst of je bestaande CRM, boekhouding of planning een goede API en de gewenste functionaliteit heeft."],
      },
      {
        heading: "Wanneer maatwerk wél logisch is",
        paragraphs: ["Als je proces echt onderscheidend is, meerdere systemen moeten samenwerken of medewerkers dagelijks moeten omgaan met onhandige workarounds, kan een kleine maatwerkapp het verschil maken."],
        points: ["Het proces is voldoende stabiel om te beschrijven.", "De winst is groter dan bouw- en onderhoudskosten.", "Er is iemand verantwoordelijk voor data en gebruik."],
      },
      {
        heading: "Bouw klein, bewijs het nut",
        paragraphs: ["Begin met een scherm, koppeling of rapportage. Meet of het dagelijkse werk echt makkelijker wordt. Breid pas uit als de eerste stap staat. Zo voorkom je een systeem vol functies die niemand nodig heeft."],
      },
    ],
  },
];

type PublishedArticle = Omit<BlogPost, "date" | "readingTime"> & { publishedAt: string; updatedAt: string };
const contentApi = () => {
  const configured = process.env.COFFA_API_URL;
  if (!configured) return null;
  try {
    const base = new URL(configured);
    if (!["https:", "http:"].includes(base.protocol) || base.username || base.password) return null;
    return new URL("/api/blog", base.origin);
  } catch { return null; }
};
const fetchPublishedArticles = cache(async (): Promise<BlogPost[]> => {
  const endpoint = contentApi();
  if (!endpoint) return [];
  try {
    const response = await fetch(endpoint, { cache: "no-store", signal: AbortSignal.timeout(5000) });
    if (!response.ok) throw new Error(`Blog API returned ${response.status}`);
    const rows = await response.json() as PublishedArticle[];
    if (!Array.isArray(rows)) throw new Error("Blog API returned invalid content");
    return rows.filter(item => item && typeof item.slug === "string" && Array.isArray(item.sections) && typeof item.publishedAt === "string")
      .map(item => {
        const words = [item.title, item.intro, ...item.sections.flatMap(section => [section.heading, ...section.paragraphs, ...(section.points ?? [])])].join(" ").trim().split(/\s+/).length;
        const date = item.publishedAt.slice(0, 10);
        return { ...item, date, modifiedDate: item.updatedAt?.slice(0, 10), readingTime: `${Math.max(1, Math.ceil(words / 220))} min` };
      });
  } catch {
    console.error("published_blog_unavailable");
    return [];
  }
});
export async function getPublishedPosts(): Promise<BlogPost[]> {
  const remote = await fetchPublishedArticles();
  const bySlug = new Map([...posts, ...remote].map(post => [post.slug, post]));
  return [...bySlug.values()].sort((a, b) => b.date.localeCompare(a.date));
}
export async function getPublishedPost(slug: string): Promise<BlogPost | undefined> {
  const remote = await fetchPublishedArticles();
  return remote.find(post => post.slug === slug) ?? posts.find(post => post.slug === slug);
}
export function getPost(slug: string) { return posts.find((post) => post.slug === slug); }
export function formatDate(date: string) {
  return new Intl.DateTimeFormat("nl-NL", { day: "numeric", month: "long", year: "numeric", timeZone: "UTC" }).format(new Date(`${date}T12:00:00Z`));
}
