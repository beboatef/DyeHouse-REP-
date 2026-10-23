using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace DyeHouseERP.Persistence.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261023223000_AllowReadyGoodsCategoriesPerOrder")]
    public partial class AllowReadyGoodsCategoriesPerOrder : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The preceding AddReadyGoodsCategory migration already replaces the old
            // single-transfer-per-order index with the filtered unique index on
            // (ProductionOrderId, Category). Repeating the DROP of the old index here
            // makes a fresh database migration fail because that index no longer exists.
            // The schema is already in the required state, so this migration is a no-op.
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No schema change was made by Up; keep the schema from the preceding
            // migration intact when rolling this migration back.
        }
    }
}
