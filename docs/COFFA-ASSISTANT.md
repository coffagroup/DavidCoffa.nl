# Inspreken en invoerassistent

De knop **Inspreken** in de bovenbalk is beschikbaar voor SuperAdmin. De server controleert die rol opnieuw, ook bij directe API-verzoeken. Deze functie geeft medewerkers of klanten geen toegang tot de beheerdersassistent.

## Gebruik

1. Open Inspreken en neem je opdracht op, kies een audiobestand of typ de opdracht.
2. Een opname stuur je pas naar OpenAI met **Opname omzetten naar tekst**. Controleer de transcriptie.
3. Kies **Opdracht uitwerken**. De assistent maakt ingevulde voorstellen voor nieuwe records.
4. Controleer per voorstel de klant, bedragen, btw, datums en overige velden. Kies **Opslaan** om het via de bestaande bedrijfs-API vast te leggen.

Voorbeeld: “Maak voor Bakker BV een conceptfactuur voor vier uur ontwikkeling à 95 euro exclusief 21 procent btw. Maak ook een maandelijks hostingabonnement van 25 euro exclusief 21 procent btw vanaf 1 december 2026.”

Ondersteund zijn nieuwe klanten, contactpersonen, leads, interne notities, projecten, taken, urenregistraties, supporttickets, offerteconcepten, factuurconcepten en abonnementen. Meerdere voorstellen in één opdracht worden afzonderlijk gecontroleerd. Een nieuw aangemaakte klant kan aan latere voorstellen met exact dezelfde klantnaam worden gekoppeld.

Bij een onbekende of dubbele klant-/projectnaam wordt geen ID gegokt. Kies dan zelf het juiste record. Bestaande records wijzigen, betaalregistraties, creditfacturen, bestanden importeren, uitnodigingen en account-/systeeminstellingen worden via de gewone schermen afgehandeld.

Offertes en facturen blijven concepten. Uitgeven en versturen blijven afzonderlijke acties. Delen met klanten en automatische abonnementsfacturatie staan in het voorstel uit; die keuzes kunnen tijdens controle bewust worden aangepast.

## API-sleutel lokaal instellen

```bash
npm run coffa:ai
```

Voer je eigen OpenAI API-sleutel in de terminal in. De invoer is verborgen. Het commando slaat de sleutel uitsluitend op in `.env.coffa.local`, buiten Git, met private bestandsrechten. Start daarna de API opnieuw met `npm run api:dev`.

De configuratie wordt op de .NET-service gelezen als `OPENAI_API_KEY` of `OpenAI__ApiKey`. De sleutel gaat niet naar Next-clientcode, localStorage, de database of het statusendpoint. Gebruik nooit een `NEXT_PUBLIC_`-variabele voor de sleutel. Deel sleutels niet in de chat.

Op Render stel je deze variabele uitsluitend op de **API-service** in. Er is geen sleutel op de publieke Next-service nodig. De optionele modelinstellingen zijn:

```text
OpenAI__PlanningModel=gpt-5.6-terra
OpenAI__TranscriptionModel=gpt-transcribe
```

Modeltoegang en API-tegoed hangen af van je eigen OpenAI-account. Ontbrekende configuratie, onbereikbaarheid, een ongeschikt model of een limiet geven een zichtbare fout. Er wordt dan geen succesvolle opslag geclaimd.

## Verwerking en grenzen

- De browser vraagt pas microfoontoegang wanneer je zelf op opnemen klikt. HTTPS of localhost en browserondersteuning voor MediaRecorder zijn vereist.
- Opnemen duurt maximaal 120 seconden; uploaden maximaal 15 MB. WebM, MP4/M4A, MP3/MPGA, MPEG, WAV, OGG en FLAC worden op extensie, grootte en herkenbare headers gecontroleerd. Dit is formaatcontrole; OpenAI controleert de decodeerbaarheid.
- Audio blijft in de browser zolang het venster openstaat. De server verwerkt de upload tijdelijk en slaat geen opname of transcriptie als document op. Framework-uploadbuffers kunnen tijdelijk op de private tijdelijke schijf staan en worden na de aanvraag vrijgegeven.
- De tekst/audio die je indient wordt door OpenAI verwerkt. Er worden geen complete CRM-, factuur- of klantenlijsten aan de prompt toegevoegd. Klant- en projectnamen worden pas lokaal tegen de database opgelost.
- Responses gebruikt `store:false`. Dit zet de opslag van de response als API-object uit; de verdere verwerking door OpenAI blijft onder de afspraken van je API-account vallen.
- Auditlog bevat alleen procesmetadata, geen API-sleutel, opname of volledige opdracht. Bij definitieve opslag gebruikt de gewone module zijn bestaande auditregistratie.
- De planner accepteert alleen vaste aanmaaktypen en toegestane velden. De AI kan geen URL, HTTP-methode, record-ID, rechten of uitvoerende tool bepalen.
- Maximaal 12 voorstellen per opdracht, 12.000 tekens invoer en 10 verwerkingsaanvragen per minuut per beheerder. Een providerverzoek wordt na 45 seconden afgebroken; geen automatische betaalde herhaalverzoeken.

## Uitnodigingen en klanten

Een CRM-klant is een **organisatie**. Een portaalgebruiker is een **persoon** met eigen inloggegevens. Elke klantuitnodiging verwijst verplicht naar één organisatie; acceptatie neemt die organisatie en klantrol uit het uitnodigingstoken over. Meerdere personen kunnen dus toegang krijgen tot hetzelfde klantbedrijf.

In het klantdossier staat **Portaaltoegang** met gekoppelde gebruikers en uitnodigingen. Vanuit dat dossier kun je een uitnodiging met vooraf geselecteerde organisatie voorbereiden. Het gebruikersoverzicht kan op organisatie filteren. Per selectie worden maximaal 200 gebruikers en uitnodigingen getoond.

Een klant aanmaken maakt bewust geen account of uitnodiging: niet ieder administratief klantrecord hoeft toegang te hebben. Contactpersonen worden ook niet vanzelf gebruikers. Dit voorkomt ongewenste toegang en houdt administratie en toegangsbeheer duidelijk.

## Verificatie

Integratietests gebruiken echte PostgreSQL en een vervangende OpenAI-provider. Ze controleren rol/CSRF, audiovalidatie, gegevensafscherming, klant-/projectselectie, weigeren van onbekende acties en overposting, en dat een voorstel geen factuur of abonnement opslaat. Providerprotocoltests controleren de HTTP-contracten met een testhandler. De echte OpenAI-koppeling en microfoontranscriptie moeten met de eigen sleutel en browser worden getest; er was tijdens implementatie geen sleutel geconfigureerd.

Gebaseerd op de officiële [OpenAI-transcriptiedocumentatie](https://developers.openai.com/api/docs/guides/speech-to-text), [Structured Outputs](https://developers.openai.com/api/docs/guides/structured-outputs) en [Responses API](https://developers.openai.com/api/reference/resources/responses/methods/create).
