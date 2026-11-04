export type Workflow = {
  id: string;
  label: string;
  title: string;
  problem: string;
  sources: string[];
  fields: string[];
  actions: string[];
  result: string;
  originalMinutes: number;
  improvedMinutes: number;
  defaultWeekly: number;
};

export const workflows: Workflow[] = [
  {
    id: "aanvragen",
    label: "Aanvragen",
    title: "Geen aanvraag meer tussen de mailtjes kwijt.",
    problem: "Een formulier, een e-mail en een Excel-lijst die niet gelijklopen.",
    sources: ["Contactformulier", "E-mail", "Excel"],
    fields: ["contact", "onderwerp", "prioriteit", "eigenaar"],
    actions: ["Aanvraag registreren", "Medewerker aanwijzen", "Ontvangst bevestigen"],
    result: "Eén overzicht, meteen de juiste opvolging.",
    originalMinutes: 12,
    improvedMinutes: 3,
    defaultWeekly: 25,
  },
  {
    id: "offertes",
    label: "Offertes",
    title: "Offertes zonder steeds hetzelfde overtypen.",
    problem: "Klantgegevens staan in drie systemen en de laatste versie is onduidelijk.",
    sources: ["CRM", "Prijslijst", "E-mail"],
    fields: ["klant_id", "producten", "tarief", "status"],
    actions: ["Conceptofferte genereren", "Status bijhouden", "Opvolging klaarzetten"],
    result: "Minder administratiewerk, meer tijd voor klantcontact.",
    originalMinutes: 30,
    improvedMinutes: 12,
    defaultWeekly: 12,
  },
  {
    id: "facturen",
    label: "Facturen",
    title: "Automatisch bijhouden wie nog moet betalen.",
    problem: "Betaalstatussen worden handmatig gecontroleerd en herinneringen vergeten.",
    sources: ["Boekhouding", "Bankstatus", "Klantbestand"],
    fields: ["factuurnr", "vervaldatum", "betaalstatus", "contact"],
    actions: ["Status synchroniseren", "Signaal bij achterstand", "Herinnering voorbereiden"],
    result: "Overzicht zonder elke week alles na te lopen.",
    originalMinutes: 9,
    improvedMinutes: 2,
    defaultWeekly: 40,
  },
  {
    id: "vastgoed",
    label: "Vastgoeddata",
    title: "Van versnipperde bronnen naar één bruikbare lijst.",
    problem: "Adressen, kenmerken en contactmomenten staan los van elkaar.",
    sources: ["Externe data", "CRM", "BAG / exports"],
    fields: ["adres_id", "objecttype", "bron", "laatste_update"],
    actions: ["Dubbele records herkennen", "Wijzigingen markeren", "Opvolgactie klaarzetten"],
    result: "Betrouwbaardere data en minder handmatig uitzoekwerk.",
    originalMinutes: 18,
    improvedMinutes: 6,
    defaultWeekly: 20,
  },
  {
    id: "service",
    label: "Klantvragen",
    title: "Vragen op één plek, ook als ze overal binnenkomen.",
    problem: "WhatsApp, mail en website leiden tot losse to-do-lijstjes.",
    sources: ["Website", "E-mail", "Berichten"],
    fields: ["klant", "categorie", "status", "toegewezen_aan"],
    actions: ["Ticket aanmaken", "Routeren naar collega", "Status terugkoppelen"],
    result: "Sneller een antwoord, minder intern zoekwerk.",
    originalMinutes: 14,
    improvedMinutes: 5,
    defaultWeekly: 18,
  },
];
