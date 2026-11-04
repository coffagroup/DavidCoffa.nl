import type { Metadata } from "next";
import Link from "next/link";
import { getPublishedPosts, formatDate } from "@/lib/blog";
import CTA from "@/components/CTA";

export const metadata: Metadata = { title: "Blog over AI, websites & automatisering voor mkb", description: "Praktische artikelen voor mkb-bedrijven over AI, bedrijfsprocessen automatiseren, websites, data en maatwerksoftware.", alternates: { canonical: "/blog" } };
export default async function BlogPage() { const posts = await getPublishedPosts(); return <>
  <section className="page-hero shell"><p className="eyebrow">BLOG / UIT DE PRAKTIJK</p><h1>Gedachten tussen<br />de <em>code door.</em></h1><p>Over data, automatisering, websites en software. Geen buzzwords om de buzzwords, maar praktische ideeën.</p></section>
  <section className="section shell"><div className="blog-list">{posts.map((post,index)=><Link key={post.slug} href={`/blog/${post.slug}`} className={`blog-list-item ${index===0 ? "featured" : ""}`}><div className="blog-list-index">0{index+1}</div><div><div className="blog-list-meta">{post.category} <span>·</span> {formatDate(post.date)} <span>·</span> {post.readingTime}</div><h2>{post.title}</h2><p>{post.excerpt}</p></div><span className="blog-list-arrow">↗</span></Link>)}</div></section><CTA/>
</> }
