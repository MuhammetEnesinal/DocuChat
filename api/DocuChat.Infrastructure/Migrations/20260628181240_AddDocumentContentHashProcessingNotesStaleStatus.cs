using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DocuChat.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentContentHashProcessingNotesStaleStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ContentHash",
                table: "Documents",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProcessingNotes",
                table: "Documents",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Documents_UserId_ContentHash",
                table: "Documents",
                columns: new[] { "UserId", "ContentHash" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Documents_UserId_ContentHash",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "ContentHash",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "ProcessingNotes",
                table: "Documents");
        }
    }
}
