using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RealEstateAdmin.Migrations
{
    /// <inheritdoc />
    public partial class AddContratAndVersements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "StatutPaiementDetaille",
                table: "Sales",
                type: "varchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "En attente")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AlterColumn<int>(
                name: "DiscountPercent",
                table: "Biens",
                type: "int",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(65,30)");

            migrationBuilder.CreateTable(
                name: "Contrats",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    SaleTransactionId = table.Column<int>(type: "int", nullable: false),
                    NumeroContrat = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DateSignature = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    ContractStatus = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false, defaultValue: "Brouillon")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    NomAcheteur = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    NomVendeur = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    NomAgent = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    TitreBien = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    AdresseBien = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SurfaceBien = table.Column<int>(type: "int", nullable: true),
                    PrixContrat = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ConditionsPaiement = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DateCreation = table.Column<DateTime>(type: "datetime(6)", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP(6)"),
                    ExecutePar = table.Column<string>(type: "varchar(450)", maxLength: 450, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Contrats", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Contrats_AspNetUsers_ExecutePar",
                        column: x => x.ExecutePar,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Contrats_Sales_SaleTransactionId",
                        column: x => x.SaleTransactionId,
                        principalTable: "Sales",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Versements",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    SaleTransactionId = table.Column<int>(type: "int", nullable: false),
                    Montant = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    DateVersement = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModePaiement = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false, defaultValue: "Virement")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Note = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP(6)"),
                    AjoutePar = table.Column<string>(type: "varchar(450)", maxLength: 450, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Versements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Versements_AspNetUsers_AjoutePar",
                        column: x => x.AjoutePar,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Versements_Sales_SaleTransactionId",
                        column: x => x.SaleTransactionId,
                        principalTable: "Sales",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_Contrats_ExecutePar",
                table: "Contrats",
                column: "ExecutePar");

            migrationBuilder.CreateIndex(
                name: "IX_Contrats_SaleTransactionId",
                table: "Contrats",
                column: "SaleTransactionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Versements_AjoutePar",
                table: "Versements",
                column: "AjoutePar");

            migrationBuilder.CreateIndex(
                name: "IX_Versements_SaleTransactionId",
                table: "Versements",
                column: "SaleTransactionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Contrats");

            migrationBuilder.DropTable(
                name: "Versements");

            migrationBuilder.DropColumn(
                name: "StatutPaiementDetaille",
                table: "Sales");

            migrationBuilder.AlterColumn<decimal>(
                name: "DiscountPercent",
                table: "Biens",
                type: "decimal(65,30)",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int");
        }
    }
}
