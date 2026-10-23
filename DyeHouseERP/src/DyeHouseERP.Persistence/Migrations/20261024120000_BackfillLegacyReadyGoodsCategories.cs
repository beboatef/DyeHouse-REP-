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
            // Historical rows do not contain reliable quality information.
            // Leave their category NULL rather than guessing that they were FirstGrade.
            // New postings must supply a category through the normal command workflow.
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Intentionally no reverse data update: after this backfill, FirstGrade values
            // may also be created by normal posting, so resetting them to NULL would erase
            // valid category data.
        }
    }
}
