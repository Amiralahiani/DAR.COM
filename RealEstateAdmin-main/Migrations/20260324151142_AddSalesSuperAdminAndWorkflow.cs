using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RealEstateAdmin.Migrations
{
    public partial class AddSalesSuperAdminAndWorkflow : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Destinataire",
                table: "Messages",
                type: "varchar(50)",
                nullable: false,
                defaultValue: "Administration");

            migrationBuilder.AddColumn<string>(
                name: "StatutCommercial",
                table: "Biens",
                type: "varchar(50)",
                nullable: false,
                defaultValue: "Disponible");

            migrationBuilder.Sql("SET FOREIGN_KEY_CHECKS=0;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS `Sales`;");
            migrationBuilder.Sql("SET FOREIGN_KEY_CHECKS=1;");

            migrationBuilder.CreateTable(
                name: "Sales",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),

                    BienImmobilierId = table.Column<int>(nullable: false),

                    BuyerId = table.Column<string>(nullable: true),
                    SellerId = table.Column<string>(nullable: true),

                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),

                    PaymentMethod = table.Column<string>(type: "varchar(50)", nullable: false, defaultValue: "Virement"),
                    PaymentStatus = table.Column<string>(type: "varchar(50)", nullable: false, defaultValue: "En attente"),
                    TransactionStatus = table.Column<string>(type: "varchar(50)", nullable: false, defaultValue: "Finalisée"),

                    // ✅ CORRECTION ICI: set DB default to current timestamp for created date
                    CreatedAt = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),

                    PaidAt = table.Column<DateTime>(nullable: true),
                    Notes = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sales", x => x.Id);

                    table.ForeignKey(
                        name: "FK_Sales_AspNetUsers_BuyerId",
                        column: x => x.BuyerId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);

                    table.ForeignKey(
                        name: "FK_Sales_AspNetUsers_SellerId",
                        column: x => x.SellerId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);

                    table.ForeignKey(
                        name: "FK_Sales_Biens_BienImmobilierId",
                        column: x => x.BienImmobilierId,
                        principalTable: "Biens",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TABLE IF EXISTS `Sales`;");
            migrationBuilder.Sql("ALTER TABLE `Messages` DROP COLUMN IF EXISTS `Destinataire`;");
            migrationBuilder.Sql("ALTER TABLE `Biens` DROP COLUMN IF EXISTS `StatutCommercial`;");
        }
    }
}