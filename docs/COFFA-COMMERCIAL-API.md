# Commerciële API

Alle routes beginnen met `/api`, vereisen een volledige aangemelde sessie en hebben bij mutaties een `X-CSRF-TOKEN` nodig. Via Next.js gebruiken ze `/api/os/...`.

`finance.read` geeft medewerkers inzage; `finance.write` geeft mutatierechten. Klanten krijgen uitsluitend expliciet gedeelde, definitieve offertes/facturen van hun eigen organisatie. Bedrijfsinstellingen zijn uitsluitend beheerbaar met `settings.manage`.

## Offertes

- `GET /quotes?search=&status=&organizationId=&page=1&pageSize=20`: `{items,total,page,pageSize}`.
- `POST /quotes`, `PUT /quotes/{id}`: `{organizationId,projectId?,title,validUntil,notes?,terms?,sharedWithCustomer,lines:[{description,quantity,unitPrice,discountPercent,taxRate}]}`. Datums als `yyyy-MM-dd`.
- `GET /quotes/{id}` en `GET /quotes/{id}/pdf`.
- `POST /quotes/{id}/issue`: definitief nummer en momentopname; genereert en bewaart ook de Coffa-template-PDF als document in het klantdossier.
- `POST /quotes/{id}/send`: geeft een concept zo nodig definitief uit en mailt de opgeslagen PDF als bijlage. Alleen echte SMTP-bevestiging zet `sent`; development-outbox retourneert `{sent:false,developmentOutbox:true,outboxId}`.
- `POST /quotes/{id}/accept` of `/reject`. Klanten kunnen dit uitsluitend voor de eigen gedeelde offerte.
- `POST /quotes/{id}/convert-to-invoice` en `/convert-to-project`: uitsluitend geaccepteerde offertes; herhalen retourneert dezelfde conversie.
- `POST /quotes/{id}/share`: `{sharedWithCustomer:true|false}`.

DTO's hebben `id,organizationId,projectId,number,title,status,validUntil,notes,terms,lines,netAmount,taxAmount,totalAmount,sharedWithCustomer,createdAt,issuedAt,sentAt,acceptedAt,convertedInvoiceId,convertedProjectId`. Statussen: `draft`, `issued`, `sent`, `accepted`, `rejected`, `expired`. Er wordt geen onbetrouwbare e-mailpixel als bekekenstatus gebruikt.

## Facturen

`GET/POST /invoices` en `GET/PUT /invoices/{id}`. Conceptbody gelijk aan offerte, met `invoiceDate,dueDate` in plaats van `validUntil`.

- `POST /invoices/{id}/issue`: definitief, uniek nummer onder database-lock. Nadien zijn bedragen, regels en momentopnamen onwijzigbaar.
- `POST /invoices/{id}/payments`: `{amount,date,reference?,idempotencyKey:uuid}`. Deelbetalingen toegestaan; herhaling met dezelfde UUID en inhoud telt eenmaal. Betaling boven openstaand bedrag wordt geweigerd.
- `POST /invoices/{id}/credit`: `{reason}`. Eén volledige creditcorrectie per originele factuur; de oorspronkelijke factuur blijft behouden. Reeds ontvangen bedragen verschijnen als `creditBalance`.
- `GET /invoices/{id}/pdf`, `POST /invoices/{id}/send` en `/share` zoals bij offertes. Uitgeven bewaart de Coffa-template-PDF als document gekoppeld aan de klant en factuur; verzenden vanuit een concept geeft de factuur uit en mailt die in één actie.
- `GET /invoices/{id}/reminder-preview`: `{to,subject,body}`. Alleen expliciet `POST /invoices/{id}/send-reminder` verzendt de herinnering.
- `POST /invoices/import`: multipart `file` (PDF, maximaal 10 MB) plus `metadata` als JSON: `{organizationId,projectId?,number,invoiceDate,dueDate,netAmount,taxAmount,totalAmount,paidAmount,paymentDate?}`. Losse gelijknamige formvelden worden ook ondersteund. Bij een ontvangen bedrag is de werkelijke betaaldatum verplicht; deze wordt niet afgeleid van de factuurdatum. Unieke oorspronkelijke nummers voorkomen dubbele registratie. PDF wordt privé opgeslagen, niet in de publieke webmap.
- `POST /invoices/from-time`: `{organizationId,timeEntryIds:uuid[],title?,invoiceDate?,dueDate?}`. Alias `/time-entries/invoice` gebruikt `entryIds`. Slaat een conceptfactuur op met de historische tariefmomentopnamen en koppelt de geselecteerde uren transactioneel; herhaling levert een conflict in plaats van een tweede factuur.

Factuur-DTO voegt toe: `kind` (`invoice`,`historical`,`credit`), `documentStatus`, `paidAmount`, `creditedAmount`, `outstandingAmount`, `creditBalance`, `payments`, `originalDocumentId`. `status` is afgeleid: `draft`,`open`,`partial`,`overdue`,`paid`,`credit`. Ontvangen bedragen zijn inclusief btw; omzet is exclusief btw.

## Abonnementen

`GET/POST /subscriptions`, `GET/PUT /subscriptions/{id}`. Body: `{organizationId,name,description,amount,taxRate,frequency,startDate,endDate?,nextInvoiceDate,status,automaticInvoicing}`. Frequenties `monthly`,`quarterly`,`yearly`; status `active`,`paused`,`ended`.

`POST /subscriptions/generate` geeft `{generated}`. Hetzelfde werk draait elk kwartier in een achtergrondproces. Een database-lock, unieke combinatie abonnement/periode en transactionele datumverschuiving voorkomen dubbele facturen. Het contractuele dagnummer blijft behouden, ook na februari. Automatische facturatie staat per contract standaard uit. Er worden uitsluitend definitieve facturen gemaakt als de bedrijfsgegevens compleet zijn; e-mailen staat standaard uit en vereist zowel de bedrijfsinstelling `automaticEmailEnabled` als de expliciete serverconfiguratie `Email:EnableSending=true`. Alleen bevestigde verzending krijgt `sentAt`; bij SMTP-verbindingsproblemen volgt een volgende poging. SMTP kan in een onderbroken bevestigingsvenster een dubbele bezorging opleveren, terwijl de factuur zelf altijd één keer wordt uitgegeven.

`GET /subscriptions/summary`: `{mrr,arr,activeContracts,endedContracts,upcoming}`. MRR telt maandelijkse, kwartaal- en jaarlijkse contracten omgerekend per maand, exclusief btw.

## Rapporten en dashboard

`GET/POST /expenses`, `GET/PUT/DELETE /expenses/{id}` vereist respectievelijk `finance.read` en `finance.write`. Body: `{date,supplier,description,netAmount,taxRate}`. Kosten worden ingevoerd exclusief btw; de API berekent btw en totaal, en archiveren verwijdert een uitgave uit de totalen.

`GET /finance?from=yyyy-MM-dd&to=yyyy-MM-dd` geeft:

- `invoicedRevenue`, `receivedPayments`, `expenses` (excl. btw), `expenseTax`, `outstandingAmount`, `overdueAmount`, `taxAmount`, `mrr`, `arr`.
- `previous:{from,to,invoicedRevenue,receivedPayments}` over een even lange vorige periode.
- `revenueSeries:[{period:'yyyy-MM',invoiced,received,costs}]`.
- `vatOverview:[{taxRate,netAmount,taxAmount}]`, `importedVat` (historische btw zonder verzonnen tariefverdeling).
- `revenueByCustomer:[{organizationId,revenue}]`, `revenueByProject:[{projectId,revenue}]`.

`GET /finance/export` met dezelfde periode retourneert CSV. Tekstcellen zijn beschermd tegen spreadsheetformules.

`GET /dashboard` retourneert actuele operationele informatie en activiteit van deze maand. Bij `finance.read` worden uitsluitend `monthlyRevenue` en `monthlyCosts` (beide excl. btw en op factuur-/uitgavendatum) aan de KPI's toegevoegd. Klanten ontvangen alleen eigen gedeelde project- en ticketinformatie; medewerkers zien alleen KPI's waarvoor zij rechten hebben. Er zijn geen voorbeeldwaarden in productie.

Dit is een management- en administratief overzicht. Het is geen grootboek, bankkoppeling of volledige boekhoudapplicatie.
