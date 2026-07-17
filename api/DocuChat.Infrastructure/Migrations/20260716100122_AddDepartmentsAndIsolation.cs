using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DocuChat.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDepartmentsAndIsolation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // NOT NULL, DB-level default YOK — veritabanı sıfırdan kurulduğu için mevcut satır
            // yok; her yeni belge DepartmentId'sini kodda açıkça set eder (kesin izolasyon).
            migrationBuilder.AddColumn<Guid>(
                name: "DepartmentId",
                table: "Documents",
                type: "uuid",
                nullable: false);

            migrationBuilder.CreateTable(
                name: "Departments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Departments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UserDepartments",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "text", nullable: false),
                    DepartmentId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserDepartments", x => new { x.UserId, x.DepartmentId });
                    table.ForeignKey(
                        name: "FK_UserDepartments_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserDepartments_Departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalTable: "Departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Documents_DepartmentId",
                table: "Documents",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_Departments_Name",
                table: "Departments",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserDepartments_DepartmentId",
                table: "UserDepartments",
                column: "DepartmentId");

            migrationBuilder.AddForeignKey(
                name: "FK_Documents_Departments_DepartmentId",
                table: "Documents",
                column: "DepartmentId",
                principalTable: "Departments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Documents_Departments_DepartmentId",
                table: "Documents");

            migrationBuilder.DropTable(
                name: "UserDepartments");

            migrationBuilder.DropTable(
                name: "Departments");

            migrationBuilder.DropIndex(
                name: "IX_Documents_DepartmentId",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "DepartmentId",
                table: "Documents");
        }
    }
}
