using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DyeHouseERP.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RemoveInertStageCapabilityFlags : Migration
    {
        /// <inheritdoc />
        /// <summary>
        /// D3: drops the three stage flags that NO code path ever read. Both the seeded stages and
        /// every stage created through the admin screen stored them, and the screen displayed them as
        /// if they did something, which is why they are removed rather than left in place. Only
        /// AllowSkip survives, because SkipStageCommand actually consults it.
        ///
        /// DATA LOSS: the stored booleans themselves are discarded. Nothing else depends on them, so no
        /// business data is affected; the values were administrative preferences that had no effect.
        /// Rollback (Down) recreates the three columns as bit NOT NULL default 0, i.e. every stage comes
        /// back with the flags OFF - which is the state the seeder always produced anyway.
        /// </summary>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AllowRepeat",
                table: "ProductionStageDefinitions");

            migrationBuilder.DropColumn(
                name: "AllowReturn",
                table: "ProductionStageDefinitions");

            migrationBuilder.DropColumn(
                name: "AllowRework",
                table: "ProductionStageDefinitions");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AllowRepeat",
                table: "ProductionStageDefinitions",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "AllowReturn",
                table: "ProductionStageDefinitions",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "AllowRework",
                table: "ProductionStageDefinitions",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }
    }
}
