using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RealEstateAdmin.Migrations
{
    /// <inheritdoc />
    public partial class AddAnnonce : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Annonces",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    UserId = table.Column<string>(type: "varchar(450)", maxLength: 450, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Gouvernorat = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Delegation = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SurfaceM2 = table.Column<int>(type: "int", nullable: false),
                    NbChambres = table.Column<int>(type: "int", nullable: false),
                    HasAscenseur = table.Column<bool>(type: "tinyint(1)", nullable: false, defaultValue: false),
                    HasBalcon = table.Column<bool>(type: "tinyint(1)", nullable: false, defaultValue: false),
                    HasChauffageCentral = table.Column<bool>(type: "tinyint(1)", nullable: false, defaultValue: false),
                    HasClimatisation = table.Column<bool>(type: "tinyint(1)", nullable: false, defaultValue: false),
                    HasGarage = table.Column<bool>(type: "tinyint(1)", nullable: false, defaultValue: false),
                    HasJardin = table.Column<bool>(type: "tinyint(1)", nullable: false, defaultValue: false),
                    HasParking = table.Column<bool>(type: "tinyint(1)", nullable: false, defaultValue: false),
                    HasPiscine = table.Column<bool>(type: "tinyint(1)", nullable: false, defaultValue: false),
                    HasTerrasse = table.Column<bool>(type: "tinyint(1)", nullable: false, defaultValue: false),
                    Description = table.Column<string>(type: "varchar(5000)", maxLength: 5000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PrixTnd = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP(6)"),
                    Statut = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false, defaultValue: "En attente")
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Annonces", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "AnnoncePhotos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    AnnonceId = table.Column<int>(type: "int", nullable: false),
                    Url = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnnoncePhotos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AnnoncePhotos_Annonces_AnnonceId",
                        column: x => x.AnnonceId,
                        principalTable: "Annonces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_AnnoncePhotos_AnnonceId",
                table: "AnnoncePhotos",
                column: "AnnonceId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "AnnoncePhotos");
            migrationBuilder.DropTable(name: "Annonces");
        }
    }
}
