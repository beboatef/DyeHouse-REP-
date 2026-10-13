using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DyeHouseERP.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMaterialIssueCancellation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CancellationReason",
                table: "MaterialIssues",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CancelledAtUtc",
                table: "MaterialIssues",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CancelledBy",
                table: "MaterialIssues",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            // Existing issues were posted when the column did not exist, so they backfill as
            // Posted (1). 0 is not a valid MaterialIssueStatus and must never appear in the table.
            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "MaterialIssues",
                type: "int",
                nullable: false,
                defaultValue: 1);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CancellationReason",
                table: "MaterialIssues");

            migrationBuilder.DropColumn(
                name: "CancelledAtUtc",
                table: "MaterialIssues");

            migrationBuilder.DropColumn(
                name: "CancelledBy",
                table: "MaterialIssues");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "MaterialIssues");
        }
    }
}
