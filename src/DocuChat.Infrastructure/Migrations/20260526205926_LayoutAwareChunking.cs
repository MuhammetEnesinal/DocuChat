using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DocuChat.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class LayoutAwareChunking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CleanContent",
                table: "DocumentChunks",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContentHash",
                table: "DocumentChunks",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "NextChunkId",
                table: "DocumentChunks",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PrevChunkId",
                table: "DocumentChunks",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StructuredTableJson",
                table: "DocumentChunks",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TokenCount",
                table: "DocumentChunks",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_DocumentChunks_ContentHash",
                table: "DocumentChunks",
                column: "ContentHash");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentChunks_DocumentId_PageNumber",
                table: "DocumentChunks",
                columns: new[] { "DocumentId", "PageNumber" });

            // TsVector: önceki sürüm Content/Header/Summary üzerine kuruluydu.
            // Yeni: CleanContent öncelikli (markdown gürültüsü yok), fallback Content.
            migrationBuilder.Sql(@"
                DROP INDEX IF EXISTS idx_chunks_tsvector;
                ALTER TABLE ""DocumentChunks"" DROP COLUMN IF EXISTS ""TsVector"";

                ALTER TABLE ""DocumentChunks""
                ADD COLUMN ""TsVector"" tsvector
                GENERATED ALWAYS AS (
                  setweight(to_tsvector('turkish', coalesce(""CleanContent"", ""Content"", '')), 'A') ||
                  setweight(to_tsvector('turkish', coalesce(""Header"", '')), 'B') ||
                  setweight(to_tsvector('turkish', coalesce(""Summary"", '')), 'C')
                ) STORED;

                CREATE INDEX idx_chunks_tsvector ON ""DocumentChunks"" USING GIN (""TsVector"");

                -- JSONB GIN index (structured table queries için)
                CREATE INDEX IF NOT EXISTS idx_chunks_structured_table
                ON ""DocumentChunks"" USING GIN (""StructuredTableJson"" jsonb_path_ops);
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DROP INDEX IF EXISTS idx_chunks_structured_table;
                DROP INDEX IF EXISTS idx_chunks_tsvector;
                ALTER TABLE ""DocumentChunks"" DROP COLUMN IF EXISTS ""TsVector"";

                -- Eski TsVector (Content fallback) geri yükle
                ALTER TABLE ""DocumentChunks""
                ADD COLUMN ""TsVector"" tsvector
                GENERATED ALWAYS AS (
                  setweight(to_tsvector('turkish', coalesce(""Content"", '')), 'A') ||
                  setweight(to_tsvector('turkish', coalesce(""Header"", '')), 'B') ||
                  setweight(to_tsvector('turkish', coalesce(""Summary"", '')), 'C')
                ) STORED;
                CREATE INDEX idx_chunks_tsvector ON ""DocumentChunks"" USING GIN (""TsVector"");
            ");

            migrationBuilder.DropIndex(
                name: "IX_DocumentChunks_ContentHash",
                table: "DocumentChunks");

            migrationBuilder.DropIndex(
                name: "IX_DocumentChunks_DocumentId_PageNumber",
                table: "DocumentChunks");

            migrationBuilder.DropColumn(
                name: "CleanContent",
                table: "DocumentChunks");

            migrationBuilder.DropColumn(
                name: "ContentHash",
                table: "DocumentChunks");

            migrationBuilder.DropColumn(
                name: "NextChunkId",
                table: "DocumentChunks");

            migrationBuilder.DropColumn(
                name: "PrevChunkId",
                table: "DocumentChunks");

            migrationBuilder.DropColumn(
                name: "StructuredTableJson",
                table: "DocumentChunks");

            migrationBuilder.DropColumn(
                name: "TokenCount",
                table: "DocumentChunks");
        }
    }
}
