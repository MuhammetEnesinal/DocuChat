using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DocuChat.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddChunkSummaryPageNumberTsVector : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PageNumber",
                table: "DocumentChunks",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Summary",
                table: "DocumentChunks",
                type: "text",
                nullable: true);

            // TsVector generated stored column (Türkçe tam metin araması (FTS) retrieval için)
            // Content (A), Header/Summary (B) ağırlıklı setweight
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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS idx_chunks_tsvector;");
            migrationBuilder.Sql(@"ALTER TABLE ""DocumentChunks"" DROP COLUMN IF EXISTS ""TsVector"";");

            migrationBuilder.DropColumn(
                name: "PageNumber",
                table: "DocumentChunks");

            migrationBuilder.DropColumn(
                name: "Summary",
                table: "DocumentChunks");
        }
    }
}
