using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DocuChat.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddClaimsStamp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ClaimsStamp",
                table: "AspNetUsers",
                type: "text",
                nullable: false,
                defaultValue: "");

            // Mevcut kullanıcılara benzersiz başlangıç damgası ver (hepsi "" paylaşmasın).
            // gen_random_uuid() PostgreSQL 13+ ile yerleşik (pg17 imajında mevcut).
            migrationBuilder.Sql(
                @"UPDATE ""AspNetUsers"" SET ""ClaimsStamp"" = replace(gen_random_uuid()::text, '-', '') WHERE ""ClaimsStamp"" = '';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ClaimsStamp",
                table: "AspNetUsers");
        }
    }
}
