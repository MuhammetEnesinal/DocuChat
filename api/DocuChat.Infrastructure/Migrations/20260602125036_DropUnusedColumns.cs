using Microsoft.EntityFrameworkCore.Migrations;
using Pgvector;

#nullable disable

namespace DocuChat.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DropUnusedColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DocumentChunks_ContentHash",
                table: "DocumentChunks");

            migrationBuilder.DropColumn(
                name: "ContentHash",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "Embedding",
                table: "DocumentImages");

            migrationBuilder.DropColumn(
                name: "ImageType",
                table: "DocumentImages");

            migrationBuilder.DropColumn(
                name: "ContentHash",
                table: "DocumentChunks");

            migrationBuilder.DropColumn(
                name: "StructuredTableJson",
                table: "DocumentChunks");

            migrationBuilder.DropColumn(
                name: "TokenCount",
                table: "DocumentChunks");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ContentHash",
                table: "Documents",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Vector>(
                name: "Embedding",
                table: "DocumentImages",
                type: "vector(512)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ImageType",
                table: "DocumentImages",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContentHash",
                table: "DocumentChunks",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StructuredTableJson",
                table: "DocumentChunks",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TokenCount",
                table: "DocumentChunks",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_DocumentChunks_ContentHash",
                table: "DocumentChunks",
                column: "ContentHash");
        }
    }
}
