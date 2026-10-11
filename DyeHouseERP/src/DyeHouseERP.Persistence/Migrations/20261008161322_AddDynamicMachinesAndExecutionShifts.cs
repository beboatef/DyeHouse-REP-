using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DyeHouseERP.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDynamicMachinesAndExecutionShifts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
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

            migrationBuilder.AddColumn<Guid>(
                name: "MachineId",
                table: "ProductionOrderStageExecutions",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProductionShiftId",
                table: "ProductionOrderStageExecutions",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Machines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    IsLocked = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Machines", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProductionShifts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    IsLocked = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductionShifts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StageMachines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StageDefinitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MachineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StageMachines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StageMachines_Machines_MachineId",
                        column: x => x.MachineId,
                        principalTable: "Machines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_StageMachines_ProductionStageDefinitions_StageDefinitionId",
                        column: x => x.StageDefinitionId,
                        principalTable: "ProductionStageDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProductionOrderStageExecutions_MachineId",
                table: "ProductionOrderStageExecutions",
                column: "MachineId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionOrderStageExecutions_ProductionShiftId",
                table: "ProductionOrderStageExecutions",
                column: "ProductionShiftId");

            migrationBuilder.CreateIndex(
                name: "IX_Machines_Code",
                table: "Machines",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Machines_IsActive",
                table: "Machines",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionShifts_Code",
                table: "ProductionShifts",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductionShifts_Sequence",
                table: "ProductionShifts",
                column: "Sequence");

            migrationBuilder.CreateIndex(
                name: "IX_StageMachines_MachineId",
                table: "StageMachines",
                column: "MachineId");

            migrationBuilder.CreateIndex(
                name: "IX_StageMachines_StageDefinitionId_MachineId",
                table: "StageMachines",
                columns: new[] { "StageDefinitionId", "MachineId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_ProductionOrderStageExecutions_Machines_MachineId",
                table: "ProductionOrderStageExecutions",
                column: "MachineId",
                principalTable: "Machines",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ProductionOrderStageExecutions_ProductionShifts_ProductionShiftId",
                table: "ProductionOrderStageExecutions",
                column: "ProductionShiftId",
                principalTable: "ProductionShifts",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ProductionOrderStageExecutions_Machines_MachineId",
                table: "ProductionOrderStageExecutions");

            migrationBuilder.DropForeignKey(
                name: "FK_ProductionOrderStageExecutions_ProductionShifts_ProductionShiftId",
                table: "ProductionOrderStageExecutions");

            migrationBuilder.DropTable(
                name: "ProductionShifts");

            migrationBuilder.DropTable(
                name: "StageMachines");

            migrationBuilder.DropTable(
                name: "Machines");

            migrationBuilder.DropIndex(
                name: "IX_ProductionOrderStageExecutions_MachineId",
                table: "ProductionOrderStageExecutions");

            migrationBuilder.DropIndex(
                name: "IX_ProductionOrderStageExecutions_ProductionShiftId",
                table: "ProductionOrderStageExecutions");

            migrationBuilder.DropColumn(
                name: "MachineId",
                table: "ProductionOrderStageExecutions");

            migrationBuilder.DropColumn(
                name: "ProductionShiftId",
                table: "ProductionOrderStageExecutions");

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
    }
}
