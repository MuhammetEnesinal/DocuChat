using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DocuChat.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DropChunkImagePath : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ImagePath",
                table: "DocumentChunks");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ImagePath",
                table: "DocumentChunks",
                type: "text",
                nullable: true);
        }
    }
}
