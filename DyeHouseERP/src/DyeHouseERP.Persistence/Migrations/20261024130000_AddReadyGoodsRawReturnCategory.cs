using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DyeHouseERP.Persistence.Migrations
{
    public partial class AddReadyGoodsRawReturnCategory : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Category",
                table: "ReadyGoodsRawReturns",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            // The ready-goods OUT ledger row is authoritative. Do not infer a category
            // for legacy documents whose corresponding ledger row/category is unavailable.
            migrationBuilder.Sql(@"
UPDATE rr
SET Category = tx.ReadyGoodsCategory
FROM ReadyGoodsRawReturns AS rr
INNER JOIN InventoryTransactions AS tx
  ON tx.SourceDocumentId = rr.Id
 AND tx.SourceDocumentType = N'ReadyGoodsRawReturn'
 AND tx.Direction = N'Out'
WHERE rr.Category IS NULL
  AND tx.ReadyGoodsCategory IS NOT NULL;
");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Category",
                table: "ReadyGoodsRawReturns");
        }
    }
}