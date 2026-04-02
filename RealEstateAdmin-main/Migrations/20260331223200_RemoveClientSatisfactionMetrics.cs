using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RealEstateAdmin.Migrations
{
    /// <inheritdoc />
    public partial class RemoveClientSatisfactionMetrics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NoteClient",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "SatisfactionClient",
                table: "AgentPerformances");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "NoteClient",
                table: "Sales",
                type: "decimal(3,2)",
                precision: 3,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "SatisfactionClient",
                table: "AgentPerformances",
                type: "double",
                nullable: false,
                defaultValue: 0.0);
        }
    }
}
