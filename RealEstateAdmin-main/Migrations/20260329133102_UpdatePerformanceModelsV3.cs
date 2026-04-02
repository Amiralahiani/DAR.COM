using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RealEstateAdmin.Migrations
{
    /// <inheritdoc />
    public partial class UpdatePerformanceModelsV3 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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
                name: "ConversionScore",
                table: "AgentPerformances");

            migrationBuilder.DropColumn(
                name: "FeedbackScore",
                table: "AgentPerformances");

            migrationBuilder.DropColumn(
                name: "PunctualityScore",
                table: "AgentPerformances");

            migrationBuilder.AddColumn<int>(
                name: "NbVisites",
                table: "Sales",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "NoteClient",
                table: "Sales",
                type: "decimal(3,2)",
                precision: 3,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BiensVendus",
                table: "AgentPerformances",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<double>(
                name: "DelaiMoyenVente",
                table: "AgentPerformances",
                type: "double",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "SatisfactionClient",
                table: "AgentPerformances",
                type: "double",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "ScoreGlobal",
                table: "AgentPerformances",
                type: "double",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "TauxConversion",
                table: "AgentPerformances",
                type: "double",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "TauxPaiementComplet",
                table: "AgentPerformances",
                type: "double",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<int>(
                name: "TotalVisites",
                table: "AgentPerformances",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "ValeurTotaleVendue",
                table: "AgentPerformances",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NbVisites",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "NoteClient",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "BiensVendus",
                table: "AgentPerformances");

            migrationBuilder.DropColumn(
                name: "DelaiMoyenVente",
                table: "AgentPerformances");

            migrationBuilder.DropColumn(
                name: "SatisfactionClient",
                table: "AgentPerformances");

            migrationBuilder.DropColumn(
                name: "ScoreGlobal",
                table: "AgentPerformances");

            migrationBuilder.DropColumn(
                name: "TauxConversion",
                table: "AgentPerformances");

            migrationBuilder.DropColumn(
                name: "TauxPaiementComplet",
                table: "AgentPerformances");

            migrationBuilder.DropColumn(
                name: "TotalVisites",
                table: "AgentPerformances");

            migrationBuilder.DropColumn(
                name: "ValeurTotaleVendue",
                table: "AgentPerformances");

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

            migrationBuilder.AddColumn<double>(
                name: "ConversionScore",
                table: "AgentPerformances",
                type: "double",
                precision: 5,
                scale: 2,
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "FeedbackScore",
                table: "AgentPerformances",
                type: "double",
                precision: 5,
                scale: 2,
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "PunctualityScore",
                table: "AgentPerformances",
                type: "double",
                precision: 5,
                scale: 2,
                nullable: false,
                defaultValue: 0.0);
        }
    }
}
