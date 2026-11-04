import type { MetadataRoute } from "next";
import { posts } from "@/lib/blog";
export default function sitemap(): MetadataRoute.Sitemap {
  const host = "https://davidcoffa.nl";
  const pages = [
    { path: "", priority: 1 },
    { path: "/diensten", priority: 0.9 },
    { path: "/automatiseringen", priority: 0.9 },
    { path: "/projecten", priority: 0.8 },
    { path: "/contact", priority: 0.8 },
    { path: "/werkwijze", priority: 0.7 },
    { path: "/over", priority: 0.6 },
    { path: "/blog", priority: 0.6 },
  ];
  return [...pages.map(({ path, priority }) => ({ url: `${host}${path}`, changeFrequency: "monthly" as const, priority })), ...posts.map(post => ({ url: `${host}/blog/${post.slug}`, lastModified: new Date(`${post.date}T12:00:00Z`), changeFrequency: "yearly" as const, priority: 0.5 }))];
}
