using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DyeHouseERP.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReadyGoodsTransferStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Existing rows are transfers that were posted when the column did
            // not exist, so they backfill as Posted - never an empty string.
            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "ReadyGoodsTransfers",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Posted");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Status",
                table: "ReadyGoodsTransfers");
        }
    }
}
