using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DyeHouseERP.Persistence.Migrations
{
    /// <summary>
    /// Two final decisions, applied together as one schema change.
    ///
    /// 1. REMOVE PRODUCTION STAGE APPROVAL. The stage-level approval workflow is gone:
    ///    ProductionStageDefinitions.RequiresApproval and the execution's
    ///    ApprovalRequired / ApprovedBy / ApprovedAtUtc columns are dropped. No other
    ///    approval workflow is touched - inventory safeguards, negative-stock separation
    ///    of duties (ApprovalRequests) and Job Order costing approval all stay.
    ///
    /// 2. DECOUPLE FORMATION REQUESTS FROM PRODUCTION ORDERS. A Formation Request and a
    ///    Production Order are independent documents: the Job Order's FormationRequestId /
    ///    FormationGroupId / FormationBasinId link columns and the request's ProductionOrderId
    ///    link column (with their indexes) are dropped. Nothing replaces them.
    ///
    /// Purely subtractive: no table, precision, string length or constraint is weakened other
    /// than the removed columns and their indexes.
    /// </summary>
    /// <inheritdoc />
    public partial class RemoveStageApprovalAndDecoupleFormationRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ProductionOrders_FormationBasinId",
                table: "ProductionOrders");

            migrationBuilder.DropIndex(
                name: "IX_ProductionOrders_FormationGroupId",
                table: "ProductionOrders");

            migrationBuilder.DropIndex(
                name: "IX_ProductionOrders_FormationRequestId",
                table: "ProductionOrders");

            migrationBuilder.DropIndex(
                name: "IX_FormationRequests_ProductionOrderId",
                table: "FormationRequests");

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

            migrationBuilder.DropColumn(
                name: "FormationBasinId",
                table: "ProductionOrders");

            migrationBuilder.DropColumn(
                name: "FormationGroupId",
                table: "ProductionOrders");

            migrationBuilder.DropColumn(
                name: "FormationRequestId",
                table: "ProductionOrders");

            migrationBuilder.DropColumn(
                name: "ProductionOrderId",
                table: "FormationRequests");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
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

            migrationBuilder.AddColumn<Guid>(
                name: "FormationBasinId",
                table: "ProductionOrders",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "FormationGroupId",
                table: "ProductionOrders",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "FormationRequestId",
                table: "ProductionOrders",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProductionOrderId",
                table: "FormationRequests",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductionOrders_FormationBasinId",
                table: "ProductionOrders",
                column: "FormationBasinId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionOrders_FormationGroupId",
                table: "ProductionOrders",
                column: "FormationGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionOrders_FormationRequestId",
                table: "ProductionOrders",
                column: "FormationRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_FormationRequests_ProductionOrderId",
                table: "FormationRequests",
                column: "ProductionOrderId");
        }
    }
}
