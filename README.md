# DavidCoffa.nl

Next.js App Router, TypeScript en CSS. Een Nederlandse website voor David Coffa, met websites, portalen en automatisering voor het mkb als aanbod.

## Lokaal starten

```bash
npm install
npm run dev
```

Open `http://localhost:3000`. Bij lokale ontwikkeling werkt het contactformulier zonder externe dienst: aanvragen worden opgeslagen in het private, door Git genegeerde bestand `.data/contact-leads.jsonl`.

## Pagina's en conversie

- `/`: klantvoordeel, drie herkenbare vraagstukken, een bestaand project, een concrete werkwijze, automatiseringsvoorbeelden en veelgestelde vragen.
- `/diensten`, `/werkwijze`, `/projecten`, `/over`: aanbod, aanpak, de drie projecten F&R Vastgoed, BouwWonders en OrangeGPT, en persoonlijke achtergrond.
- `/automatiseringen`: filterbare catalogus en interactieve tijdwinstsimulator.
- `/blog` en `/blog/[slug]`: artikelen uit `src/lib/blog.ts`.
- `/contact`: toegankelijk formulier, vervolgstappen en directe contactmogelijkheden.
- `POST /api/contact`: centrale berichtenopslag, voorbereid op het toekomstige dashboard.

De primaire actie is overal **Bespreek jouw uitdaging**. Diensten kunnen het onderwerp vooraf invullen via `/contact?onderwerp=...`.

Het bestaande groene palet en `dc.`-logo blijven de basis; terracotta `#B84D2F` markeert primaire contactacties. De uitbreidingen staan in `src/app/conversion.css`, de oorspronkelijke basis in `src/app/globals.css`. De website gebruikt geen externe component- of animatiebibliotheken.

## Controleren en bouwen

```bash
npm run typecheck
npm run test:contact
npm run build
npm run start
```

De contacttests controleren invoer, bodylimieten, origin, spambeperking, opslagfouten, idempotentie, parameterbinding en lokale bestandspersistentie. Ze gebruiken geen live database en sturen geen e-mail.

## Berichten en het dashboard

Het formulier bevestigt ontvangst na succesvolle opslag. Een UUID voorkomt dubbele opslag bij dezelfde inzending. De publieke API kan uitsluitend berichten ontvangen; er is geen openbaar berichtenoverzicht.

Voor productie:

1. Voer `docs/migrations/001-contact-leads.sql` uit op de PostgreSQL-database die het toekomstige dashboard gaat gebruiken.
2. Stel `DATABASE_URL` server-side in, met de TLS-instellingen van je database. Zie `.env.example`.
3. Controleer de publieke origin; gebruik zo nodig `CONTACT_ALLOWED_ORIGIN` bij een reverse proxy.
4. Controleer een echte inzending op de uiteindelijke hosting.

Zonder productieconfiguratie geeft de API een foutmelding met een directe mailmogelijkheid; er verschijnt geen onterechte ontvangstbevestiging. De lokale bestandsadapter is alleen bedoeld voor development/test.

Zie [contactconfiguratie](docs/CONTACT-SETUP.md) en de [dashboardroadmap](docs/DASHBOARD-ROADMAP.md). De dashboardinterface, authenticatie en hostingdatabase zijn vervolgstappen. Het huidige project bevat geen e-mailnotificaties of analyticsdienst.

## Inhoud beheren

- Diensten en projecten: `src/lib/content.ts`.
- Blog: `src/lib/blog.ts`; nieuwe slugs en metadata worden uit die bron opgebouwd.
- Automatiseringscatalogus: `src/lib/automation-cases.ts`.
- Tijdwinstscenario's: `src/lib/workflows.ts`.
- SEO-keywordonderzoek en pagina-indeling: `docs/SEO-KEYWORD-RESEARCH.md`.
- AI-leesbare samenvatting van de site: `public/llms.txt`.

De tijdwinstberekening is `(oude minuten − nieuwe minuten) × aantal per week × 52 / 12 / 60`. De uitkomsten zijn illustratieve rekenvoorbeelden, geen gemeten klantresultaten. De schematische projectbeelden zijn geen productiescreenshots. Voeg uitsluitend echte klantreacties en aantoonbare resultaten toe.

## Publieke contactgegevens

- E-mail: info@davidcoffa.nl
- Telefoon: +31 6 83 43 90 00
- LinkedIn: https://www.linkedin.com/in/davidcoffa
