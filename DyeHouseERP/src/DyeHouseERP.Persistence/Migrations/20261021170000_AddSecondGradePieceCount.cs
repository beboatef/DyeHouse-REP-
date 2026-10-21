using DyeHouseERP.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DyeHouseERP.Persistence.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261021170000_AddSecondGradePieceCount")]
public partial class AddSecondGradePieceCount : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "SecondGradePieceCount",
            table: "ProductionOrderStageExecutions",
            type: "int",
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "SecondGradePieceCount",
            table: "ProductionOrderStageExecutions");
    }
}
