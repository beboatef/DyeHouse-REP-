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

            migrationBuilder.AddColumn<string>(
                name: "ReadyGoodsCategory",
                table: "InventoryTransactions",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "Category", table: "ReadyGoodsTransfers");
            migrationBuilder.DropColumn(name: "ReadyGoodsCategory", table: "InventoryTransactions");
        }
    }
}
