using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DyeHouseERP.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RemoveCustomerAccountNumberDefault : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "AccountNumber",
                table: "Customers",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(50)",
                oldMaxLength: 50,
                oldDefaultValueSql: "CONVERT(nvarchar(50), [Code])");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Intentionally empty: the DEFAULT this migration removes referenced
            // another column ([Code]), which SQL Server rejects outright, so it
            // can never be recreated. A valid rollback would have nothing to
            // restore - AccountNumber is always supplied by the domain layer.
        }
    }
}
