using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DyeHouseERP.Persistence.Migrations
{
    /// <summary>
    /// Multiple basins per Formation Request group (spec sections 10-11).
    ///
    /// This migration is PURELY ADDITIVE and deliberately contains no data migration at all:
    ///   - it creates the new FormationBasins table (one row per basin, FK to its parent group);
    ///   - it adds the nullable ProductionOrders.FormationBasinId column and its index.
    ///
    /// Existing data is preserved by construction, not by a backfill:
    ///   * no existing FormationGroups row is updated, deleted or renumbered, so every historical group keeps
    ///     its PlannedQuantity, ProducedQuantity and specification snapshot exactly as they were;
    ///   * a group with zero basins is the normal, fully supported case - its own PlannedQuantity and its own
    ///     specification ARE the basin, which is precisely how it behaved before this migration;
    ///   * the request header total is computed as the sum of group quantities, and a group's quantity only
    ///     becomes the sum of its basins once basins actually exist, so totals cannot drift either way;
    ///   * Down() only drops what Up() created, so the change is fully reversible.
    /// </summary>
    public partial class AddFormationBasins : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "FormationBasinId",
                table: "ProductionOrders",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "FormationBasins",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FormationGroupId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BasinNumber = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    PlannedQuantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    Unit = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    TubCount = table.Column<int>(type: "int", nullable: true),
                    Color = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    WidthCm = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: true),
                    MetersPerKg = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    Gsm = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: true),
                    TubFormat = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    WindingTapeFormat = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    QualityInstructions = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    LabInstructions = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    InternalInstructions = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CustomerInstructions = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    ProducedQuantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    SpecificationTemplateId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SpecificationTemplateName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    SpecificationSnapshotAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FormationBasins", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FormationBasins_FormationGroups_FormationGroupId",
                        column: x => x.FormationGroupId,
                        principalTable: "FormationGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProductionOrders_FormationBasinId",
                table: "ProductionOrders",
                column: "FormationBasinId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionOrders_FormationGroupId",
                table: "ProductionOrders",
                column: "FormationGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_FormationBasins_FormationGroupId_BasinNumber",
                table: "FormationBasins",
                columns: new[] { "FormationGroupId", "BasinNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FormationBasins_SpecificationTemplateId",
                table: "FormationBasins",
                column: "SpecificationTemplateId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FormationBasins");

            migrationBuilder.DropIndex(
                name: "IX_ProductionOrders_FormationBasinId",
                table: "ProductionOrders");

            migrationBuilder.DropIndex(
                name: "IX_ProductionOrders_FormationGroupId",
                table: "ProductionOrders");

            migrationBuilder.DropColumn(
                name: "FormationBasinId",
                table: "ProductionOrders");
        }
    }
}
