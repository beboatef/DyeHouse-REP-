using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DyeHouseERP.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase5ReadyGoodsDeliveryReturns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AllowSkip",
                table: "ProductionStageDefinitions");

            migrationBuilder.AddColumn<decimal>(
                name: "SecondGradeKg",
                table: "ProductionOrderStageExecutions",
                type: "decimal(18,3)",
                precision: 18,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "SecondGradeMeter",
                table: "ProductionOrderStageExecutions",
                type: "decimal(18,3)",
                precision: 18,
                scale: 3,
                nullable: true);

            // Cost responsibility (spec section 20). The default is Customer: every Job Order that
            // existed before this column was worked for the customer and is the customer's cost.
            migrationBuilder.AddColumn<int>(
                name: "CostBearer",
                table: "ProductionOrders",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<Guid>(
                name: "CustomerReturnId",
                table: "ProductionOrders",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "OriginalRawQuantityKg",
                table: "DeliveryLines",
                type: "decimal(18,3)",
                precision: 18,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "OriginalRawQuantityMeter",
                table: "DeliveryLines",
                type: "decimal(18,3)",
                precision: 18,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DeliverRequestId",
                table: "Deliveries",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PrimaryCustomerId",
                table: "Deliveries",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PrimaryDeliveryId",
                table: "Deliveries",
                type: "uniqueidentifier",
                nullable: true);

            // Owner of the returned material (spec section 32). The default is the customer's own
            // material, which is what every row recorded before this column actually is.
            migrationBuilder.AddColumn<string>(
                name: "Ownership",
                table: "CustomerReturns",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "CustomerOwned");

            migrationBuilder.AddColumn<string>(
                name: "ReferenceDocument",
                table: "CustomerReturns",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ReadyGoodsRawReturns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MovementNumber = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    MovementDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ProductionOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceWarehouseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DestinationRawMessageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DestinationWarehouseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QuantityKg = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: true),
                    QuantityMeter = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    IsLocked = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReadyGoodsRawReturns", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Deliveries_DeliverRequestId",
                table: "Deliveries",
                column: "DeliverRequestId",
                unique: true,
                filter: "[DeliverRequestId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Deliveries_PrimaryDeliveryId",
                table: "Deliveries",
                column: "PrimaryDeliveryId");

            migrationBuilder.CreateIndex(
                name: "IX_ReadyGoodsRawReturns_CustomerId",
                table: "ReadyGoodsRawReturns",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_ReadyGoodsRawReturns_DestinationRawMessageId",
                table: "ReadyGoodsRawReturns",
                column: "DestinationRawMessageId");

            migrationBuilder.CreateIndex(
                name: "IX_ReadyGoodsRawReturns_MovementDate",
                table: "ReadyGoodsRawReturns",
                column: "MovementDate");

            migrationBuilder.CreateIndex(
                name: "IX_ReadyGoodsRawReturns_MovementNumber",
                table: "ReadyGoodsRawReturns",
                column: "MovementNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReadyGoodsRawReturns_ProductionOrderId",
                table: "ReadyGoodsRawReturns",
                column: "ProductionOrderId");

            migrationBuilder.AddForeignKey(
                name: "FK_Deliveries_Deliveries_PrimaryDeliveryId",
                table: "Deliveries",
                column: "PrimaryDeliveryId",
                principalTable: "Deliveries",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Deliveries_Deliveries_PrimaryDeliveryId",
                table: "Deliveries");

            migrationBuilder.DropTable(
                name: "ReadyGoodsRawReturns");

            migrationBuilder.DropIndex(
                name: "IX_Deliveries_DeliverRequestId",
                table: "Deliveries");

            migrationBuilder.DropIndex(
                name: "IX_Deliveries_PrimaryDeliveryId",
                table: "Deliveries");

            migrationBuilder.DropColumn(
                name: "SecondGradeKg",
                table: "ProductionOrderStageExecutions");

            migrationBuilder.DropColumn(
                name: "SecondGradeMeter",
                table: "ProductionOrderStageExecutions");

            migrationBuilder.DropColumn(
                name: "CostBearer",
                table: "ProductionOrders");

            migrationBuilder.DropColumn(
                name: "CustomerReturnId",
                table: "ProductionOrders");

            migrationBuilder.DropColumn(
                name: "OriginalRawQuantityKg",
                table: "DeliveryLines");

            migrationBuilder.DropColumn(
                name: "OriginalRawQuantityMeter",
                table: "DeliveryLines");

            migrationBuilder.DropColumn(
                name: "DeliverRequestId",
                table: "Deliveries");

            migrationBuilder.DropColumn(
                name: "PrimaryCustomerId",
                table: "Deliveries");

            migrationBuilder.DropColumn(
                name: "PrimaryDeliveryId",
                table: "Deliveries");

            migrationBuilder.DropColumn(
                name: "Ownership",
                table: "CustomerReturns");

            migrationBuilder.DropColumn(
                name: "ReferenceDocument",
                table: "CustomerReturns");

            migrationBuilder.AddColumn<bool>(
                name: "AllowSkip",
                table: "ProductionStageDefinitions",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }
    }
}
