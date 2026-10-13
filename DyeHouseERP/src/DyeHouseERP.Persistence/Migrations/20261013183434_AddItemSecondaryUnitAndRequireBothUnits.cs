using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DyeHouseERP.Persistence.Migrations
{
    /// <summary>
    /// Structured primary/secondary units on the item master (spec section 5).
    /// All three changes are additive-only and safe for existing data:
    ///   * Items.SecondaryUnit - nullable, so every existing item keeps a single unit
    ///     (BaseUnit only) and no historical quantity is reinterpreted.
    ///   * Items.RequireBothUnits - NOT NULL with a false default, so existing items
    ///     keep their current single-measurement behavior.
    ///   * two check constraints - a secondary unit is either absent or different from
    ///     the base unit, and "require both" implies a secondary unit is configured.
    /// The secondary unit is deliberately NOT guessed from Notes: only a reliable
    /// structured value is ever populated, so the backfill leaves it NULL.
    /// </summary>
    public partial class AddItemSecondaryUnitAndRequireBothUnits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "RequireBothUnits",
                table: "Items",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "SecondaryUnit",
                table: "Items",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Items_RequireBothUnitsNeedsSecondary",
                table: "Items",
                sql: "[RequireBothUnits] = 0 OR [SecondaryUnit] IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Items_SecondaryUnitDiffers",
                table: "Items",
                sql: "[SecondaryUnit] IS NULL OR [SecondaryUnit] <> [BaseUnit]");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Items_RequireBothUnitsNeedsSecondary",
                table: "Items");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Items_SecondaryUnitDiffers",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "RequireBothUnits",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "SecondaryUnit",
                table: "Items");
        }
    }
}
