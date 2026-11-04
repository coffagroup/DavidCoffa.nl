export type ContactValues = { name: string; email: string; subject: string; message: string; website: string };
export type ContactErrors = Partial<Record<keyof ContactValues, string>>;
export type ContactData = Omit<ContactValues, "website"> & { website: string };

export function validateContact(input: unknown): { data?: ContactData; errors: ContactErrors } {
  const errors: ContactErrors = {};
  if (!input || typeof input !== "object" || Array.isArray(input)) return { errors: { message: "Vul je naam, e-mailadres en vraag in." } };
  const raw = input as Record<string, unknown>;
  const field = (key: keyof ContactValues) => typeof raw[key] === "string" ? (raw[key] as string).trim() : "";
  const data = { name: field("name"), email: field("email"), subject: field("subject"), message: field("message"), website: field("website") };
  // PostgreSQL char_length also counts Unicode code points, not UTF-16 units.
  const messageLength = Array.from(data.message).length;
  if (!data.name || data.name.length > 120 || /[\r\n\u0000-\u001f\u007f]/.test(data.name)) errors.name = "Vul je naam in (maximaal 120 tekens).";
  if (!data.email || data.email.length > 160 || !/^[^\s@<>]+@[^\s@<>]+\.[^\s@<>]+$/.test(data.email) || /[\u0000-\u001f\u007f]/.test(data.email)) errors.email = "Vul een geldig e-mailadres in.";
  if ((raw.subject !== undefined && typeof raw.subject !== "string") || data.subject.length > 160 || /[\r\n\u0000-\u001f\u007f]/.test(data.subject)) errors.subject = "Gebruik maximaal 160 tekens voor het onderwerp.";
  if (messageLength < 10 || messageLength > 4000 || /[\u0000]/.test(data.message)) errors.message = "Beschrijf je vraag in 10 tot 4000 tekens.";
  if ((raw.website !== undefined && typeof raw.website !== "string") || data.website) errors.website = "Het bericht kon niet worden verwerkt.";
  return Object.keys(errors).length ? { errors } : { data, errors };
}
