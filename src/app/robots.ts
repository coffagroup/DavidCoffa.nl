import type { MetadataRoute } from "next";
export default function robots(): MetadataRoute.Robots { return { rules: [{ userAgent: "*", allow: "/", disallow: ["/admin", "/api/internal", "/api/os", "/api/blog", "/portaal"] }], sitemap: "https://davidcoffa.nl/sitemap.xml" }; }
