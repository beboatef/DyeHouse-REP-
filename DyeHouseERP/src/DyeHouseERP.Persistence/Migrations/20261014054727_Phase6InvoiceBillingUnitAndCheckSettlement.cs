using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DyeHouseERP.Persistence.Migrations
{
    /// <summary>
    /// Phase 6 (finance, invoicing, treasury &amp; cheques). Four schema changes, each explained where
    /// it is applied below; the one backfill that needs a deliberate value is the invoice line's
    /// billing unit, defaulted to 'KG' because that is the entity's own default and the unit every
    /// pre-existing line was implicitly billed in (the column did not exist before, so no stored data
    /// is reinterpreted: an old line reads as the same unit it was always priced in).
    ///
    /// This migration is CREATED, NOT APPLIED.
    /// </summary>
    /// <inheritdoc />
    public partial class Phase6InvoiceBillingUnitAndCheckSettlement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // SupplierPayments.TreasuryAccountId becomes NULLABLE: a supplier payment settled by an
            // endorsed customer cheque has NO account, because no money left one (spec section 39).
            // The plain index is replaced by a UNIQUE FILTERED one on CheckId below (cancelled
            // payments excluded) so one cheque can settle at most one live payment.
            migrationBuilder.DropIndex(
                name: "IX_SupplierPayments_CheckId",
                table: "SupplierPayments");

            migrationBuilder.AlterColumn<Guid>(
                name: "TreasuryAccountId",
                table: "SupplierPayments",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AddColumn<Guid>(
                name: "CostEntryId",
                table: "InvoiceLines",
                type: "uniqueidentifier",
                nullable: true);

            // InvoiceLines.Unit: the unit the SERVICE is billed in, taken from the order's configured
            // service price. Existing rows default to 'KG' - the domain's own default and the unit
            // those lines were already priced per.
            migrationBuilder.AddColumn<string>(
                name: "Unit",
                table: "InvoiceLines",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "KG");

            // CostEntries.IsChargeableToCustomer: false for every existing cost entry, so no
            // historical (reprocessing/separate) cost can suddenly be billed to a customer.
            migrationBuilder.AddColumn<bool>(
                name: "IsChargeableToCustomer",
                table: "CostEntries",
                type: "bit",
                nullable: false,
                defaultValue: false);

            // Database-level backstop for "one cheque settles one payable": a unique index over the
            // NON-CANCELLED cheque-settled payments (TreasuryDocumentStatus.Cancelled = 2).
            migrationBuilder.CreateIndex(
                name: "IX_SupplierPayments_CheckId",
                table: "SupplierPayments",
                column: "CheckId",
                unique: true,
                filter: "[CheckId] IS NOT NULL AND [Status] <> 2");

            // Database-level backstop for "a cost is billed once": one invoice line per chargeable cost entry.
            migrationBuilder.CreateIndex(
                name: "IX_InvoiceLines_CostEntryId",
                table: "InvoiceLines",
                column: "CostEntryId",
                unique: true,
                filter: "[CostEntryId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SupplierPayments_CheckId",
                table: "SupplierPayments");

            migrationBuilder.DropIndex(
                name: "IX_InvoiceLines_CostEntryId",
                table: "InvoiceLines");

            migrationBuilder.DropColumn(
                name: "CostEntryId",
                table: "InvoiceLines");

            migrationBuilder.DropColumn(
                name: "Unit",
                table: "InvoiceLines");

            migrationBuilder.DropColumn(
                name: "IsChargeableToCustomer",
                table: "CostEntries");

            migrationBuilder.AlterColumn<Guid>(
                name: "TreasuryAccountId",
                table: "SupplierPayments",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SupplierPayments_CheckId",
                table: "SupplierPayments",
                column: "CheckId");
        }
    }
}
