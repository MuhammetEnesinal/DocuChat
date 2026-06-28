using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DocuChat.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DropFeedbackChunkTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChatMessageFeedbackChunks");

            migrationBuilder.DropColumn(
                name: "UsedChunkIdsJson",
                table: "ChatMessages");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "UsedChunkIdsJson",
                table: "ChatMessages",
                type: "jsonb",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ChatMessageFeedbackChunks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ChunkId = table.Column<Guid>(type: "uuid", nullable: false),
                    FeedbackId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatMessageFeedbackChunks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChatMessageFeedbackChunks_ChatMessageFeedbacks_FeedbackId",
                        column: x => x.FeedbackId,
                        principalTable: "ChatMessageFeedbacks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ChatMessageFeedbackChunks_DocumentChunks_ChunkId",
                        column: x => x.ChunkId,
                        principalTable: "DocumentChunks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChatMessageFeedbackChunks_ChunkId",
                table: "ChatMessageFeedbackChunks",
                column: "ChunkId");

            migrationBuilder.CreateIndex(
                name: "IX_ChatMessageFeedbackChunks_FeedbackId",
                table: "ChatMessageFeedbackChunks",
                column: "FeedbackId");

            migrationBuilder.CreateIndex(
                name: "IX_ChatMessageFeedbackChunks_FeedbackId_ChunkId",
                table: "ChatMessageFeedbackChunks",
                columns: new[] { "FeedbackId", "ChunkId" },
                unique: true);
        }
    }
}
