using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RealEstateAdmin.Migrations
{
    /// <inheritdoc />
    public partial class AddSalesSuperAdminAndWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TABLE `Messages` ADD COLUMN IF NOT EXISTS `Destinataire` varchar(50) CHARACTER SET utf8mb4 NOT NULL DEFAULT 'Administration';");

            migrationBuilder.Sql(
                "ALTER TABLE `Biens` ADD COLUMN IF NOT EXISTS `StatutCommercial` varchar(50) CHARACTER SET utf8mb4 NOT NULL DEFAULT 'Disponible';");

            migrationBuilder.Sql("SET FOREIGN_KEY_CHECKS=0;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS `Sales`;");
            migrationBuilder.Sql("SET FOREIGN_KEY_CHECKS=1;");

            migrationBuilder.Sql(
                @"CREATE TABLE `Sales` (
                    `Id` int NOT NULL AUTO_INCREMENT,
                    `BienImmobilierId` int NOT NULL,
                    `BuyerId` varchar(450) CHARACTER SET utf8mb4 NULL,
                    `SellerId` varchar(450) CHARACTER SET utf8mb4 NULL,
                    `Amount` decimal(18,2) NOT NULL,
                    `PaymentMethod` varchar(50) CHARACTER SET utf8mb4 NOT NULL DEFAULT 'Virement',
                    `PaymentStatus` varchar(50) CHARACTER SET utf8mb4 NOT NULL DEFAULT 'En attente',
                    `TransactionStatus` varchar(50) CHARACTER SET utf8mb4 NOT NULL DEFAULT 'Finalisée',
                    `CreatedAt` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
                    `PaidAt` datetime(6) NULL,
                    `Notes` varchar(1000) CHARACTER SET utf8mb4 NULL,
                    CONSTRAINT `PK_Sales` PRIMARY KEY (`Id`),
                    CONSTRAINT `FK_Sales_AspNetUsers_BuyerId` FOREIGN KEY (`BuyerId`) REFERENCES `AspNetUsers` (`Id`) ON DELETE SET NULL,
                    CONSTRAINT `FK_Sales_AspNetUsers_SellerId` FOREIGN KEY (`SellerId`) REFERENCES `AspNetUsers` (`Id`) ON DELETE SET NULL,
                    CONSTRAINT `FK_Sales_Biens_BienImmobilierId` FOREIGN KEY (`BienImmobilierId`) REFERENCES `Biens` (`Id`) ON DELETE CASCADE
                ) CHARACTER SET=utf8mb4;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TABLE IF EXISTS `Sales`;");
            migrationBuilder.Sql("ALTER TABLE `Messages` DROP COLUMN IF EXISTS `Destinataire`;");
            migrationBuilder.Sql("ALTER TABLE `Biens` DROP COLUMN IF EXISTS `StatutCommercial`;");
        }
    }
}
