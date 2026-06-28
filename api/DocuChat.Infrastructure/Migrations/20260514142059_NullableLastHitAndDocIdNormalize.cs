using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DocuChat.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class NullableLastHitAndDocIdNormalize : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<DateTime>(
                name: "LastHitAt",
                table: "QuestionCaches",
                type: "timestamp with time zone",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");

            migrationBuilder.CreateIndex(
                name: "IX_QuestionCaches_CreatedAt",
                table: "QuestionCaches",
                column: "CreatedAt");

            // Hit almamış satırların LastHitAt'ı gerçekten boşmuş gibi davransın —
            // eski default DateTime.UtcNow'lar TTL'i bozuyordu.
            migrationBuilder.Sql(
                @"UPDATE ""QuestionCaches"" SET ""LastHitAt"" = NULL WHERE ""HitCount"" = 0;");

            // DocumentIds artık ",<guid>,<guid>," formatında — ClearByDocumentId
            // guard'lı Contains kullanabilsin. Boş/null olmayan ve henüz çevre virgüllü olmayanları normalize et.
            migrationBuilder.Sql(
                @"UPDATE ""QuestionCaches""
                  SET ""DocumentIds"" = ',' || ""DocumentIds"" || ','
                  WHERE ""DocumentIds"" IS NOT NULL
                    AND ""DocumentIds"" <> ''
                    AND ""DocumentIds"" NOT LIKE ',%';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // DocumentIds formatını geri al (çevre virgülleri kaldır).
            migrationBuilder.Sql(
                @"UPDATE ""QuestionCaches""
                  SET ""DocumentIds"" = TRIM(BOTH ',' FROM ""DocumentIds"")
                  WHERE ""DocumentIds"" LIKE ',%';");

            // Null LastHitAt'ları sütun nullable olmadan önce doldur — UtcNow ile.
            migrationBuilder.Sql(
                @"UPDATE ""QuestionCaches"" SET ""LastHitAt"" = NOW() WHERE ""LastHitAt"" IS NULL;");

            migrationBuilder.DropIndex(
                name: "IX_QuestionCaches_CreatedAt",
                table: "QuestionCaches");

            migrationBuilder.AlterColumn<DateTime>(
                name: "LastHitAt",
                table: "QuestionCaches",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified),
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldNullable: true);
        }
    }
}
