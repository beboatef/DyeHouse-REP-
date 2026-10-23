using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace DyeHouseERP.Persistence.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261023223000_AllowReadyGoodsCategoriesPerOrder")]
    public partial class AllowReadyGoodsCategoriesPerOrder : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ReadyGoodsTransfers_ProductionOrderId",
                table: "ReadyGoodsTransfers");

            migrationBuilder.CreateIndex(
                name: "IX_ReadyGoodsTransfers_ProductionOrderId_Category",
                table: "ReadyGoodsTransfers",
                columns: new[] { "ProductionOrderId", "Category" },
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ReadyGoodsTransfers_ProductionOrderId_Category",
                table: "ReadyGoodsTransfers");

            migrationBuilder.CreateIndex(
                name: "IX_ReadyGoodsTransfers_ProductionOrderId",
                table: "ReadyGoodsTransfers",
                column: "ProductionOrderId",
                unique: true);
        }
    }
}
