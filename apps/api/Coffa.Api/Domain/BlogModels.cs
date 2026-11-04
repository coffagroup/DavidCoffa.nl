namespace Coffa.Api.Domain;

public sealed class BlogResearch
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Topic { get; set; } = "";
    public string TargetAudience { get; set; } = "";
    public string TargetRegion { get; set; } = "";
    public string Summary { get; set; } = "";
    public string OpportunitiesJson { get; set; } = "[]";
    public string ExistingContentJson { get; set; } = "[]";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string CreatedBy { get; set; } = "";
}

public sealed class BlogArticle
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? ResearchId { get; set; }
    public string Slug { get; set; } = "";
    public string Category { get; set; } = "";
    public string FocusKeyword { get; set; } = "";
    public string RelatedKeywordsJson { get; set; } = "[]";
    public string SearchIntent { get; set; } = "";
    public string Title { get; set; } = "";
    public string MetaTitle { get; set; } = "";
    public string MetaDescription { get; set; } = "";
    public string Excerpt { get; set; } = "";
    public string Intro { get; set; } = "";
    public string SectionsJson { get; set; } = "[]";
    public string InternalLinksJson { get; set; } = "[]";
    public string Status { get; set; } = "draft";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? PublishedAt { get; set; }
    public string CreatedBy { get; set; } = "";
}
