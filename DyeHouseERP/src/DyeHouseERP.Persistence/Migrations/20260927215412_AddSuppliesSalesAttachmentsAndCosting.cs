using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DyeHouseERP.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSuppliesSalesAttachmentsAndCosting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ActualReturnDate",
                table: "RawExternalReleases",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CancellationReason",
                table: "RawExternalReleases",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ExpectedReturnDate",
                table: "RawExternalReleases",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ExternalProcessingCost",
                table: "RawExternalReleases",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalProcessingStage",
                table: "RawExternalReleases",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProductionOrderId",
                table: "RawExternalReleases",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReturnedBy",
                table: "RawExternalReleases",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ReturnedQuantityKg",
                table: "RawExternalReleases",
                type: "decimal(18,3)",
                precision: 18,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ReturnedQuantityMeter",
                table: "RawExternalReleases",
                type: "decimal(18,3)",
                precision: 18,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "RawExternalReleases",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "ApprovedCost",
                table: "ProductionOrders",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CostApprovedAtUtc",
                table: "ProductionOrders",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CostApprovedBy",
                table: "ProductionOrders",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CostingNotes",
                table: "ProductionOrders",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "EstimatedCost",
                table: "ProductionOrders",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "Materials",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "ReorderLevel",
                table: "Materials",
                type: "decimal(18,3)",
                precision: 18,
                scale: 3,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Attachments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EntityType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    EntityId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Content = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    UploadedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    UploadedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Attachments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MaterialSales",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SaleNumber = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    SaleDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    BuyerName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    TreasuryAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PaymentMethod = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Discount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Tax = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    PostedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    PostedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CancelledBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CancelledAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CancellationReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    IsLocked = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaterialSales", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SupplyIssues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IssueNumber = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    IssueDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DepartmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IssuedTo = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Purpose = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    PostedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    PostedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CancelledBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CancelledAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CancellationReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    IsLocked = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupplyIssues", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MaterialSaleLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MaterialSaleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MaterialId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    Unit = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    UnitPrice = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaterialSaleLines", x => x.Id);
                    table.CheckConstraint("CK_MaterialSaleLines_QuantityPositive", "[Quantity] > 0 AND [UnitPrice] >= 0");
                    table.ForeignKey(
                        name: "FK_MaterialSaleLines_MaterialSales_MaterialSaleId",
                        column: x => x.MaterialSaleId,
                        principalTable: "MaterialSales",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SupplyIssueLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SupplyIssueId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MaterialId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    Unit = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    UnitCost = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupplyIssueLines", x => x.Id);
                    table.CheckConstraint("CK_SupplyIssueLines_QuantityPositive", "[Quantity] > 0 AND [UnitCost] >= 0");
                    table.ForeignKey(
                        name: "FK_SupplyIssueLines_SupplyIssues_SupplyIssueId",
                        column: x => x.SupplyIssueId,
                        principalTable: "SupplyIssues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RawExternalReleases_ProductionOrderId",
                table: "RawExternalReleases",
                column: "ProductionOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_RawExternalReleases_Status",
                table: "RawExternalReleases",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Materials_Kind",
                table: "Materials",
                column: "Kind");

            migrationBuilder.CreateIndex(
                name: "IX_Attachments_EntityType_EntityId",
                table: "Attachments",
                columns: new[] { "EntityType", "EntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_Attachments_UploadedAtUtc",
                table: "Attachments",
                column: "UploadedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_MaterialSaleLines_MaterialId",
                table: "MaterialSaleLines",
                column: "MaterialId");

            migrationBuilder.CreateIndex(
                name: "IX_MaterialSaleLines_MaterialSaleId",
                table: "MaterialSaleLines",
                column: "MaterialSaleId");

            migrationBuilder.CreateIndex(
                name: "IX_MaterialSales_CustomerId",
                table: "MaterialSales",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_MaterialSales_SaleDate",
                table: "MaterialSales",
                column: "SaleDate");

            migrationBuilder.CreateIndex(
                name: "IX_MaterialSales_SaleNumber",
                table: "MaterialSales",
                column: "SaleNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MaterialSales_Status",
                table: "MaterialSales",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_MaterialSales_WarehouseId",
                table: "MaterialSales",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplyIssueLines_MaterialId",
                table: "SupplyIssueLines",
                column: "MaterialId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplyIssueLines_SupplyIssueId",
                table: "SupplyIssueLines",
                column: "SupplyIssueId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplyIssues_DepartmentId",
                table: "SupplyIssues",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplyIssues_IssueDate",
                table: "SupplyIssues",
                column: "IssueDate");

            migrationBuilder.CreateIndex(
                name: "IX_SupplyIssues_IssueNumber",
                table: "SupplyIssues",
                column: "IssueNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SupplyIssues_Status",
                table: "SupplyIssues",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_SupplyIssues_WarehouseId",
                table: "SupplyIssues",
                column: "WarehouseId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Attachments");

            migrationBuilder.DropTable(
                name: "MaterialSaleLines");

            migrationBuilder.DropTable(
                name: "SupplyIssueLines");

            migrationBuilder.DropTable(
                name: "MaterialSales");

            migrationBuilder.DropTable(
                name: "SupplyIssues");

            migrationBuilder.DropIndex(
                name: "IX_RawExternalReleases_ProductionOrderId",
                table: "RawExternalReleases");

            migrationBuilder.DropIndex(
                name: "IX_RawExternalReleases_Status",
                table: "RawExternalReleases");

            migrationBuilder.DropIndex(
                name: "IX_Materials_Kind",
                table: "Materials");

            migrationBuilder.DropColumn(
                name: "ActualReturnDate",
                table: "RawExternalReleases");

            migrationBuilder.DropColumn(
                name: "CancellationReason",
                table: "RawExternalReleases");

            migrationBuilder.DropColumn(
                name: "ExpectedReturnDate",
                table: "RawExternalReleases");

            migrationBuilder.DropColumn(
                name: "ExternalProcessingCost",
                table: "RawExternalReleases");

            migrationBuilder.DropColumn(
                name: "ExternalProcessingStage",
                table: "RawExternalReleases");

            migrationBuilder.DropColumn(
                name: "ProductionOrderId",
                table: "RawExternalReleases");

            migrationBuilder.DropColumn(
                name: "ReturnedBy",
                table: "RawExternalReleases");

            migrationBuilder.DropColumn(
                name: "ReturnedQuantityKg",
                table: "RawExternalReleases");

            migrationBuilder.DropColumn(
                name: "ReturnedQuantityMeter",
                table: "RawExternalReleases");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "RawExternalReleases");

            migrationBuilder.DropColumn(
                name: "ApprovedCost",
                table: "ProductionOrders");

            migrationBuilder.DropColumn(
                name: "CostApprovedAtUtc",
                table: "ProductionOrders");

            migrationBuilder.DropColumn(
                name: "CostApprovedBy",
                table: "ProductionOrders");

            migrationBuilder.DropColumn(
                name: "CostingNotes",
                table: "ProductionOrders");

            migrationBuilder.DropColumn(
                name: "EstimatedCost",
                table: "ProductionOrders");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "Materials");

            migrationBuilder.DropColumn(
                name: "ReorderLevel",
                table: "Materials");
        }
    }
}
