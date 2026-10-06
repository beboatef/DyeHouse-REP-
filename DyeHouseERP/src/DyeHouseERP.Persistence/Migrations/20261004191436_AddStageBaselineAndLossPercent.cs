using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DyeHouseERP.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStageBaselineAndLossPercent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "BaselineKg",
                table: "ProductionOrderStageExecutions",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "BaselineMeter",
                table: "ProductionOrderStageExecutions",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "LossPercentKg",
                table: "ProductionOrderStageExecutions",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "LossPercentMeter",
                table: "ProductionOrderStageExecutions",
                type: "decimal(18,2)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BaselineKg",
                table: "ProductionOrderStageExecutions");

            migrationBuilder.DropColumn(
                name: "BaselineMeter",
                table: "ProductionOrderStageExecutions");

            migrationBuilder.DropColumn(
                name: "LossPercentKg",
                table: "ProductionOrderStageExecutions");

            migrationBuilder.DropColumn(
                name: "LossPercentMeter",
                table: "ProductionOrderStageExecutions");
        }
    }
}
