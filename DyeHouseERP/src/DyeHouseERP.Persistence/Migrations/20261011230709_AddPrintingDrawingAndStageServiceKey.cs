using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DyeHouseERP.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPrintingDrawingAndStageServiceKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ProductionOrderServicePrices_ProductionOrderId_Unit",
                table: "ProductionOrderServicePrices");

            migrationBuilder.AddColumn<bool>(
                name: "RequiresDrawingNumber",
                table: "ProductionStageDefinitions",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "DrawingNumber",
                table: "ProductionOrderStageExecutions",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "RequiresDrawingNumber",
                table: "ProductionOrderStageExecutions",
                type: "bit",
                nullable: false,
                defaultValue: false);

            // Stable seed identity, independent of display sequence. Preserve all existing stages.
            migrationBuilder.Sql("""
                IF NOT EXISTS (SELECT 1 FROM [ProductionStageDefinitions] WHERE [Code] = N'PRINTING')
                INSERT INTO [ProductionStageDefinitions]
                    ([Id], [Code], [Name], [Sequence], [IsActive], [RequiresInputQuantity],
                     [RequiresOutputQuantity], [RequiresApproval], [AllowSkip], [AllowRepeat],
                     [AllowRework], [AllowReturn], [IsFormationStage], [IsReadyGoodsStage],
                     [RequiresDrawingNumber], [IsLocked], [CreatedBy], [CreatedAtUtc])
                VALUES
                    ('bf836f18-d834-44c1-9364-b6491972ff40', N'PRINTING', N'الطباعة',
                     (SELECT ISNULL(MAX([Sequence]), 0) + 1 FROM [ProductionStageDefinitions]),
                     1, 1, 1, 0, 0, 0, 0, 0, 0, 0, 1, 0, N'seed', SYSUTCDATETIME());
                """);

            migrationBuilder.CreateIndex(
                name: "IX_ProductionOrderServicePrices_ProductionOrderId_StageDefinitionId_Unit",
                table: "ProductionOrderServicePrices",
                columns: new[] { "ProductionOrderId", "StageDefinitionId", "Unit" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Fail clearly rather than erase drawings or stage prices during rollback.
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [ProductionOrderStageExecutions] WHERE [DrawingNumber] IS NOT NULL)
                   OR EXISTS (SELECT 1 FROM [ProductionOrderServicePrices] GROUP BY [ProductionOrderId], [Unit] HAVING COUNT(*) > 1)
                    THROW 51000, 'Printing rollback would discard drawing or stage-pricing data; rollback refused.', 1;
                """);
            migrationBuilder.DropIndex(
                name: "IX_ProductionOrderServicePrices_ProductionOrderId_StageDefinitionId_Unit",
                table: "ProductionOrderServicePrices");

            migrationBuilder.DropColumn(
                name: "RequiresDrawingNumber",
                table: "ProductionStageDefinitions");

            migrationBuilder.DropColumn(
                name: "DrawingNumber",
                table: "ProductionOrderStageExecutions");

            migrationBuilder.DropColumn(
                name: "RequiresDrawingNumber",
                table: "ProductionOrderStageExecutions");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionOrderServicePrices_ProductionOrderId_Unit",
                table: "ProductionOrderServicePrices",
                columns: new[] { "ProductionOrderId", "Unit" },
                unique: true);
        }
    }
}
