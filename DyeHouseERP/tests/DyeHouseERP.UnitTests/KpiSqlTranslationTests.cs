using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DyeHouseERP.UnitTests;

/// <summary>
/// The KPI and accounting-export LINQ must run on SQL Server, not just on the
/// InMemory provider used for value tests: unmapped computed properties and
/// client-only constructs throw at runtime on the real database. These tests
/// generate the SQL (ToQueryString never opens a connection) for the risky
/// fragments, mirroring the handler queries, and fail the build if EF Core
/// cannot translate them.
/// </summary>
public class KpiSqlTranslationTests
{
    private static ApplicationDbContext NewSqlDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer("Server=(local);Database=DyeHouseKpiTranslationProof;Trusted_Connection=True;TrustServerCertificate=True")
            .Options;
        return new ApplicationDbContext(options);
    }

    /// <summary>
    /// The treasury running balance (ExportAccountingQuery.BuildTreasuryAsync):
    /// a correlated SUM over the full history ordered by (TransactionDate, Id).
    /// Proves the Guid tiebreak translates instead of throwing at runtime.
    /// </summary>
    [Fact]
    public void TreasuryRunningBalance_TranslatesToSql()
    {
        using var db = NewSqlDb();
        var from = new DateTime(2026, 1, 1);
        var to = new DateTime(2026, 1, 31);

        var query = db.TreasuryTransactions.AsNoTracking()
            .Where(t => t.TransactionDate >= from && t.TransactionDate <= to)
            .Select(t => new
            {
                t.Id,
                RunningBalance = db.TreasuryTransactions
                    .Where(x => x.TreasuryAccountId == t.TreasuryAccountId
                        && (x.TransactionDate < t.TransactionDate
                            || (x.TransactionDate == t.TransactionDate && x.Id.CompareTo(t.Id) <= 0)))
                    .Sum(x => x.Direction == TreasuryDirection.In ? x.Amount : -x.Amount)
            });

        var sql = query.ToQueryString();
        sql.Should().Contain("SUM");
    }

    /// <summary>
    /// The waste aggregation (GetWasteRateKpiQueryHandler): the WasteKg
    /// definition replicated from mapped stream columns. WasteKg itself is an
    /// unmapped computed property, so the query must read the InputKg /
    /// OutputKg columns (the SUM alias matching the DTO name is harmless).
    /// </summary>
    [Fact]
    public void WasteAggregation_TranslatesToSql()
    {
        using var db = NewSqlDb();
        var orderId = Guid.NewGuid();

        var query = db.ProductionOrderStageExecutions.AsNoTracking()
            .Where(e => e.ProductionOrderId == orderId
                && e.InputKg != null && e.OutputKg != null
                && e.InputKg.Value - e.OutputKg.Value - (e.SecondGradeKg ?? 0m) - (e.SeparatesKg ?? 0m) >= 0m)
            .GroupBy(e => e.ProductionOrderId)
            .Select(g => new
            {
                OrderId = g.Key,
                WasteKg = g.Sum(e => e.InputKg!.Value - e.OutputKg!.Value - (e.SecondGradeKg ?? 0m) - (e.SeparatesKg ?? 0m))
            });

        var sql = query.ToQueryString();
        sql.Should().Contain("SUM");
        sql.Should().Contain("InputKg");
    }

    /// <summary>
    /// The payroll run totals (ExportAccountingQuery.BuildPayrollAsync):
    /// GrossPay/NetPay are unmapped computed properties, so the sums expand
    /// them from the mapped pay components.
    /// </summary>
    [Fact]
    public void PayrollTotals_TranslateToSql()
    {
        using var db = NewSqlDb();
        var runId = Guid.NewGuid();

        var query = db.PayrollRunLines.AsNoTracking()
            .Where(l => l.PayrollRunId == runId)
            .GroupBy(l => l.PayrollRunId)
            .Select(g => new
            {
                RunId = g.Key,
                Gross = g.Sum(l => l.BasicSalary + l.Allowances + l.Incentives + l.Bonuses + l.Commissions + l.OvertimeAmount + l.OtherEarnings),
                Net = g.Sum(l => l.BasicSalary + l.Allowances + l.Incentives + l.Bonuses + l.Commissions + l.OvertimeAmount + l.OtherEarnings - l.Deductions - l.Tax - l.Insurance - l.Advances)
            });

        var sql = query.ToQueryString();
        sql.Should().Contain("SUM");
    }
}
