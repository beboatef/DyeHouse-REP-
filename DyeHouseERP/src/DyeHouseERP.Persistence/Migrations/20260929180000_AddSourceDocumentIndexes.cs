using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DyeHouseERP.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSourceDocumentIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // M1: reversal/cancel lookups filter these ledgers by their source
            // document; additive, non-unique, no data changes.
            migrationBuilder.CreateIndex(
                name: "IX_TreasuryTransactions_SourceDocumentId",
                table: "TreasuryTransactions",
                column: "SourceDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryTransactions_SourceDocumentId",
                table: "InventoryTransactions",
                column: "SourceDocumentId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TreasuryTransactions_SourceDocumentId",
                table: "TreasuryTransactions");

            migrationBuilder.DropIndex(
                name: "IX_InventoryTransactions_SourceDocumentId",
                table: "InventoryTransactions");
        }
    }
}
