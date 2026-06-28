using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DocuChat.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DropDocumentChunkSummary : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Summary kolonu kullanılmıyor (captionSummary zaten embedText'in içinde).
            // PostgreSQL GENERATED tsvector kolonu Summary'ye bağımlı → önce
            // index + tsvector'ü düşür, Summary'i drop et, tsvector'ü Summary'siz yeniden oluştur.

            migrationBuilder.Sql(@"DROP INDEX IF EXISTS idx_chunks_tsvector;");
            migrationBuilder.Sql(@"ALTER TABLE ""DocumentChunks"" DROP COLUMN IF EXISTS ""TsVector"";");

            migrationBuilder.DropColumn(
                name: "Summary",
                table: "DocumentChunks");

            // TsVector'ü Summary'siz yeniden oluştur (Content ağırlık A, Header ağırlık B).
            migrationBuilder.Sql(@"
                ALTER TABLE ""DocumentChunks""
                ADD COLUMN ""TsVector"" tsvector
                GENERATED ALWAYS AS (
                  setweight(to_tsvector('turkish', coalesce(""Content"", '')), 'A') ||
                  setweight(to_tsvector('turkish', coalesce(""Header"", '')), 'B')
                ) STORED;
            ");

            migrationBuilder.Sql(@"
                CREATE INDEX IF NOT EXISTS idx_chunks_tsvector
                ON ""DocumentChunks"" USING GIN (""TsVector"");
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Tersine: tsvector + index düşür, Summary'yi geri ekle, tsvector'ü Summary'li yeniden oluştur.
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS idx_chunks_tsvector;");
            migrationBuilder.Sql(@"ALTER TABLE ""DocumentChunks"" DROP COLUMN IF EXISTS ""TsVector"";");

            migrationBuilder.AddColumn<string>(
                name: "Summary",
                table: "DocumentChunks",
                type: "text",
                nullable: true);

            migrationBuilder.Sql(@"
                ALTER TABLE ""DocumentChunks""
                ADD COLUMN ""TsVector"" tsvector
                GENERATED ALWAYS AS (
                  setweight(to_tsvector('turkish', coalesce(""Content"", '')), 'A') ||
                  setweight(to_tsvector('turkish', coalesce(""Header"", '')), 'B') ||
                  setweight(to_tsvector('turkish', coalesce(""Summary"", '')), 'B')
                ) STORED;
            ");

            migrationBuilder.Sql(@"
                CREATE INDEX IF NOT EXISTS idx_chunks_tsvector
                ON ""DocumentChunks"" USING GIN (""TsVector"");
            ");
        }
    }
}
