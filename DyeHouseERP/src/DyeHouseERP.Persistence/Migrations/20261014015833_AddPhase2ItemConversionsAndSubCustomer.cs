using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DyeHouseERP.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPhase2ItemConversionsAndSubCustomer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ParentCustomerId",
                table: "Customers",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ItemUnitConversions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Operation = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    FromUnit = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ToUnit = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Factor = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    IsLocked = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItemUnitConversions", x => x.Id);
                    table.CheckConstraint("CK_ItemUnitConversions_DifferentUnits", "[FromUnit] <> [ToUnit]");
                    table.CheckConstraint("CK_ItemUnitConversions_PositiveFactor", "[Factor] > 0");
                    table.ForeignKey(
                        name: "FK_ItemUnitConversions_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Customers_ParentCustomerId",
                table: "Customers",
                column: "ParentCustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_ItemUnitConversions_ItemId",
                table: "ItemUnitConversions",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_ItemUnitConversions_ItemId_Operation",
                table: "ItemUnitConversions",
                columns: new[] { "ItemId", "Operation" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Customers_ParentNotSelf",
                table: "Customers",
                sql: "[ParentCustomerId] IS NULL OR [ParentCustomerId] <> [Id]");

            migrationBuilder.AddForeignKey(
                name: "FK_Customers_Customers_ParentCustomerId",
                table: "Customers",
                column: "ParentCustomerId",
                principalTable: "Customers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Customers_Customers_ParentCustomerId",
                table: "Customers");

            migrationBuilder.DropTable(
                name: "ItemUnitConversions");

            migrationBuilder.DropIndex(
                name: "IX_Customers_ParentCustomerId",
                table: "Customers");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Customers_ParentNotSelf",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "ParentCustomerId",
                table: "Customers");
        }
    }
}
