using DyeHouseERP.Persistence;
using DyeHouseERP.Persistence.Services;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DyeHouseERP.IntegrationTests;

/// <summary>
/// Restore-drill verification entry point (see docs/operations/backup-and-restore.md).
///
/// The CI `backup-drill` job restores the latest backup into a scratch database and
/// points DYEHOUSE_RESTOREREVERIFY_CONNECTION at it. These tests then recompute
/// balances through the application's OWN ledger services and compare them against
/// independent hand-written SQL SUMs on the same restored copy: any divergence
/// means the restore did not faithfully reproduce the ledger the application reads.
/// Outside the drill (env var unset) the tests skip instead of failing.
/// </summary>
public class BackupRestoreVerificationTests
{
    private const int MaxKeysPerCheck = 500;

    private static string? ScratchConnection =>
        Environment.GetEnvironmentVariable("DYEHOUSE_RESTOREREVERIFY_CONNECTION");

    private static DbContextOptions<ApplicationDbContext> Options(string connectionString) =>
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(connectionString)
            .Options;

    /// <summary>
    /// Skips the drill unless the runner points at a restored scratch database.
    /// xUnit v2 has no Assert.Skip, so the skip reason lives on a Fact subclass.
    /// </summary>
    private sealed class ScratchDatabaseFactAttribute : FactAttribute
    {
        public ScratchDatabaseFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(ScratchConnection))
                Skip = "Set DYEHOUSE_RESTOREREVERIFY_CONNECTION to a restored scratch database " +
                       "(see docs/operations/backup-and-restore.md) to run the restore verification.";
        }
    }

    private static string RequireScratch() => ScratchConnection!;

    [ScratchDatabaseFact]
    public async Task Restored_Fabric_Ledger_Matches_Application_Balances()
    {
        var connection = RequireScratch();
        await using var db = new ApplicationDbContext(Options(connection));
        var ledger = new InventoryLedgerService(db);

        var keys = await db.InventoryTransactions.AsNoTracking()
            .Where(t => t.RawMessageId != null)
            .Select(t => new { t.WarehouseId, t.ItemId, RawMessageId = t.RawMessageId!.Value })
            .Distinct()
            .Take(MaxKeysPerCheck)
            .ToListAsync();

        foreach (var key in keys)
        {
            var app = await ledger.GetMessageBalanceAsync(key.RawMessageId, key.ItemId, key.WarehouseId);

            await using var sql = new SqlConnection(connection);
            await sql.OpenAsync();
            await using var cmd = new SqlCommand(
                "SELECT COALESCE(SUM(QuantityKg * CASE Direction WHEN N'In' THEN 1 ELSE -1 END), 0), " +
                "COALESCE(SUM(QuantityMeter * CASE Direction WHEN N'In' THEN 1 ELSE -1 END), 0) " +
                "FROM InventoryTransactions " +
                "WHERE WarehouseId = @w AND ItemId = @i AND RawMessageId = @m", sql);
            cmd.Parameters.AddWithValue("@w", key.WarehouseId);
            cmd.Parameters.AddWithValue("@i", key.ItemId);
            cmd.Parameters.AddWithValue("@m", key.RawMessageId);
            await using var reader = await cmd.ExecuteReaderAsync();
            await reader.ReadAsync();
            var expected = ((decimal)reader.GetDecimal(0), (decimal)reader.GetDecimal(1));

            app.Should().Be(expected,
                $"fabric balance for message {key.RawMessageId} item {key.ItemId} must match the restored ledger");
        }
    }

    [ScratchDatabaseFact]
    public async Task Restored_Customer_Ledger_Slices_Match_Application_Balances()
    {
        var connection = RequireScratch();
        await using var db = new ApplicationDbContext(Options(connection));
        var ledger = new InventoryLedgerService(db);

        var keys = await db.InventoryTransactions.AsNoTracking()
            .Select(t => new { t.WarehouseId, t.ItemId, t.CustomerId, t.RawMessageId })
            .Distinct()
            .Take(MaxKeysPerCheck)
            .ToListAsync();

        foreach (var key in keys.Where(k => k.RawMessageId.HasValue))
        {
            var app = await ledger.GetCustomerBalanceAsync(
                key.RawMessageId!.Value, key.ItemId, key.CustomerId, key.WarehouseId);

            await using var sql = new SqlConnection(connection);
            await sql.OpenAsync();
            await using var cmd = new SqlCommand(
                "SELECT COALESCE(SUM(QuantityKg * CASE Direction WHEN N'In' THEN 1 ELSE -1 END), 0), " +
                "COALESCE(SUM(QuantityMeter * CASE Direction WHEN N'In' THEN 1 ELSE -1 END), 0) " +
                "FROM InventoryTransactions " +
                "WHERE WarehouseId = @w AND ItemId = @i AND CustomerId = @c AND RawMessageId = @m", sql);
            cmd.Parameters.AddWithValue("@w", key.WarehouseId);
            cmd.Parameters.AddWithValue("@i", key.ItemId);
            cmd.Parameters.AddWithValue("@c", key.CustomerId);
            cmd.Parameters.AddWithValue("@m", key.RawMessageId!.Value);
            await using var reader = await cmd.ExecuteReaderAsync();
            await reader.ReadAsync();
            var expected = ((decimal)reader.GetDecimal(0), (decimal)reader.GetDecimal(1));

            app.Should().Be(expected,
                $"customer slice for customer {key.CustomerId} must match the restored ledger");
        }
    }

    [ScratchDatabaseFact]
    public async Task Restored_Material_Ledger_Matches_Application_Balances()
    {
        var connection = RequireScratch();
        await using var db = new ApplicationDbContext(Options(connection));
        var ledger = new MaterialLedgerService(db);

        var keys = await db.MaterialTransactions.AsNoTracking()
            .Select(t => new { t.WarehouseId, t.MaterialId })
            .Distinct()
            .Take(MaxKeysPerCheck)
            .ToListAsync();

        foreach (var key in keys)
        {
            var app = await ledger.GetBalanceAsync(key.MaterialId, key.WarehouseId);

            await using var sql = new SqlConnection(connection);
            await sql.OpenAsync();
            await using var cmd = new SqlCommand(
                "SELECT COALESCE(SUM(Quantity * CASE Direction WHEN N'In' THEN 1 ELSE -1 END), 0) " +
                "FROM MaterialTransactions WHERE WarehouseId = @w AND MaterialId = @m", sql);
            cmd.Parameters.AddWithValue("@w", key.WarehouseId);
            cmd.Parameters.AddWithValue("@m", key.MaterialId);
            var expected = (decimal)(await cmd.ExecuteScalarAsync())!;

            app.Should().Be(expected,
                $"material balance for material {key.MaterialId} must match the restored ledger");
        }
    }

    [ScratchDatabaseFact]
    public async Task Restored_Money_Ledgers_Match_Application_Sums()
    {
        var connection = RequireScratch();
        await using var db = new ApplicationDbContext(Options(connection));

        // Treasury: the application sums Amount signed by Direction (In = +1, Out = -1).
        var treasury = await db.TreasuryTransactions.AsNoTracking()
            .GroupBy(t => t.TreasuryAccountId)
            .Select(g => new
            {
                AccountId = g.Key,
                Balance = g.Sum(t => t.Amount * (t.Direction == DyeHouseERP.Domain.Entities.TreasuryDirection.In ? 1 : -1))
            })
            .ToListAsync();

        foreach (var row in treasury)
        {
            await using var sql = new SqlConnection(connection);
            await sql.OpenAsync();
            await using var cmd = new SqlCommand(
                "SELECT COALESCE(SUM(Amount * CASE Direction WHEN N'In' THEN 1 ELSE -1 END), 0) " +
                "FROM TreasuryTransactions WHERE TreasuryAccountId = @a", sql);
            cmd.Parameters.AddWithValue("@a", row.AccountId);
            var expected = (decimal)(await cmd.ExecuteScalarAsync())!;

            row.Balance.Should().Be(expected,
                $"treasury balance for account {row.AccountId} must match the restored ledger");
        }

        // Customer + supplier ledgers: Debit - Credit per party.
        var customers = await db.Set<DyeHouseERP.Domain.Entities.CustomerLedgerEntry>().AsNoTracking()
            .GroupBy(e => e.CustomerId)
            .Select(g => new { CustomerId = g.Key, Balance = g.Sum(e => e.Debit - e.Credit) })
            .ToListAsync();

        foreach (var row in customers)
        {
            await using var sql = new SqlConnection(connection);
            await sql.OpenAsync();
            await using var cmd = new SqlCommand(
                "SELECT COALESCE(SUM(Debit - Credit), 0) FROM CustomerLedgerEntries WHERE CustomerId = @c", sql);
            cmd.Parameters.AddWithValue("@c", row.CustomerId);
            var expected = (decimal)(await cmd.ExecuteScalarAsync())!;

            row.Balance.Should().Be(expected,
                $"customer ledger balance for customer {row.CustomerId} must match the restored ledger");
        }
    }
}
