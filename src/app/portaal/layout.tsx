import type { Metadata } from "next";
import "./portal.css";
import "./blog-manager.css";
import "./analytics.css";
export const metadata: Metadata = { title: "Coffa OS — Klantenportaal", robots: { index: false, follow: false } };
export default function PortalLayout({ children }: { children: React.ReactNode }) { return <div className="coffa-os">{children}</div>; }
