"use client";
import { usePathname } from "next/navigation";
import SiteHeader from "@/components/SiteHeader";
import SiteFooter from "@/components/SiteFooter";
import AnalyticsConsent from "@/components/AnalyticsConsent";
export default function SiteChrome({ children }: { children: React.ReactNode }) {
  const path = usePathname();
  if (path === "/portaal" || path.startsWith("/portaal/")) return <>{children}</>;
  return <><a href="#content" className="skip-link">Direct naar inhoud</a><SiteHeader /><main id="content">{children}</main><SiteFooter /><AnalyticsConsent /></>;
}
