import { createContactHandler } from "@/lib/contact-handler";
import { getContactRepository } from "@/lib/contact-repository";

export const runtime = "nodejs";
export const dynamic = "force-dynamic";

export const POST = createContactHandler({
  repository: getContactRepository,
  allowedOrigin: process.env.CONTACT_ALLOWED_ORIGIN,
});
