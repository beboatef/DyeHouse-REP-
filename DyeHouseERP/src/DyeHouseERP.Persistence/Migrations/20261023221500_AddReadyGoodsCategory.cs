using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using DyeHouseERP.Persistence;

#nullable disable

namespace DyeHouseERP.Persistence.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261023221500_AddReadyGoodsCategory")]
    public partial class AddReadyGoodsCategory : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Category",
                table: "ReadyGoodsTransfers",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.DropIndex(
                name: "IX_ReadyGoodsTransfers_ProductionOrderId",
                table: "ReadyGoodsTransfers");

            migrationBuilder.CreateIndex(
                name: "IX_ReadyGoodsTransfers_ProductionOrderId_Category",
                table: "ReadyGoodsTransfers",
                columns: new[] { "ProductionOrderId", "Category" },
                unique: true);

            migrationBuilder.AddColumn<string>(
                name: "Category",
                table: "DeliveryLines",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReadyGoodsCategory",
                table: "InventoryTransactions",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Category",
                table: "DeliveryLines",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "IX_ReadyGoodsTransfers_ProductionOrderId_Category", table: "ReadyGoodsTransfers");
            migrationBuilder.DropColumn(name: "Category", table: "DeliveryLines");
            migrationBuilder.DropColumn(name: "Category", table: "ReadyGoodsTransfers");
            migrationBuilder.CreateIndex(name: "IX_ReadyGoodsTransfers_ProductionOrderId", table: "ReadyGoodsTransfers", column: "ProductionOrderId", unique: true);
            migrationBuilder.DropColumn(name: "ReadyGoodsCategory", table: "InventoryTransactions");
            migrationBuilder.DropColumn(name: "Category", table: "DeliveryLines");
        }
    }
}
