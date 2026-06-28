using Microsoft.EntityFrameworkCore.Migrations;
using Pgvector;

#nullable disable

namespace DocuChat.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddFeedbackQuestionVector : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Feedback feature yeni — eski test verilerini temizle (QuestionVector NOT NULL eklenirken hata olmasın)
            // FK CASCADE ile ChatMessageFeedbackChunks da temizlenir
            migrationBuilder.Sql(@"DELETE FROM ""ChatMessageFeedbacks"";");

            migrationBuilder.AddColumn<Vector>(
                name: "QuestionVector",
                table: "ChatMessageFeedbacks",
                type: "vector(1024)",
                nullable: false);

            migrationBuilder.CreateIndex(
                name: "IX_ChatMessageFeedbacks_QuestionVector",
                table: "ChatMessageFeedbacks",
                column: "QuestionVector")
                .Annotation("Npgsql:IndexMethod", "hnsw")
                .Annotation("Npgsql:IndexOperators", new[] { "vector_cosine_ops" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ChatMessageFeedbacks_QuestionVector",
                table: "ChatMessageFeedbacks");

            migrationBuilder.DropColumn(
                name: "QuestionVector",
                table: "ChatMessageFeedbacks");
        }
    }
}
