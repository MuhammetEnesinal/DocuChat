using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Pgvector;

#nullable disable

namespace DocuChat.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentImagesAndJoin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DocumentImages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Path = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    Caption = table.Column<string>(type: "text", nullable: true),
                    PageNumber = table.Column<int>(type: "integer", nullable: true),
                    ContentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ImageType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Embedding = table.Column<Vector>(type: "vector(512)", nullable: true),
                    Source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentImages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DocumentImages_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ChunkImages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ChunkId = table.Column<Guid>(type: "uuid", nullable: false),
                    ImageId = table.Column<Guid>(type: "uuid", nullable: false),
                    PositionInChunk = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChunkImages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChunkImages_DocumentChunks_ChunkId",
                        column: x => x.ChunkId,
                        principalTable: "DocumentChunks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ChunkImages_DocumentImages_ImageId",
                        column: x => x.ImageId,
                        principalTable: "DocumentImages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChunkImages_ChunkId",
                table: "ChunkImages",
                column: "ChunkId");

            migrationBuilder.CreateIndex(
                name: "IX_ChunkImages_ChunkId_ImageId_PositionInChunk",
                table: "ChunkImages",
                columns: new[] { "ChunkId", "ImageId", "PositionInChunk" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChunkImages_ImageId",
                table: "ChunkImages",
                column: "ImageId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentImages_ContentHash",
                table: "DocumentImages",
                column: "ContentHash");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentImages_DocumentId",
                table: "DocumentImages",
                column: "DocumentId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChunkImages");

            migrationBuilder.DropTable(
                name: "DocumentImages");
        }
    }
}
