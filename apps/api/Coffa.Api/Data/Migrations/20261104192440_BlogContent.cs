using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Coffa.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class BlogContent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BlogResearches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Topic = table.Column<string>(type: "text", nullable: false),
                    TargetAudience = table.Column<string>(type: "text", nullable: false),
                    TargetRegion = table.Column<string>(type: "text", nullable: false),
                    Summary = table.Column<string>(type: "text", nullable: false),
                    OpportunitiesJson = table.Column<string>(type: "jsonb", nullable: false),
                    ExistingContentJson = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BlogResearches", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BlogArticles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ResearchId = table.Column<Guid>(type: "uuid", nullable: true),
                    Slug = table.Column<string>(type: "text", nullable: false),
                    Category = table.Column<string>(type: "text", nullable: false),
                    FocusKeyword = table.Column<string>(type: "text", nullable: false),
                    RelatedKeywordsJson = table.Column<string>(type: "jsonb", nullable: false),
                    SearchIntent = table.Column<string>(type: "text", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    MetaTitle = table.Column<string>(type: "text", nullable: false),
                    MetaDescription = table.Column<string>(type: "text", nullable: false),
                    Excerpt = table.Column<string>(type: "text", nullable: false),
                    Intro = table.Column<string>(type: "text", nullable: false),
                    SectionsJson = table.Column<string>(type: "jsonb", nullable: false),
                    InternalLinksJson = table.Column<string>(type: "jsonb", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PublishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BlogArticles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BlogArticles_BlogResearches_ResearchId",
                        column: x => x.ResearchId,
                        principalTable: "BlogResearches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BlogArticles_ResearchId",
                table: "BlogArticles",
                column: "ResearchId");

            migrationBuilder.CreateIndex(
                name: "IX_BlogArticles_Slug",
                table: "BlogArticles",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BlogArticles_Status_PublishedAt",
                table: "BlogArticles",
                columns: new[] { "Status", "PublishedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_BlogResearches_CreatedAt",
                table: "BlogResearches",
                column: "CreatedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BlogArticles");

            migrationBuilder.DropTable(
                name: "BlogResearches");
        }
    }
}
