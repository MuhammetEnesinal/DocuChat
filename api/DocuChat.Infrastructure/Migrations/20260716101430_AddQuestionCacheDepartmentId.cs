using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DocuChat.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddQuestionCacheDepartmentId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "DepartmentId",
                table: "QuestionCaches",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_QuestionCaches_DepartmentId",
                table: "QuestionCaches",
                column: "DepartmentId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_QuestionCaches_DepartmentId",
                table: "QuestionCaches");

            migrationBuilder.DropColumn(
                name: "DepartmentId",
                table: "QuestionCaches");
        }
    }
}
