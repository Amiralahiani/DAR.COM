using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RealEstateAdmin.Migrations
{
    /// <inheritdoc />
    public partial class AddUserIdToBienImmobilier : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "UserId",
                table: "Biens",
                type: "varchar(450)",
                maxLength: 450,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_Biens_UserId",
                table: "Biens",
                column: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Biens_AspNetUsers_UserId",
                table: "Biens",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Biens_AspNetUsers_UserId",
                table: "Biens");

            migrationBuilder.DropIndex(
                name: "IX_Biens_UserId",
                table: "Biens");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "Biens");
        }
    }
}
