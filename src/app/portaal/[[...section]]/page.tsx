import CoffaApp from "@/components/portal/CoffaApp";
import { Suspense } from "react";
export const dynamic = "force-dynamic";
export default function PortalPage() { return <Suspense fallback={<p>Portaal laden…</p>}><CoffaApp /></Suspense>; }
