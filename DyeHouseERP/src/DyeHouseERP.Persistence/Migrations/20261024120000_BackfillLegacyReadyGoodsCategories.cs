using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace DyeHouseERP.Persistence.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261024120000_BackfillLegacyReadyGoodsCategories")]
    public partial class BackfillLegacyReadyGoodsCategories : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Before category tracking was introduced, existing ready-goods movements
            // represented the ordinary finished output. Classify those legacy rows as
            // FirstGrade so they remain visible in category-specific balances and delivery
            // statements instead of appearing as "Unclassified".
            migrationBuilder.Sql(@"
UPDATE ReadyGoodsTransfers
SET Category = N'FirstGrade'
WHERE Category IS NULL;

UPDATE DeliveryLines
SET Category = N'FirstGrade'
WHERE Category IS NULL;

UPDATE InventoryTransactions
SET ReadyGoodsCategory = N'FirstGrade'
WHERE ReadyGoodsCategory IS NULL
  AND RawMessageId IS NULL
  AND ProductionOrderId IS NOT NULL;
");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Intentionally no reverse data update: after this backfill, FirstGrade values
            // may also be created by normal posting, so resetting them to NULL would erase
            // valid category data.
        }
    }
}
