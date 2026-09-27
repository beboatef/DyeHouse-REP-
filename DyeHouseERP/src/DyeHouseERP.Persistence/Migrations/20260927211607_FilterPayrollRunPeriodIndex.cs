using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DyeHouseERP.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FilterPayrollRunPeriodIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PayrollRuns_PeriodYear_PeriodMonth",
                table: "PayrollRuns");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollRuns_PeriodYear_PeriodMonth",
                table: "PayrollRuns",
                columns: new[] { "PeriodYear", "PeriodMonth" },
                unique: true,
                filter: "[Status] <> 'Cancelled'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PayrollRuns_PeriodYear_PeriodMonth",
                table: "PayrollRuns");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollRuns_PeriodYear_PeriodMonth",
                table: "PayrollRuns",
                columns: new[] { "PeriodYear", "PeriodMonth" },
                unique: true);
        }
    }
}
