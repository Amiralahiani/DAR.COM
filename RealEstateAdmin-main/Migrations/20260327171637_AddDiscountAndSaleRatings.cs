using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RealEstateAdmin.Migrations
{
    /// <inheritdoc />
    public partial class AddDiscountAndSaleRatings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AgentId",
                table: "Sales",
                type: "varchar(450)",
                maxLength: 450,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<decimal>(
                name: "ConversionRating",
                table: "Sales",
                type: "decimal(65,30)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "FeedbackRating",
                table: "Sales",
                type: "decimal(65,30)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PunctualityRating",
                table: "Sales",
                type: "decimal(65,30)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DiscountPercent",
                table: "Biens",
                type: "decimal(65,30)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateIndex(
                name: "IX_Sales_AgentId",
                table: "Sales",
                column: "AgentId");

            migrationBuilder.AddForeignKey(
                name: "FK_Sales_AspNetUsers_AgentId",
                table: "Sales",
                column: "AgentId",
                principalTable: "AspNetUsers",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Sales_AspNetUsers_AgentId",
                table: "Sales");

            migrationBuilder.DropIndex(
                name: "IX_Sales_AgentId",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "AgentId",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "ConversionRating",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "FeedbackRating",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "PunctualityRating",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "DiscountPercent",
                table: "Biens");
        }
    }
}
