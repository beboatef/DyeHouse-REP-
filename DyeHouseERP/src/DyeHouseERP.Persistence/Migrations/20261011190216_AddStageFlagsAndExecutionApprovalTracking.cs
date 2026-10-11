using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DyeHouseERP.Persistence.Migrations
{
    /// <summary>
    /// Restores the production-stage policy flags and the stage-execution approval
    /// tracking that the application code requires, WITHOUT touching the machine /
    /// production-shift linkage added by AddDynamicMachinesAndExecutionShifts.
    ///
    /// Purely additive: every column is added with a non-null default, so no table is
    /// dropped, no monetary precision or string length is reduced, no index or check
    /// constraint is weakened, and the historical MachineId / ProductionShiftId
    /// references on ProductionOrderStageExecutions stay exactly as they were.
    /// </summary>
    /// <inheritdoc />
    public partial class AddStageFlagsAndExecutionApprovalTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AllowRepeat",
                table: "ProductionStageDefinitions",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "AllowReturn",
                table: "ProductionStageDefinitions",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "AllowRework",
                table: "ProductionStageDefinitions",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "AllowSkip",
                table: "ProductionStageDefinitions",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "RequiresApproval",
                table: "ProductionStageDefinitions",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "ApprovalRequired",
                table: "ProductionOrderStageExecutions",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "ApprovedAtUtc",
                table: "ProductionOrderStageExecutions",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ApprovedBy",
                table: "ProductionOrderStageExecutions",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AllowRepeat",
                table: "ProductionStageDefinitions");

            migrationBuilder.DropColumn(
                name: "AllowReturn",
                table: "ProductionStageDefinitions");

            migrationBuilder.DropColumn(
                name: "AllowRework",
                table: "ProductionStageDefinitions");

            migrationBuilder.DropColumn(
                name: "AllowSkip",
                table: "ProductionStageDefinitions");

            migrationBuilder.DropColumn(
                name: "RequiresApproval",
                table: "ProductionStageDefinitions");

            migrationBuilder.DropColumn(
                name: "ApprovalRequired",
                table: "ProductionOrderStageExecutions");

            migrationBuilder.DropColumn(
                name: "ApprovedAtUtc",
                table: "ProductionOrderStageExecutions");

            migrationBuilder.DropColumn(
                name: "ApprovedBy",
                table: "ProductionOrderStageExecutions");
        }
    }
}
