using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RealEstateAdmin.Migrations.ApplicationDb
{
    /// <inheritdoc />
    public partial class AddNatureBienEtatBien : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EtatBien",
                table: "Biens",
                type: "varchar(50)",
                maxLength: 50,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "NatureBien",
                table: "Biens",
                type: "varchar(50)",
                maxLength: 50,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "EtatBien",
                table: "Annonces",
                type: "varchar(50)",
                maxLength: 50,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "NatureBien",
                table: "Annonces",
                type: "varchar(50)",
                maxLength: 50,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EtatBien",
                table: "Biens");

            migrationBuilder.DropColumn(
                name: "NatureBien",
                table: "Biens");

            migrationBuilder.DropColumn(
                name: "EtatBien",
                table: "Annonces");

            migrationBuilder.DropColumn(
                name: "NatureBien",
                table: "Annonces");
        }
    }
}
