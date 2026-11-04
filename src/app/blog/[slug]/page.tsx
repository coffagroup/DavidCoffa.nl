import type { Metadata } from "next";
import Link from "next/link";
import { notFound } from "next/navigation";
import { getPublishedPosts, getPublishedPost, formatDate } from "@/lib/blog";
import CTA from "@/components/CTA";

type Props = { params: Promise<{ slug: string }> };
export async function generateMetadata({ params }: Props): Promise<Metadata> {
  const { slug } = await params;
  const post = await getPublishedPost(slug);
  if (!post) return { title: "Artikel niet gevonden" };
  const title = post.metaTitle || post.title;
  const description = post.metaDescription || post.excerpt;
  return { title, description, keywords: [post.focusKeyword, ...(post.relatedKeywords ?? [])].filter((keyword): keyword is string => Boolean(keyword)), alternates: { canonical: `/blog/${slug}` }, openGraph: { type: "article", title, description, publishedTime: `${post.date}T12:00:00Z`, modifiedTime: `${post.modifiedDate ?? post.date}T12:00:00Z` } };
}
export default async function BlogArticle({ params }: Props) {
  const { slug } = await params;
  const [post, posts] = await Promise.all([getPublishedPost(slug), getPublishedPosts()]);
  if (!post) notFound();
  const related = posts.filter((item) => item.slug !== post.slug).slice(0,2);
  const structuredData = { "@context": "https://schema.org", "@type": "Article", headline: post.title, description: post.metaDescription || post.excerpt, datePublished: `${post.date}T12:00:00Z`, dateModified: `${post.modifiedDate ?? post.date}T12:00:00Z`, author: { "@type": "Person", name: "David Coffa", url: "https://davidcoffa.nl/over" }, publisher: { "@type": "Organization", name: "David Coffa", url: "https://davidcoffa.nl" }, mainEntityOfPage: `https://davidcoffa.nl/blog/${post.slug}`, articleSection: post.category, keywords: [post.focusKeyword, ...(post.relatedKeywords ?? [])].filter(Boolean) };
  const safeJsonLd = JSON.stringify(structuredData).replace(/</g, "\\u003c");
  const internalLinks = (post.internalLinks ?? []).filter(path => path !== `/blog/${post.slug}`);
  return <><article className="blog-article shell"><script type="application/ld+json" dangerouslySetInnerHTML={{ __html: safeJsonLd }} /><Link href="/blog" className="back-link">← Terug naar alle artikelen</Link><div className="article-meta"><span>{post.category}</span><span>{formatDate(post.date)} · {post.readingTime} leestijd</span></div><h1>{post.title}</h1><p className="article-lead">{post.intro}</p><div className="article-divider"></div><div className="article-body">{post.sections.map((section)=><section key={section.heading}><h2>{section.heading}</h2>{section.paragraphs.map((paragraph)=><p key={paragraph}>{paragraph}</p>)}{section.points && <ul>{section.points.map((point)=><li key={point}>{point}</li>)}</ul>}</section>)}{internalLinks.length > 0 && <section><h2>Verder lezen</h2><ul>{internalLinks.map(path => <li key={path}><Link href={path}>{path.replaceAll("-", " ").replaceAll("/", "") || "Home"} ↗</Link></li>)}</ul></section>}</div><div className="article-signoff"><span className="brand-mark">dc<span>.</span></span><p>David Coffa<br /><small>Software developer & maker</small></p></div></article><section className="section shell related-posts"><h2>Lees ook</h2><div className="blog-grid">{related.map((item)=><Link key={item.slug} href={`/blog/${item.slug}`} className="blog-card"><div className="blog-card-head"><span>{item.category}</span><span>↗</span></div><div><h3>{item.title}</h3><p>{item.excerpt}</p></div><div className="blog-card-foot">{formatDate(item.date)}</div></Link>)}</div></section><CTA/></>;
}
