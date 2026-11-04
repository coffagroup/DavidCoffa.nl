export type Service = {
  id: string;
  number: string;
  title: string;
  summary: string;
  delivers: string[];
  icon: string;
};

export const services: Service[] = [
  {
    id: "data",
    number: "01",
    title: "Data & processen in kaart",
    summary: "Ontdek waar dubbel werk en onduidelijkheid ontstaan, zodat je gericht kunt verbeteren.",
    delivers: ["Bronneninventarisatie", "Eén gegevensmodel", "Heldere proceskaart"],
    icon: "database",
  },
  {
    id: "automation",
    number: "02",
    title: "Slimme automatiseringen",
    summary: "Herhaalwerk uit handen nemen: van nieuwe aanvragen tot offertes, herinneringen en interne taken.",
    delivers: ["Workflows", "API-koppelingen", "Minder invoerwerk"],
    icon: "automation",
  },
  {
    id: "websites",
    number: "03",
    title: "Websites die je aanbod helder maken",
    summary: "Een snelle, herkenbare website die uitlegt wat je doet en bezoekers helpt contact op te nemen.",
    delivers: ["Ontwerp & development", "SEO & performance", "Content & formulieren"],
    icon: "web",
  },
  {
    id: "platforms",
    number: "04",
    title: "Portalen & dashboards",
    summary: "Werk op één plek: inzicht, taken, klanten en documenten in een systeem dat voor jou klopt.",
    delivers: ["Klantportalen", "Interne tools", "Rapportages"],
    icon: "layout",
  },
  {
    id: "ai",
    number: "05",
    title: "AI waar het helpt",
    summary: "Zoeken, samenvatten en ondersteunen, met aandacht voor gegevens en controle door mensen.",
    delivers: ["Slim zoeken", "AI-assistenten", "Menselijke controle"],
    icon: "spark",
  },
];

export type Project = {
  slug: string;
  title: string;
  kind: string;
  description: string;
  stack: string;
  state: string;
  artwork: "real-estate" | "construction" | "ai";
  href?: string;
};

export const projects: Project[] = [
  {
    slug: "fr-vastgoed",
    title: "F&R Vastgoed",
    kind: "AI · vastgoed leadgeneratie",
    description: "Voor F&R Vastgoed: een vastgoed lead generator die volledig op AI is gebaseerd. De website is te bekijken op fr-vastgoed.nl.",
    stack: "AI · Vastgoed · Leadgeneratie",
    state: "Project",
    artwork: "real-estate",
    href: "https://fr-vastgoed.nl/",
  },
  {
    slug: "bouwwonders",
    title: "BouwWonders",
    kind: "Project",
    description: "Project voor BouwWonders.",
    stack: "BouwWonders",
    state: "Project",
    artwork: "construction",
  },
  {
    slug: "orangegpt",
    title: "OrangeGPT",
    kind: "Project",
    description: "Project onder de naam OrangeGPT.",
    stack: "OrangeGPT",
    state: "Project",
    artwork: "ai",
  },
];
