# Contactberichten en het toekomstige dashboard

Het contactformulier verstuurt naar `POST /api/contact`. De server valideert naam, e-mail, optioneel onderwerp en vraag, controleert de origin en een honeypot en past een procesgebonden limiet toe (5 berichten per e-mailadres en 100 totaal per 15 minuten). E-mailadressen voor deze limiet worden met een willekeurig processalt gehasht. Er wordt geen IP-adres opgeslagen.

Een succesmelding verschijnt pas nadat het bericht is opgeslagen. De browser gebruikt een submission-UUID opnieuw bij een netwerkretry, zodat hetzelfde bericht niet dubbel wordt opgeslagen. Een gewijzigde inhoud krijgt een nieuwe UUID. Er wordt geen e-mail verzonden en er is geen publiek endpoint om berichten op te vragen.

## Lokale ontwikkeling

Bij `npm run dev` en zonder `DATABASE_URL` schrijft de server naar `.data/contact-leads.jsonl`, buiten `public/` en uitgesloten van Git. Schrijfacties binnen het ontwikkelproces worden geserialiseerd; het bestand krijgt rechten `0600`. Het bestand wordt gesynchroniseerd voordat een succesantwoord teruggaat. Deze adapter is uitsluitend voor development/test, voor één lokaal serverproces. Verwijder lokale testberichten zodra je ze niet meer nodig hebt.

## Productie met PostgreSQL

1. Gebruik de database die ook door je toekomstige dashboard wordt gebruikt.
2. Voer `docs/migrations/001-contact-leads.sql` eenmalig uit met een migratierol.
3. Stel server-side `DATABASE_URL` in met de juiste TLS-instellingen voor je database. Kopieer indien nodig `.env.example` naar `.env.local` voor lokale verbindingstests; commit nooit de ingevulde gegevens.
4. Geef de applicatierol uitsluitend `INSERT` en `SELECT` op `contact_leads`; de tabel aanmaken gebeurt buiten de applicatie.
5. Stel eventueel `CONTACT_ALLOWED_ORIGIN=https://davidcoffa.nl` in wanneer de hostingproxy de request-origin herschrijft. Gebruik daarbij de daadwerkelijke origin waar het formulier draait (inclusief `www` als dat de publieke host is).
6. Controleer een inzending en de opgeslagen rij vóór livegang. Controleer zowel validatiefouten als een databasefout; alleen de eerste mag nooit als ontvangen worden bevestigd.

In productie wordt zonder databaseconfiguratie **geen** bestand als stille fallback gebruikt: de API geeft `503`, bewaart de ingevulde tekst in het formulier en biedt rechtstreeks mailen of bellen aan.

## Later aansluiten op het dashboard

Het dashboard kan `contact_leads` uitlezen en de status veranderen van `new` naar `contacted` of `closed`. De tabel en repository zijn hiervoor al gescheiden van de publieke UI. Bouw het beheer pas achter server-side authenticatie en autorisatie; de publieke contact-API biedt bewust geen lijst- of wijzigfunctie. Spreek bij livegang een bewaartermijn en verwijderproces voor persoonsgegevens af.

De limiter is per Node-proces, niet gedeeld over meerdere serverless instanties. Bij opschalen hoort een gedeelde limiter of de bescherming van het hostingplatform; het databasecontract en formulier hoeven daarvoor niet te veranderen.

## Verificatie

`npm run test:contact` test de requestgrenzen, validatie, origin, honeypot, limiter, storagefouten, idempotentie, parameterbinding voor PostgreSQL en echte lokale bestandspersistentie. De tests gebruiken geen live database en verzenden geen e-mail.

De PostgreSQL-adapter gebruikt [parametergebonden queries](https://node-postgres.com/features/queries) en een server-side [connection URI](https://node-postgres.com/features/connecting).
