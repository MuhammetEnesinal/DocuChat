using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DocuChat.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DropQuestionCacheDocColumns : Migration
    {
        // NOT: TsVector (DocumentChunks) generated kolonu manuel SQL ile yönetiliyor ve
        // ModelSnapshot'ta yok; EF onu eklemeye çalışıyordu — kasıtlı olarak çıkarıldı.
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DocumentContentHashes",
                table: "QuestionCaches");

            migrationBuilder.DropColumn(
                name: "DocumentIds",
                table: "QuestionCaches");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DocumentContentHashes",
                table: "QuestionCaches",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DocumentIds",
                table: "QuestionCaches",
                type: "text",
                nullable: true);
        }
    }
}
