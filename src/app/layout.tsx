import type { Metadata, Viewport } from "next";
import SiteChrome from "@/components/SiteChrome";
import { LanguageProvider } from "@/components/LanguageProvider";
import "./globals.css";
import "./conversion.css";
import "./analytics-consent.css";

const siteUrl = "https://davidcoffa.nl";
export const metadata: Metadata = {
  metadataBase: new URL(siteUrl),
  title: { default: "Website laten maken of automatiseren? | David Coffa", template: "%s | David Coffa" },
  description: "David Coffa bouwt websites, AI-oplossingen en automatiseringen voor mkb-bedrijven. Rechtstreeks met de developer, van eerste vraag tot werkende oplossing.",
  openGraph: { type: "website", locale: "nl_NL", siteName: "David Coffa", url: siteUrl, title: "Websites, AI & automatisering voor mkb | David Coffa", description: "Websites, AI-oplossingen en automatiseringen voor mkb-bedrijven. Rechtstreeks met de developer." },
  twitter: { card: "summary", title: "Websites, AI & automatisering voor mkb | David Coffa", description: "Websites, AI-oplossingen en automatiseringen voor mkb-bedrijven." },
  robots: { index: true, follow: true, googleBot: { index: true, follow: true, "max-image-preview": "large", "max-snippet": -1, "max-video-preview": -1 } },
  icons: { icon: "/favicon.svg" },
};
export const viewport: Viewport = { themeColor: "#f7f6f1", width: "device-width", initialScale: 1 };

const structuredData = {
  "@context": "https://schema.org",
  "@graph": [
    {
      "@type": "WebSite",
      "@id": `${siteUrl}/#website`,
      url: siteUrl,
      name: "David Coffa",
      inLanguage: "nl-NL",
      publisher: { "@id": `${siteUrl}/#person` },
    },
    {
      "@type": "Person",
      "@id": `${siteUrl}/#person`,
      name: "David Coffa",
      url: siteUrl,
      jobTitle: "Software developer",
      description: "Software developer die websites, AI-oplossingen en automatiseringen voor mkb-bedrijven bouwt.",
      knowsAbout: ["Websites voor het mkb", "Bedrijfsprocessen automatiseren", "Kunstmatige intelligentie", "Maatwerksoftware"],
      sameAs: ["https://www.linkedin.com/in/davidcoffa", "https://github.com/coffagroup"],
    },
  ],
};

export default function RootLayout({ children }: Readonly<{ children: React.ReactNode }>) {
  return <html lang="nl"><body id="top"><script type="application/ld+json" dangerouslySetInnerHTML={{ __html: JSON.stringify(structuredData).replace(/</g, "\\u003c") }} /><LanguageProvider><SiteChrome>{children}</SiteChrome></LanguageProvider></body></html>;
}
