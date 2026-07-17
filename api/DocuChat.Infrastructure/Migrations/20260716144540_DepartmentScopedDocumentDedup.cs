using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DocuChat.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DepartmentScopedDocumentDedup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Documents_UserId_ContentHash",
                table: "Documents");

            migrationBuilder.DropIndex(
                name: "IX_Documents_UserId_FileName",
                table: "Documents");

            migrationBuilder.CreateIndex(
                name: "IX_Documents_DepartmentId_ContentHash",
                table: "Documents",
                columns: new[] { "DepartmentId", "ContentHash" });

            migrationBuilder.CreateIndex(
                name: "IX_Documents_DepartmentId_FileName",
                table: "Documents",
                columns: new[] { "DepartmentId", "FileName" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Documents_DepartmentId_ContentHash",
                table: "Documents");

            migrationBuilder.DropIndex(
                name: "IX_Documents_DepartmentId_FileName",
                table: "Documents");

            migrationBuilder.CreateIndex(
                name: "IX_Documents_UserId_ContentHash",
                table: "Documents",
                columns: new[] { "UserId", "ContentHash" });

            migrationBuilder.CreateIndex(
                name: "IX_Documents_UserId_FileName",
                table: "Documents",
                columns: new[] { "UserId", "FileName" },
                unique: true);
        }
    }
}
