using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DocuChat.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentVersioningAndSummary : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DocumentContentHashes",
                table: "QuestionCaches",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContentHash",
                table: "Documents",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Summary",
                table: "Documents",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DocumentContentHashes",
                table: "QuestionCaches");

            migrationBuilder.DropColumn(
                name: "ContentHash",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "Summary",
                table: "Documents");
        }
    }
}
