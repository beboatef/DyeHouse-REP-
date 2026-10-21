using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DyeHouseERP.Persistence.Migrations;

public partial class AddReadyGoodsCategories : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_ReadyGoodsTransfers_ProductionOrderId",
            table: "ReadyGoodsTransfers");

        migrationBuilder.AddColumn<string>(
            name: "Category",
            table: "ReadyGoodsTransfers",
            type: "nvarchar(30)",
            maxLength: 30,
            nullable: false,
            defaultValue: "FirstGrade");

        migrationBuilder.AddColumn<string>(
            name: "ReadyGoodsCategory",
            table: "InventoryTransactions",
            type: "nvarchar(30)",
            maxLength: 30,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "Category",
            table: "DeliveryLines",
            type: "nvarchar(30)",
            maxLength: 30,
            nullable: false,
            defaultValue: "FirstGrade");

        migrationBuilder.Sql(@"
UPDATE InventoryTransactions
SET ReadyGoodsCategory = 'FirstGrade'
WHERE RawMessageId IS NULL
  AND ProductionOrderId IS NOT NULL
  AND ReadyGoodsCategory IS NULL;");

        migrationBuilder.CreateIndex(
            name: "IX_ReadyGoodsTransfers_ProductionOrderId_Category",
            table: "ReadyGoodsTransfers",
            columns: new[] { "ProductionOrderId", "Category" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_ReadyGoodsTransfers_ProductionOrderId_Category",
            table: "ReadyGoodsTransfers");

        migrationBuilder.DropColumn(name: "Category", table: "ReadyGoodsTransfers");
        migrationBuilder.DropColumn(name: "ReadyGoodsCategory", table: "InventoryTransactions");
        migrationBuilder.DropColumn(name: "Category", table: "DeliveryLines");

        migrationBuilder.CreateIndex(
            name: "IX_ReadyGoodsTransfers_ProductionOrderId",
            table: "ReadyGoodsTransfers",
            column: "ProductionOrderId",
            unique: true);
    }
}
