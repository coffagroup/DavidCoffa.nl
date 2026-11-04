using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Coffa.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class PersistentKeyringAndTicketEmail : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "EmailAttempts",
                table: "UserNotifications",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "EmailLastError",
                table: "UserNotifications",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EmailRecipient",
                table: "UserNotifications",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "EmailRetryAt",
                table: "UserNotifications",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "EmailSentAt",
                table: "UserNotifications",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EmailState",
                table: "UserNotifications",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LeaseUntil",
                table: "UserNotifications",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DataProtectionKeys",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FriendlyName = table.Column<string>(type: "text", nullable: true),
                    Xml = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DataProtectionKeys", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserNotifications_EmailState_EmailRetryAt",
                table: "UserNotifications",
                columns: new[] { "EmailState", "EmailRetryAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DataProtectionKeys");

            migrationBuilder.DropIndex(
                name: "IX_UserNotifications_EmailState_EmailRetryAt",
                table: "UserNotifications");

            migrationBuilder.DropColumn(
                name: "EmailAttempts",
                table: "UserNotifications");

            migrationBuilder.DropColumn(
                name: "EmailLastError",
                table: "UserNotifications");

            migrationBuilder.DropColumn(
                name: "EmailRecipient",
                table: "UserNotifications");

            migrationBuilder.DropColumn(
                name: "EmailRetryAt",
                table: "UserNotifications");

            migrationBuilder.DropColumn(
                name: "EmailSentAt",
                table: "UserNotifications");

            migrationBuilder.DropColumn(
                name: "EmailState",
                table: "UserNotifications");

            migrationBuilder.DropColumn(
                name: "LeaseUntil",
                table: "UserNotifications");
        }
    }
}
