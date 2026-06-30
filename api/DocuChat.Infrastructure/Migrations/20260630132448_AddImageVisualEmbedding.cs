using Microsoft.EntityFrameworkCore.Migrations;
using Pgvector;

#nullable disable

namespace DocuChat.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddImageVisualEmbedding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Vector>(
                name: "VisualEmbedding",
                table: "DocumentImages",
                type: "vector(512)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_DocumentImages_VisualEmbedding",
                table: "DocumentImages",
                column: "VisualEmbedding")
                .Annotation("Npgsql:IndexMethod", "hnsw")
                .Annotation("Npgsql:IndexOperators", new[] { "vector_cosine_ops" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DocumentImages_VisualEmbedding",
                table: "DocumentImages");

            migrationBuilder.DropColumn(
                name: "VisualEmbedding",
                table: "DocumentImages");
        }
    }
}
