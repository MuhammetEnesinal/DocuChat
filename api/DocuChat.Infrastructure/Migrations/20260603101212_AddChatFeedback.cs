using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DocuChat.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddChatFeedback : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "UsedChunkIdsJson",
                table: "ChatMessages",
                type: "jsonb",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ChatMessageFeedbacks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    MessageId = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    QuestionText = table.Column<string>(type: "text", nullable: false),
                    AnswerText = table.Column<string>(type: "text", nullable: false),
                    Rating = table.Column<int>(type: "integer", nullable: false),
                    ReasonCategories = table.Column<List<string>>(type: "text[]", nullable: false),
                    ReasonText = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatMessageFeedbacks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChatMessageFeedbacks_ChatMessages_MessageId",
                        column: x => x.MessageId,
                        principalTable: "ChatMessages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ChatMessageFeedbackChunks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FeedbackId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChunkId = table.Column<Guid>(type: "uuid", nullable: false),
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

            migrationBuilder.CreateIndex(
                name: "IX_ChatMessageFeedbacks_CreatedAt",
                table: "ChatMessageFeedbacks",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_ChatMessageFeedbacks_MessageId",
                table: "ChatMessageFeedbacks",
                column: "MessageId");

            migrationBuilder.CreateIndex(
                name: "IX_ChatMessageFeedbacks_Rating",
                table: "ChatMessageFeedbacks",
                column: "Rating");

            migrationBuilder.CreateIndex(
                name: "IX_ChatMessageFeedbacks_UserId",
                table: "ChatMessageFeedbacks",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_ChatMessageFeedbacks_UserId_MessageId",
                table: "ChatMessageFeedbacks",
                columns: new[] { "UserId", "MessageId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChatMessageFeedbackChunks");

            migrationBuilder.DropTable(
                name: "ChatMessageFeedbacks");

            migrationBuilder.DropColumn(
                name: "UsedChunkIdsJson",
                table: "ChatMessages");
        }
    }
}
