import Image from "next/image";

const integrations = [
  { logo: "microsoft.svg", name: "Microsoft 365" },
  { logo: "google.svg", name: "Google Workspace" },
  { logo: "hubspot.svg", name: "HubSpot" },
  { logo: "shopify.svg", name: "Shopify" },
  { logo: "woocommerce.svg", name: "WooCommerce" },
  { logo: "zapier.svg", name: "Zapier" },
  { logo: "make.svg", name: "Make" },
  { logo: "notion.svg", name: "Notion" },
  { logo: "airtable.svg", name: "Airtable" },
  { logo: "zoho.svg", name: "Zoho" },
];

function IntegrationSet({ duplicate = false }: { duplicate?: boolean }) {
  return (
    <div className="integration-set" aria-hidden={duplicate || undefined}>
      {integrations.map((integration) => (
        <div className="integration-item" key={integration.name}>
          <Image className="integration-logo" src={`/integrations/${integration.logo}`} alt="" width={28} height={28} loading="eager" />
          <span>{integration.name}</span>
        </div>
      ))}
    </div>
  );
}

export default function IntegrationMarquee() {
  return (
    <section className="integration-section" aria-labelledby="integration-title">
      <div className="shell integration-intro">
        <div>
          <p className="eyebrow">JOUW APPS, SAMEN AAN HET WERK</p>
          <h2 id="integration-title">Software die met elkaar <em>samenwerkt.</em></h2>
        </div>
        <p>Werk je met Microsoft 365, Google Workspace, boekhoudsoftware of een CRM? Vaak kunnen bestaande tools worden gekoppeld en processen slimmer ingericht. Wat mogelijk is, hangt af van jouw systemen.</p>
      </div>
      <div className="integration-marquee" role="region" aria-label="Voorbeelden van veelgebruikte apps en systemen">
        <div className="integration-track">
          <IntegrationSet />
          <IntegrationSet duplicate />
        </div>
      </div>
      <p className="shell integration-note">Voorbeelden van apps en platforms — geen kant-en-klare integratiebelofte. <a href="/contact">Bespreek jouw systemen ↗</a></p>
    </section>
  );
}
