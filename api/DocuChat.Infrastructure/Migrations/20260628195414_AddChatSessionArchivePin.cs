using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DocuChat.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddChatSessionArchivePin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ChatSessions_UserId",
                table: "ChatSessions");

            migrationBuilder.AddColumn<DateTime>(
                name: "ArchivedAt",
                table: "ChatSessions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsArchived",
                table: "ChatSessions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsPinned",
                table: "ChatSessions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "PinnedAt",
                table: "ChatSessions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChatSessions_UserId_IsArchived_IsPinned",
                table: "ChatSessions",
                columns: new[] { "UserId", "IsArchived", "IsPinned" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ChatSessions_UserId_IsArchived_IsPinned",
                table: "ChatSessions");

            migrationBuilder.DropColumn(
                name: "ArchivedAt",
                table: "ChatSessions");

            migrationBuilder.DropColumn(
                name: "IsArchived",
                table: "ChatSessions");

            migrationBuilder.DropColumn(
                name: "IsPinned",
                table: "ChatSessions");

            migrationBuilder.DropColumn(
                name: "PinnedAt",
                table: "ChatSessions");

            migrationBuilder.CreateIndex(
                name: "IX_ChatSessions_UserId",
                table: "ChatSessions",
                column: "UserId");
        }
    }
}
