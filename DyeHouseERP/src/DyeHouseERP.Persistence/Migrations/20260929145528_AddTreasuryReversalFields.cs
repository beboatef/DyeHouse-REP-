using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DyeHouseERP.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTreasuryReversalFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ReversesTransactionId",
                table: "TreasuryTransactions",
                type: "uniqueidentifier",
                nullable: true);

            // Existing rows were posted before the column existed, so they
            // backfill as TreasuryDocumentStatus.Posted (= 1) - never 0.
            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "SupplierPayments",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "Receipts",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "Payments",
                type: "int",
                nullable: false,
                defaultValue: 1);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReversesTransactionId",
                table: "TreasuryTransactions");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "SupplierPayments");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "Receipts");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "Payments");
        }
    }
}
