using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DocuChat.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDepartmentCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Kolon önce NULLABLE eklenir. Tabloda mevcut departmanlar var; NOT NULL + unique index
            // doğrudan eklenseydi hepsi "" olur ve unique index PATLARDI.
            migrationBuilder.AddColumn<string>(
                name: "Code",
                table: "Departments",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            // Mevcut departmanlara addan otomatik kod: boşluklar atılır, büyük harfe çevrilir,
            // 20 karaktere kırpılır. (PostgreSQL upper() UTF-8'de 'Yazılım' → 'YAZILIM' verir.)
            migrationBuilder.Sql(@"UPDATE ""Departments"" SET ""Code"" = left(upper(replace(""Name"", ' ', '')), 20);");
            // Ad sadece boşluktan ibaretse kod boş kalır — güvenli bir varsayılana çek.
            migrationBuilder.Sql(@"UPDATE ""Departments"" SET ""Code"" = 'DEPT' WHERE ""Code"" IS NULL OR ""Code"" = '';");

            // Çakışan kodlara sıra numarası eklenir (ABC, ABC2, ABC3...) → unique index güvenle kurulur.
            migrationBuilder.Sql(@"
                WITH d AS (
                    SELECT ""Id"", ""Code"",
                           row_number() OVER (PARTITION BY ""Code"" ORDER BY ""CreatedAt"", ""Id"") AS rn
                    FROM ""Departments""
                )
                UPDATE ""Departments"" x
                SET ""Code"" = left(d.""Code"", 18) || d.rn::text
                FROM d
                WHERE x.""Id"" = d.""Id"" AND d.rn > 1;");

            // Backfill tamam → artık NOT NULL yapılabilir.
            // DİKKAT: AlterColumn(nullable:false) burada YETMİYOR — oldNullable belirtilmezse EF
            // kolonu zaten NOT NULL sanıp yalnız "ALTER COLUMN ... TYPE" üretiyor, SET NOT NULL'ı
            // atlıyor (yaşandı). Bu yüzden açık SQL.
            migrationBuilder.Sql(@"ALTER TABLE ""Departments"" ALTER COLUMN ""Code"" SET NOT NULL;");

            migrationBuilder.CreateIndex(
                name: "IX_Departments_Code",
                table: "Departments",
                column: "Code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Departments_Code",
                table: "Departments");

            migrationBuilder.DropColumn(
                name: "Code",
                table: "Departments");
        }
    }
}
