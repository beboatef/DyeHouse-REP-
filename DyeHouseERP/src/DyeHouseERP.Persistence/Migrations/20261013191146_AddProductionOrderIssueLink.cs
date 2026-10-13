using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DyeHouseERP.Persistence.Migrations
{
    /// <summary>
    /// The canonical issue linkage (spec section 5 + 14). All four columns are nullable and
    /// additive, so no historical row is rewritten and no legacy order is fabricated into
    /// something it was not:
    ///   * ProductionOrders.SourceIssueTransactionId - the single inventory OUT row that
    ///     created this order. Filtered UNIQUE index: exactly one generated order per issue,
    ///     while legacy/reprocessing orders (NULL) stay untouched.
    ///   * ProductionOrders.IssueRequestId - the client idempotency token of the issue
    ///     confirmation. Filtered UNIQUE index: a retried/double-submitted confirmation can
    ///     never create a second order.
    ///   * ProductionOrders.FormationRequestId / FormationBasinId - optional planning
    ///     references. Never a copy of the planning data, and never required on an order.
    /// Existing orders keep NULL everywhere: no issue link is invented for history, and no
    /// quantity is inferred. Historical reconciliation is therefore not needed.
    /// </summary>
    public partial class AddProductionOrderIssueLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "FormationBasinId",
                table: "ProductionOrders",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "FormationRequestId",
                table: "ProductionOrders",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "IssueRequestId",
                table: "ProductionOrders",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SourceIssueTransactionId",
                table: "ProductionOrders",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductionOrders_FormationBasinId",
                table: "ProductionOrders",
                column: "FormationBasinId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionOrders_FormationRequestId",
                table: "ProductionOrders",
                column: "FormationRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionOrders_IssueRequestId",
                table: "ProductionOrders",
                column: "IssueRequestId",
                unique: true,
                filter: "[IssueRequestId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionOrders_SourceIssueTransactionId",
                table: "ProductionOrders",
                column: "SourceIssueTransactionId",
                unique: true,
                filter: "[SourceIssueTransactionId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ProductionOrders_FormationBasinId",
                table: "ProductionOrders");

            migrationBuilder.DropIndex(
                name: "IX_ProductionOrders_FormationRequestId",
                table: "ProductionOrders");

            migrationBuilder.DropIndex(
                name: "IX_ProductionOrders_IssueRequestId",
                table: "ProductionOrders");

            migrationBuilder.DropIndex(
                name: "IX_ProductionOrders_SourceIssueTransactionId",
                table: "ProductionOrders");

            migrationBuilder.DropColumn(
                name: "FormationBasinId",
                table: "ProductionOrders");

            migrationBuilder.DropColumn(
                name: "FormationRequestId",
                table: "ProductionOrders");

            migrationBuilder.DropColumn(
                name: "IssueRequestId",
                table: "ProductionOrders");

            migrationBuilder.DropColumn(
                name: "SourceIssueTransactionId",
                table: "ProductionOrders");
        }
    }
}
