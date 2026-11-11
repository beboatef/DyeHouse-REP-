using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DyeHouseERP.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPasswordPolicy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "MustChangePassword",
                table: "Users",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "PasswordChangedAtUtc",
                table: "Users",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastActivityAtUtc",
                table: "UserRefreshTokens",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<int>(
                name: "PasswordMaxAgeDays",
                table: "CompanySettings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "SessionIdleMinutes",
                table: "CompanySettings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "PasswordHistories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PasswordHash = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    IsLocked = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PasswordHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PasswordHistories_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PasswordHistories_UserId_CreatedAtUtc",
                table: "PasswordHistories",
                columns: new[] { "UserId", "CreatedAtUtc" });

            // Backfill so the new policy has a truthful base on existing rows:
            // - existing live sessions count as active since their creation (not since year 1,
            //   which would idle-kill every session on the next refresh);
            // - the idle default is the 30-minute policy default, matching new rows;
            // - passwords of unknown age count from this rollout, so expiry starts applying
            //   to legacy accounts instead of never applying (NULL would exempt them forever).
            migrationBuilder.Sql(
                "UPDATE [UserRefreshTokens] SET [LastActivityAtUtc] = [CreatedAtUtc] " +
                "WHERE [LastActivityAtUtc] = '0001-01-01';");
            migrationBuilder.Sql(
                "UPDATE [CompanySettings] SET [SessionIdleMinutes] = 30 " +
                "WHERE [SessionIdleMinutes] = 0;");
            migrationBuilder.Sql(
                "UPDATE [Users] SET [PasswordChangedAtUtc] = GETUTCDATE() " +
                "WHERE [PasswordChangedAtUtc] IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PasswordHistories");

            migrationBuilder.DropColumn(
                name: "MustChangePassword",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "PasswordChangedAtUtc",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "LastActivityAtUtc",
                table: "UserRefreshTokens");

            migrationBuilder.DropColumn(
                name: "PasswordMaxAgeDays",
                table: "CompanySettings");

            migrationBuilder.DropColumn(
                name: "SessionIdleMinutes",
                table: "CompanySettings");
        }
    }
}
