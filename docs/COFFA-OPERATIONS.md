# CRM, projecten, tickets en privébestanden

De operationele modules staan in `apps/api/Coffa.Api/Endpoints/Operations*.cs`; de database-entiteiten en EF-configuratie staan afzonderlijk in `Domain/OperationsModels.cs` en `Data/OperationsDbContext.cs`. Het volledige requestcontract staat in `COFFA-OPERATIONS-API.md`.

## Autorisatie

Klanten hebben uitsluitend toegang tot hun eigen actieve organisatie. Projecten en taken worden pas zichtbaar na expliciete deling; taken vereisen ook een gedeeld project. Klantprojecten bevatten geen budget, interne repositorylink of geregistreerde interne uren. Interne CRM-notities en ticketreacties zijn nooit klantzichtbaar. Ticketreacties en bijlagen worden bij ieder verzoek opnieuw op organisatie getoetst.

Medewerkers gebruiken de exacte rechten `crm.read/write`, `projects.read/write`, `time.read/write`, `tickets.read/write`, `documents.read/write`, `finance.read/write`. Budgetten en uurtarieven zijn aanvullend financieel afgeschermd. Ticketmedewerkers kunnen ticketbijlagen uploaden en downloaden zonder toegang te krijgen tot algemene documenten. Factuurbijlagen vereisen financiële rechten; offertebijlagen eveneens financiële rechten.

Leadconversie maakt in een transactie een organisatie en primaire contactpersoon aan en behoudt notities, labels en contactgeschiedenis. Herhalen van dezelfde conversie retourneert dezelfde organisatie. Factureerbare uren houden het oorspronkelijke uurtarief. Gefactureerde uren kunnen niet worden aangepast of verwijderd; wijziging en facturatie delen een PostgreSQL-transactielock.

## Privéopslag

Configureer productie met:

- `Storage__Bucket`
- `Storage__Endpoint` voor een S3-compatibele HTTPS-endpoint; weglaten voor standaard S3
- `Storage__Region`, standaard `eu-west-1`
- `Storage__AccessKey` en `Storage__SecretKey`, uitsluitend als geheime servervariabelen

Gebruik een private bucket. Geef de applicatie uitsluitend objectlezen, schrijven en verwijderen binnen de bedoelde bucket. Public-access-instellingen en bucketbeheer horen bij infrastructuurbeheer, niet bij bezoekers. Bestanden worden alleen via een geautoriseerde downloadroute aangeboden, met attachment-dispositie en `nosniff`. Er worden geen publieke object-URL's uitgegeven.

Zonder opslagconfiguratie gebruiken uitsluitend `Development` en `Testing` een private map `.private/objects` onder de API. `Storage__DevelopmentDirectory` kan die map aanpassen. Deze ontwikkeladapter mag niet als permanente Render-opslag worden ingezet. Productie zonder S3-configuratie retourneert een duidelijke configuratiefout.

Uploads zijn beperkt tot 20 MiB. PDF, PNG, JPEG, WebP, UTF-8 tekst/CSV en DOCX/XLSX worden op extensie én signatuur gecontroleerd. Office-archieven hebben begrensde aantallen en uitgepakte omvang; macrobestanden worden geweigerd. Bestanden worden niet uitgevoerd of inline weergegeven. Dit vervangt geen virusscanner als een latere bedrijfsomgeving die vereist.

## Ticketmeldingen

Publieke ticketacties maken persistente meldingen voor de juiste ontvangers. Interne reacties genereren geen klantmelding. Meldingen blijven in het portaal beschikbaar, onafhankelijk van e-mailconfiguratie.

`Tickets__EmailNotificationsEnabled=true` activeert de persistente e-mailoutboxworker. Deze staat standaard uit. De worker gebruikt de centrale e-mailservice en controleert vóór verzending opnieuw of de ontvanger actief is en nog toegang heeft tot de organisatie/tickets. E-mailfouten veranderen een reeds opgeslagen ticketreactie niet in een API-fout. Ze krijgen een volgende herprobeertijd; de daadwerkelijke verzendstatus en pogingen worden opgeslagen. Ontwikkel-outbox is nadrukkelijk een andere status dan daadwerkelijk verzonden.

De worker gebruikt een databaselease en `SKIP LOCKED` voor meerdere processen. SMTP heeft geen universele idempotentiesleutel; bij een procescrash precies tussen SMTP-acceptatie en databasebevestiging kan een melding opnieuw worden verstuurd. Ticketreacties zelf worden hierdoor nooit opnieuw aangemaakt. Schakel productie-e-mail pas in met de centrale SMTP-configuratie en gecontroleerde afzenderinstellingen.

## Websiteaanvragen naar de salespipeline

`POST /api/public/contact` is een server-naar-server-koppeling. `Contact__ApiKey` moet minstens 32 tekens bevatten en uitsluitend door de Next-server worden gebruikt als `X-Coffa-Contact-Key`. De route controleert de sleutel constant-time, begrenst de body tot 24 KiB en valideert de velden opnieuw. De API maakt een saleslead en ontvangstbewijs atomair aan. Hetzelfde aanvraag-ID met dezelfde inhoud is idempotent; gewijzigde inhoud bij hetzelfde ID geeft een conflict. Een ontbrekende sleutel schakelt de route uit met een configuratiefout. Deze route bevat geen publieke CRM-leesfunctie.

## Integratietests

`OperationsTests` gebruikt de gedeelde geïsoleerde PostgreSQL-testfixture. De tests controleren twee verschillende klantorganisaties, beperkte medewerkerrechten, interne notities, publiek ticketverkeer, geautoriseerde downloads, uploadvalidatie, timerpersistentie, historische uurtarieven, geblokkeerde wijzigingen van gefactureerde tijd, leadconversie en websiteaanvraag-idempotentie. Er worden geen echte klantmails verstuurd.
