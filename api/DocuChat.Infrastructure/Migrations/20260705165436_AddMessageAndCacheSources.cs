using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DocuChat.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMessageAndCacheSources : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SourcesJson",
                table: "QuestionCaches",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourcesJson",
                table: "ChatMessages",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SourcesJson",
                table: "QuestionCaches");

            migrationBuilder.DropColumn(
                name: "SourcesJson",
                table: "ChatMessages");
        }
    }
}
