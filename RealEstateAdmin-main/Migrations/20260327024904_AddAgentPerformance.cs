using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RealEstateAdmin.Migrations
{
    /// <inheritdoc />
    public partial class AddAgentPerformance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AgentPerformances",
                columns: table => new
                {
                    AgentId = table.Column<string>(type: "varchar(450)", maxLength: 450, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PunctualityScore = table.Column<double>(type: "double", precision: 5, scale: 2, nullable: false),
                    FeedbackScore = table.Column<double>(type: "double", precision: 5, scale: 2, nullable: false),
                    ConversionScore = table.Column<double>(type: "double", precision: 5, scale: 2, nullable: false),
                    LastComputed = table.Column<DateTime>(type: "datetime(6)", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP(6)")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentPerformances", x => x.AgentId);
                })
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AgentPerformances");
        }
    }
}
