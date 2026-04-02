using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RealEstateAdmin.Migrations.ApplicationIdentityDb
{
    /// <inheritdoc />
    public partial class AddDateInscriptionToIdentityV3 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DateInscription",
                table: "AspNetUsers",
                type: "datetime(6)",
                nullable: false,
                defaultValueSql: "CURRENT_TIMESTAMP(6)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DateInscription",
                table: "AspNetUsers");
        }
    }
}
