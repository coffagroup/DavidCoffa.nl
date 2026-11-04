# Coffa OS API

Modulaire ASP.NET Core 10-monoliet met Identity, EF Core en PostgreSQL. De bestaande Next.js-website blijft op de repository-root. Browserrequests gaan via de Next BFF `/api/os/*` naar de private API `/api/*`, zodat de browser één origin gebruikt. De API geeft geen CORS-permissies aan externe origins.

## Configuratie en starten

Gebruik `apps/api/Coffa.Api/.env.example` als overzicht van de .NET-omgevingsvariabelen; .NET laadt dit bestand niet automatisch. Vul secrets uitsluitend in je lokale shell/processconfiguratie of in Render. `ConnectionStrings__Default` accepteert een Npgsql-verbindingsstring of een PostgreSQL-URI. `DATABASE_URL` is een alternatief, zoals beschikbaar op Render; percent-encoded credentials en een expliciete `sslmode` worden ondersteund.

Start PostgreSQL met de repository-composeconfiguratie. Herstel en bouw met `dotnet restore Coffa.sln` en `dotnet build Coffa.sln`. Voer migraties expliciet uit met `dotnet run --project apps/api/Coffa.Api -- --migrate`. Start daarna met `dotnet run --project apps/api/Coffa.Api` en de ingestelde `ASPNETCORE_URLS`. `Database__AutoMigrate=true` is beschikbaar voor lokale ontwikkeling; gebruik in productie een aparte migratiestap. `/health` controleert de databaseverbinding. `/openapi/v1.json` is alleen beschikbaar in Development/Testing.

## Eerste beheerder

Stel eenmalig `Bootstrap__Email`, een sterk `Bootstrap__Password` (minimaal twaalf tekens) en eventueel `Bootstrap__Name` in. Er wordt uitsluitend een eerste SuperAdmin aangemaakt wanneer er nog geen SuperAdmin bestaat. Verwijder daarna de bootstrapwaarden uit de hostingconfiguratie. Er is geen publieke registratie-endpoint.

De eerste login levert uitsluitend een sessie voor het activeren van 2FA. Alle bedrijfsendpoints blijven ontoegankelijk totdat een authenticatorcode is geverifieerd. De response van `/api/auth/2fa/enable` bevat tien recoverycodes die je veilig moet bewaren. Later inloggen vereist wachtwoord plus TOTP of een eenmalige recoverycode.

## Sessies en CSRF

Identity gebruikt een HttpOnly, SameSite=Strict-cookie. In productie is Secure verplicht. Iedere request controleert server-side de sessierij, actieve gebruiker, verloopdatum en sessieversie; deactivering, rechtenwijziging en wachtwoordreset trekken oude sessies in. De beperkte 2FA-setupsessie geldt twintig minuten; een volledige sessie acht uur. Alleen volledige sessies voldoen aan de standaard-autorisatiepolicy.

Haal vóór een mutatie een token op via `GET /api/auth/csrf`, inclusief de bijbehorende cookie, en stuur dit als `X-CSRF-TOKEN`. Dit geldt ook vóór login en uitnodiging accepteren. Haal na login/2FA een nieuw token op omdat het aan de identiteit is gekoppeld. Alleen de server-to-server publieke contactadapter heeft expliciete CSRF-exemptiemetadata; die adapter controleert zelf een geheime serverkey.

De standaard Data Protection-keyring wordt met de officiële EF-provider in PostgreSQL opgeslagen (`DataProtectionKeys`). Alle API-instanties gebruiken dezelfde applicatienaam en gedeelde keyring; een lokale Render-schijf is hiervoor niet nodig. Databaseback-ups bevatten ook de keys. Configureer optioneel `DataProtection__CertificatePath` en `DataProtection__CertificatePassword` om de keyring met een eigen PKCS#12-certificaat te beschermen. Bewaar het certificaat buiten Git en zorg dat alle instanties het kunnen lezen. Zonder certificaat moet toegang tot de database en back-ups strikt worden beperkt. `DataProtection__Path` is alleen een optionele override voor een eigen blijvende privédirectory.

## E-mail

`Email__EnableSending` staat standaard uit. Development/Testing schrijft uitsluitend naar een privé-outbox (`Email__OutboxPath` of `.private/outbox`) en meldt `sent:false, developmentOutbox:true`. Er worden geen echte klantmails verstuurd. Productie zonder expliciete SMTP-configuratie weigert daadwerkelijke verzendacties. Publieke reset- en verificatieverzoeken geven dezelfde generieke response voor bestaande en onbekende accounts, zonder te claimen dat mail is verzonden. Beschikbaarheid van de mailservice is zichtbaar in beheeracties/logs.

Stel voor echte SMTP-verzending host, poort, gebruikersnaam, wachtwoord en geautoriseerde afzender in, en zet `Email__EnableSending=true`. Poort 465 gebruikt TLS vanaf de verbinding; overige poorten vereisen STARTTLS. Certificaatvalidatie blijft actief. Configureer SPF zodat de SMTP-verzenddienst mail voor het domein mag afleveren, publiceer de DKIM-records van die dienst en stel een DMARC-policy in die bij het domeinbeheer past. Deze DNS-waarden zijn providerafhankelijk; gebruik de exacte records van de gekozen provider.

## Migraties en tests

De ingecheckte EF-migraties bouwen alle Identity-, CRM-, commerciële en operationele tabellen. Gebruik `dotnet ef migrations add <Name> --project apps/api/Coffa.Api` met EF CLI 10.0.14 voor schemawijzigingen.

`dotnet test Coffa.sln` gebruikt een geïsoleerd schema in de lokale testdatabase via `COFFA_TEST_CONNECTION` of de private `.env.coffa.local` van de repository. Tests maken geen klantmails en verwijderen uitsluitend hun eigen gegenereerde testschema en privé-outbox. Auth-integratietests doorlopen echte cookies, CSRF, verplichte TOTP-setup, eenmalige recoverycodes, uitnodigingsverificatie, tenantafscherming en sessie-intrekking.
