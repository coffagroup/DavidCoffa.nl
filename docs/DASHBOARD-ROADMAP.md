# Vervolg: het Coffa-dashboard

Deze versie scheidt content (`src/lib`) van UI (`src/app`, `src/components`). De publieke contactflow slaat inmiddels aanvragen op via `POST /api/contact`, met een aparte repository voor PostgreSQL en lokale developmentopslag. Zie [CONTACT-SETUP.md](CONTACT-SETUP.md). De beheerinterface en authenticatie zijn vervolgstappen.

## Eerste release van het dashboard

1. **Toegang** — voeg `/admin` toe als beveiligde routegroep. Gebruik bijvoorbeeld Auth.js met een betrouwbare provider / passkeys of een beheerde authdienst. Bescherm elke serveractie en API op serverniveau; een verborgen knop is geen beveiliging. Zet rollen `owner` en eventueel `editor` op de server.
2. **Database** — PostgreSQL + bijvoorbeeld Drizzle ORM. Tabellen: `users`, `posts`, `projects`, `services`, `contact_leads`, `audit_logs`. Unique slugs, `draft/published`-status, timestamps en foreign keys.
3. **Blog-CMS** — overzicht, artikel toevoegen/bewerken, concept/preview/publiceren, URL-slug en SEO-velden. Vervang de lokale `posts`-array door een getypeerde server-side repository.
4. **Projectenbeheer** — titel, categorie, afbeelding, beschrijving, status, link, sorteerpositie.
5. **Contactverzoeken** — gebruik de bestaande `contact_leads`-tabel en migratie uit `docs/migrations/001-contact-leads.sql`. Voeg een geauthenticeerd berichtenoverzicht en statuswijzigingen toe. Validatie, bodylimieten, honeypot, idempotente opslag en procesgebonden rate limiting zijn aanwezig in de publieke API. Stel bij opschalen een gedeelde limiet in en bepaal een bewaartermijn.
6. **Rapportages** — bezoekers/leadbronnen en uitgevoerde acties alleen met passende privacy-instellingen.

## Structuur om op door te bouwen

```text
src/
  app/
    (public)/          # optioneel: bestaande publieke routes hierin groeperen
    admin/             # PAS TOEVOEGEN MET SERVER-SIDE AUTH
      posts/
      projects/
      leads/
  components/
  lib/
    blog.ts            # huidige getypeerde inhoud
    content.ts         # huidige getypeerde projecten / diensten
    workflows.ts       # rekenvoorbeelden
    db/                # toekomstig: schema + migrations
    auth/              # toekomstig: sessies en autorisatie
```

## Vóór publicatie van een adminomgeving

- Auth, autorisatie en server-side checks op **elke** mutatie
- CSRF-/origin-controles waar nodig en bescherming tegen brute force
- Server-only secrets, environment variables, HTTPS
- Inputvalidatie met een gedeeld schema (bijv. Zod)
- Gescheiden draft/public content; preview zonder indexering
- Auditlog voor publicaties en deletes; back-ups en restoreprocedure
- Dataminimalisatie en passend privacybeleid

Het dashboard is een aparte, beveiligde applicatiefunctie, niet slechts een extra pagina.
