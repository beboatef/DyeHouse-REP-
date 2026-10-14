using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DyeHouseERP.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase3ReceivingIdempotencyAndColor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ReceiveRequestId",
                table: "RawMessages",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Color",
                table: "RawMessageLines",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_RawMessages_ReceiveRequestId",
                table: "RawMessages",
                column: "ReceiveRequestId",
                unique: true,
                filter: "[ReceiveRequestId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RawMessages_ReceiveRequestId",
                table: "RawMessages");

            migrationBuilder.DropColumn(
                name: "Color",
                table: "RawMessageLines");

            migrationBuilder.DropColumn(
                name: "ReceiveRequestId",
                table: "RawMessages");
        }
    }
}
