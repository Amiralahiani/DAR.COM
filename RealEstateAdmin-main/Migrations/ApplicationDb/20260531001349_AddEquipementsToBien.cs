using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RealEstateAdmin.Migrations.ApplicationDb
{
    /// <inheritdoc />
    public partial class AddEquipementsToBien : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "HasAscenseur",
                table: "Biens",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "HasBalcon",
                table: "Biens",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "HasChauffageCentral",
                table: "Biens",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "HasClimatisation",
                table: "Biens",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "HasGarage",
                table: "Biens",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "HasJardin",
                table: "Biens",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "HasParking",
                table: "Biens",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "HasPiscine",
                table: "Biens",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "HasTerrasse",
                table: "Biens",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HasAscenseur",
                table: "Biens");

            migrationBuilder.DropColumn(
                name: "HasBalcon",
                table: "Biens");

            migrationBuilder.DropColumn(
                name: "HasChauffageCentral",
                table: "Biens");

            migrationBuilder.DropColumn(
                name: "HasClimatisation",
                table: "Biens");

            migrationBuilder.DropColumn(
                name: "HasGarage",
                table: "Biens");

            migrationBuilder.DropColumn(
                name: "HasJardin",
                table: "Biens");

            migrationBuilder.DropColumn(
                name: "HasParking",
                table: "Biens");

            migrationBuilder.DropColumn(
                name: "HasPiscine",
                table: "Biens");

            migrationBuilder.DropColumn(
                name: "HasTerrasse",
                table: "Biens");
        }
    }
}
