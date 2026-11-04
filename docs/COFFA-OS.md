# Coffa OS

Coffa OS breidt de bestaande Next.js-site uit met een ASP.NET Core 10 API en PostgreSQL. De frontend blijft op de repository-root; de backend staat onder `apps/api/Coffa.Api`. De publieke pagina's behouden hun inhoud, SEO en contactflow. De footer bevat de link naar `/portaal`.

## Geïmplementeerde onderdelen

- Identity-accounts zonder openbare registratie; tijdelijke uitnodigingen, e-mailverificatie, wachtwoordreset, blokkering, sessiebeheer, TOTP en herstelcodes. SuperAdmin krijgt pas volledige toegang na 2FA.
- CRM met klantorganisaties, meerdere gebruikers per organisatie, contactpersonen, interne notities, klantdossiers en een salespipeline met opvolgacties en conversie naar klant.
- Offertes en facturen met serverberekening in `decimal`, korting, btw, concepten, definitieve nummers, snapshots, PDF, expliciete verzending, betalingen, deelbetalingen, creditfacturen en historische PDF-import.
- Abonnementen met maand-, kwartaal- en jaarfrequentie, MRR/ARR, facturatieachtergrondtaak en bescherming tegen dubbele perioden.
- Financiële rapportages op echte facturen en betalingen, periodefilters, btw, klant-/projectomzet, vergelijking en CSV-export.
- Projecten, taken/subtaken, Kanban, lijstweergave, deadlines, timers, handmatige uren en factureren van geselecteerde uren tegen het historisch opgeslagen tarief.
- Supporttickets met klantreacties, interne reacties, status, toewijzing, projectkoppeling, bijlagen en persistente meldingen. De optionele e-mailworker controleert ontvangerrechten opnieuw vóór verzending.
- Privédocumenten met metadata, autorisatie, uploadvalidatie en gecontroleerde downloads. Productie gebruikt configureerbare S3-opslag; lokale bestanden zijn uitsluitend toegestaan in Development/Testing.
- Dashboard en klantenportaal met rolafhankelijke navigatie, formulieren, zoekopdrachten, filters, paginering en fout-/laad-/lege toestanden.
- Websitecontacten worden via een serverkey idempotent als CRM-lead opgeslagen zodra beide applicaties gekoppeld zijn.
- SEO-blogstudio voor beheerders: kwalitatief zoekwoordenonderzoek, overlapcontrole met bestaande artikelen, bewerkbare AI-concepten en expliciete publicatie/depublicatie. Gepubliceerde artikelen verschijnen in de blog, artikelmetadata, Article-structured-data en sitemap.

De financiële schermen zijn een management- en administratief overzicht. Er worden geen bankbetalingen uitgevoerd. Een status 'verzonden' wordt alleen vastgelegd bij daadwerkelijke mailverzending; er wordt geen onbetrouwbare 'bekeken'-status gesimuleerd.

## Lokaal starten op macOS

Vereist: Node 20.9+ (aanbevolen 22), .NET 10 SDK, Docker/OrbStack.

```bash
npm install
npm run coffa:setup
docker compose --env-file .env.coffa.local up -d db
dotnet restore Coffa.sln
npm run api:migrate
npm run coffa:bootstrap
```

`coffa:setup` maakt een private `.env.coffa.local` met een willekeurig databasewachtwoord en een serverkey en vult uitsluitend ontbrekende `COFFA_*`-waarden in `.env.local` aan. Bestaande waarden blijven behouden. Beide bestanden blijven buiten Git. PostgreSQL luistert alleen lokaal op poort 55439 en gebruikt een eigen Docker-volume.

`coffa:bootstrap` vraagt interactief om het e-mailadres en een verborgen wachtwoord voor de eerste SuperAdmin. Het maakt uitsluitend de eerste beheerder en vervangt geen bestaande wachtwoorden. Het commando stopt na de bootstrap; er hoeft geen tweede server te blijven draaien.

Start daarna in twee terminals:

```bash
npm run api:dev
```

```bash
npm run dev
```

Open `http://localhost:3000/portaal`. De API draait op `http://127.0.0.1:5100`; `/health` controleert PostgreSQL. Bij de eerste beheerderslogin stel je een authenticator in en bewaar je de herstelcodes. Er zijn geen standaardproductieaccounts of voorbeeldbedrijfsgegevens toegevoegd.

### Lokale uitnodigingen en resetlinks

E-mail staat lokaal uit. Berichten worden uitsluitend in `apps/api/Coffa.Api/.private/outbox` opgeslagen met private bestandsrechten. De beheerinterface meldt dat er geen e-mail is verstuurd. Open lokaal de meest recente outbox-JSON om de uitnodigingslink te gebruiken; dit bestand wordt nooit via een publiek endpoint aangeboden. Test geen uitnodigingen met echte klantadressen voordat SMTP expliciet is geconfigureerd.

## Authenticatie en scheiding van gegevens

De browser gebruikt uitsluitend `/api/os/*` op hetzelfde domein. De Next-route stuurt alleen toegestane headers en cookies door naar de vaste serverconfiguratie `COFFA_API_URL`. Een browser kan de API-bestemming niet aanpassen. Mutaties vereisen een overeenkomende browser-origin én een Identity-gebonden antiforgerytoken. Cookies blijven HttpOnly, SameSite=Strict en in productie Secure. Er wordt geen toegangstoken in localStorage gezet.

De API controleert iedere sessie, actieve gebruiker, security stamp, sessieversie en de verplichte beheerders-2FA. Het verbergen van navigatie is alleen gebruikersgemak; alle rechten en klantfilters worden opnieuw op de server toegepast. Interne notities/reacties en financiële velden worden uit klant-DTO's weggelaten. Downloads lopen via de API; de private opslagkeys worden niet gepubliceerd.

De Data Protection-keyring staat via de officiële EF-provider in PostgreSQL, zodat sessies na een API-herstart blijven werken. De API heeft geen lokale Render-disk nodig. Optionele certificaatbescherming is beschreven in de backend-README.

## Productieconfiguratie

Next.js:

- `COFFA_API_URL`: absolute origin van de .NET-service, zonder `/api`-pad.
- `COFFA_CONTACT_KEY`: dezelfde waarde als `Contact__ApiKey` op de API; uitsluitend server-side.

.NET:

- `DATABASE_URL` of `ConnectionStrings__Default`: PostgreSQL-verbinding met passende TLS-instellingen.
- `ASPNETCORE_ENVIRONMENT=Production`.
- Op Render: `Hosting__HttpsOnlyProxy=true`. Alleen gebruiken achter een proxy die HTTPS afdwingt en de applicatiepoort afschermt. Dit zorgt dat de Secure-antiforgerycookie na TLS-terminatie correct werkt, zonder willekeurige forwarded headers te vertrouwen.
- `Public__PortalUrl`: exacte HTTPS-URL van het portaal, bijvoorbeeld `https://davidcoffa.nl/portaal`.
- `Contact__ApiKey`: willekeurige secret van minimaal 32 tekens, gedeeld met de Next-server.
- `OpenAI__ApiKey` of `OPENAI_API_KEY`: server-side API-key voor de AI-zoekwoordenworkflow en conceptgenerator. Optioneel `OpenAI__BlogModel` om het tekstmodel te kiezen; anders wordt `OpenAI__PlanningModel` gebruikt. De generator deelt met OpenAI alleen de ingevoerde briefing en bestaande artikelgegevens die nodig zijn om overlap te beperken.
- `Analytics__GoogleServiceAccountJson`: volledige Google Cloud-serviceaccount-JSON als servergeheim; geef het account alleen Viewer-toegang op de GA4-property en alleen-lezen toegang op Search Console.
- `Analytics__Ga4PropertyId`: numerieke GA4-property-ID (niet de `G-...` meet-ID).
- `Analytics__SearchConsoleSiteUrl`: optioneel de exacte Search Console-property, bijvoorbeeld `https://davidcoffa.nl/` of `sc-domain:davidcoffa.nl`.

Next.js:

- `NEXT_PUBLIC_GA_MEASUREMENT_ID`: GA4-meet-ID (`G-...`). De Google-tag wordt pas geladen nadat een websitebezoeker expliciet analytics-toestemming geeft; weigeren is altijd mogelijk en de voorkeur kan via de knop Cookievoorkeuren worden gewijzigd.
- `Storage__Bucket`, `Storage__Region`, `Storage__AccessKey`, `Storage__SecretKey` en indien nodig `Storage__Endpoint` met HTTPS. Houd de bucket privé; geef alleen de benodigde objectrechten.
- `Email__EnableSending=true` pas na het instellen van SMTP, geautoriseerde afzender, SPF, DKIM en DMARC. De exacte DNS-records komen van je eigen mailprovider.
- `Tickets__EmailNotificationsEnabled=true` pas wanneer ticketmails gewenst zijn en SMTP correct werkt.
- Bootstrapgegevens alleen eenmalig instellen; daarna verwijderen.

Het blogonderzoek gebruikt kwalitatieve intentie- en relevantie-inschattingen; er is geen Search Console- of Keyword Planner-koppeling. Zoekvolumes, concurrentiecijfers en rankinggaranties worden daarom niet getoond. Beoordeel concepten, feitelijke claims en interne links voordat je ze zelf publiceert. De publieke blog behoudt de bestaande artikelen wanneer de API tijdelijk niet bereikbaar is.

Website-statistieken worden via Google Analytics Data API en optioneel Google Search Console in het afgeschermde menu Marketing getoond. De rapportage bevat GA4-gebruikers, sessies, paginaweergaven, verkeersbronnen, pagina’s en (indien gekoppeld) Search Console-klikken, vertoningen, zoekopdrachten en pagina’s uit Google Zoeken over 7/30/90 dagen of 12 maanden. Data wordt maximaal 10 minuten gecachet; Search Console loopt doorgaans enkele dagen achter. Er worden geen statistieken bijgehouden voor bezoekers die analytics weigeren. Configureer de GA4-meet-ID in de Next-omgeving en de serviceaccount, property-ID en optionele Search Console-site op de API-server. Schakel in Google Cloud de gebruikte Analytics Data API en, indien gewenst, Search Console API in. Deel nooit de serviceaccount-key met de browser of commit die in de repository.

Automatische abonnementsmails staan eveneens uit totdat de bedrijfsinstelling én de verzendconfiguratie dit expliciet toestaan. Zonder configuratie geven bestanden/verzendacties een duidelijke fout; er wordt geen productiebestand stil naar een tijdelijke schijf geschreven.

## Render

Er was geen bestaande Render-configuratie in deze repository aanwezig. `render.coffa.yaml` is daarom een optioneel startpunt voor **nieuwe** services, met nieuwe namen. Het is niet gekoppeld aan een bestaande productieapp en is niet toegepast.

1. Bouw de API met `apps/api/Dockerfile`, context repository-root. De container draait als niet-rootgebruiker en luistert op poort 10000.
2. Stel de API-variabelen in en gebruik `/health` als healthcheck.
3. Voer vóór de nieuwe API-versie `dotnet Coffa.Api.dll --migrate` uit. Gebruik één migratiestap; laat willekeurige schaalinstanties niet gelijktijdig migreren.
4. Test eerst met een aparte webservice (`npm ci && npm run build`, daarna `npm run start -- --hostname 0.0.0.0 --port $PORT`). Vul de feitelijke API-URL in als `COFFA_API_URL`.
5. Pas `Public__PortalUrl` aan aan de daadwerkelijk gebruikte test- of productie-origin. Browsercookies lopen via Next; cross-origin CORS-toegang is niet nodig en blijft uitgeschakeld.
6. Koppel de bestaande publieke webservice pas na controle aan dezelfde API en serverkey. Bestaande productieconfiguratie is niet overschreven.

Render beëindigt TLS vóór de HTTP-hop naar de container en dwingt extern HTTPS af; de blueprint activeert daarom de hierboven beschreven hostinginstelling. Zie [Render web services](https://render.com/docs/web-services) en de [Microsoft-proxyrichtlijn](https://learn.microsoft.com/aspnet/core/host-and-deploy/proxy-load-balancer#when-it-isnt-possible-to-add-forwarded-headers-and-all-requests-are-secure).

Gebruik een continu draaiende API voor achtergrondtaken. Databaseback-ups, de private objectopslag, bewaartermijnen en monitoring moeten bij de uiteindelijke hosting worden ingesteld. De blueprint gebruikt de gedocumenteerde [Render-variabelenkoppeling en migratiestap](https://render.com/docs/blueprint-spec).

## Verificatie

```bash
npm run typecheck
npm run test:contact
npm run test:api
npm run build
npm run test:coffa-e2e
```

De .NET-tests gebruiken echte PostgreSQL-migraties, een apart willekeurig testschema en private testuitvoer. Ze verwijderen uitsluitend dat eigen schema. De HTTP-e2e-test start tijdelijk de API en de gebouwde Next-app en doorloopt echte BFF-cookies, CSRF, verplichte 2FA, een uitnodiging, tenantafscherming, ticketreacties, documenten, factuur/PDF/deelbetaling, contact→CRM en uitloggen. Er worden geen echte mails verstuurd.

Gecontroleerd tijdens implementatie: Next.js-productiebuild en TypeScript, 42 .NET-tests met PostgreSQL, 15 contacttests, de volledige HTTP-e2e-stroom, PDF-layouts en de desktop- en mobiele portaalnavigatie. Er is geen afzonderlijk lintcommando in deze repository. De Linux-container is lokaal gebouwd en gestart; dit vervangt geen test op een echte Render-omgeving. Live SMTP en S3 zijn zonder productiecredentials niet getest.

Voor backenddetails: [API](../apps/api/Coffa.Api/README.md), [operations](COFFA-OPERATIONS.md), [commercieel contract](COFFA-COMMERCIAL-API.md).

## Wat nog moet worden geconfigureerd voor dagelijks productiegebruik

De code en lokale workflows zijn aanwezig. De echte Render-services, productie-PostgreSQL, private S3-bucket, SMTP/DNS en je persoonlijke eerste SuperAdmin-account zijn niet namens jou ingericht. Er zijn geen productieaccounts, facturen, echte klantmails of bankbetalingen aangemaakt. Kies en controleer die instellingen voordat je klanten uitnodigt.

## Inspreken

SuperAdmin kan nieuwe bedrijfsrecords dicteren of typen via **Inspreken**. De assistent vult controleerbare formulieren in; de gewone API blijft verantwoordelijk voor opslag en validatie. Stel lokaal de sleutel in met `npm run coffa:ai`, of configureer `OPENAI_API_KEY` op de productie-API-service. Zie [spraakassistent en klantuitnodigingen](COFFA-ASSISTANT.md) voor mogelijkheden, grenzen, gegevensverwerking en verificatie.
