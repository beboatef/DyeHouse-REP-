using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DyeHouseERP.Persistence.Migrations
{
    /// <summary>
    /// Adds the columns behind three edits that are safe because they never rewrite posted history:
    ///   * TreasuryTransfers.Status - a posted transfer is now cancellable through the same append-only
    ///     reversal model as receipts/payments, instead of being an un-correctable row.
    ///   * TreasuryTransfers.RowVersion / PurchaseOrders.RowVersion - optimistic concurrency, so a
    ///     concurrent cancel/correction cannot double-reverse or clash with a receipt.
    ///   * FormationRequests.RevisionOfFormationRequestId - a submitted/approved request is corrected by a
    ///     new Draft revision instead of being edited in place.
    /// All three are additive-only; nothing posted is rewritten and no existing row is deleted.
    /// </summary>
    public partial class AddTreasuryTransferCancellationAndEditCorrections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "TreasuryTransfers",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            // Existing transfers were posted before the column existed, so they backfill as
            // TreasuryDocumentStatus.Posted (= 1) - never the 0 enum default, which is undefined.
            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "TreasuryTransfers",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "PurchaseOrders",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<Guid>(
                name: "RevisionOfFormationRequestId",
                table: "FormationRequests",
                type: "uniqueidentifier",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "TreasuryTransfers");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "TreasuryTransfers");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "PurchaseOrders");

            migrationBuilder.DropColumn(
                name: "RevisionOfFormationRequestId",
                table: "FormationRequests");
        }
    }
}
