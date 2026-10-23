using DyeHouseERP.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DyeHouseERP.Persistence.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261024130000_AddReadyGoodsRawReturnCategory")]
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

            // Preserve the category already recorded on the ready-goods OUT ledger row.
            // Older rows have been backfilled to FirstGrade by the preceding migration.
            migrationBuilder.Sql(@"
UPDATE rr
SET rr.Category = COALESCE(tx.ReadyGoodsCategory, N'FirstGrade')
FROM ReadyGoodsRawReturns AS rr
LEFT JOIN InventoryTransactions AS tx
  ON tx.SourceDocumentId = rr.Id
 AND tx.SourceDocumentType = N'ReadyGoodsRawReturn'
 AND tx.Direction = N'Out'
WHERE rr.Category IS NULL;
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