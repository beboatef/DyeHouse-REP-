using DyeHouseERP.Application.Common.Exceptions;
using DyeHouseERP.Application.MaterialSales.Commands;
using DyeHouseERP.Application.Supplies.Commands;
using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Enums;
using DyeHouseERP.Domain.Exceptions;
using DyeHouseERP.Persistence;
using DyeHouseERP.Persistence.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace DyeHouseERP.UnitTests;

/// <summary>
/// H-2e / H-2f regression cover. A line APPENDED to a supply issue or a material sale that ALREADY exists
/// must be INSERTed. The defect was the same one H-2d had on purchase orders: the new line carries the key it
/// was created with, so EF change detection took it for an existing row and saved an UPDATE against a row that
/// was never inserted - SaveChangesAsync threw DbUpdateConcurrencyException (HTTP 409) and the line was lost.
///
/// Handler-level tests against a real DbContext on purpose: change-tracker state is invisible to a domain test,
/// which is exactly why the defect survived the suite.
/// </summary>
public class SupplyIssueAndMaterialSaleLineInsertTests
{
    private readonly DbContextOptions<ApplicationDbContext> _options;

    public SupplyIssueAndMaterialSaleLineInsertTests()
    {
        _options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"supply-sale-line-insert-{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
    }

    private ApplicationDbContext NewDb() => new(_options);

    /// <summary>An operating-supply material with real ledger balance, and a supply issue already in the store.</summary>
    private static async Task<(Guid IssueId, Guid MaterialId, Guid OtherMaterialId)> SeedPersistedIssueAsync(
        ApplicationDbContext db, decimal stock = 100m)
    {
        var warehouse = new Warehouse("SUPPLIES", "Supplies Store", WarehouseKind.OperatingSupplies, "tester");
        var material = new Material("MAT-S1", "Winding tape", MaterialUnit.KG, 10m, "tester", MaterialKind.OperatingSupply);
        var other = new Material("MAT-S2", "Packaging", MaterialUnit.KG, 4m, "tester", MaterialKind.OperatingSupply);
        db.AddRange(warehouse, material, other);
        await db.SaveChangesAsync();

        // The material ledger is the source of truth for the balance the handler checks.
        db.MaterialTransactions.Add(new MaterialTransaction(
            DocumentType.PurchaseOrder, "PO-SEED", Guid.NewGuid(), DateTime.UtcNow,
            material.Id, warehouse.Id, null, stock, MaterialTransactionDirection.In, 10m, "tester"));
        db.MaterialTransactions.Add(new MaterialTransaction(
            DocumentType.PurchaseOrder, "PO-SEED", Guid.NewGuid(), DateTime.UtcNow,
            other.Id, warehouse.Id, null, stock, MaterialTransactionDirection.In, 4m, "tester"));
        await db.SaveChangesAsync();

        var issue = await new CreateSupplyIssueCommandHandler(
                db, new StubCurrentUser("tester"), new FixedNumberGenerator())
            .Handle(new CreateSupplyIssueCommand(DateTime.UtcNow, warehouse.Id, "Maintenance", "Spare parts"),
                CancellationToken.None);

        return (issue.Id, material.Id, other.Id);
    }

    /// <summary>A chemical with real ledger balance, and a material sale already in the store.</summary>
    private static async Task<(Guid SaleId, Guid MaterialId)> SeedPersistedSaleAsync(
        ApplicationDbContext db, decimal stock = 100m)
    {
        var warehouse = new Warehouse("MATERIALS", "Materials Store", WarehouseKind.Materials, "tester");
        var material = new Material("MAT-C1", "Dye Red", MaterialUnit.KG, 25m, "tester");
        db.AddRange(warehouse, material);
        await db.SaveChangesAsync();

        db.MaterialTransactions.Add(new MaterialTransaction(
            DocumentType.PurchaseOrder, "PO-SEED", Guid.NewGuid(), DateTime.UtcNow,
            material.Id, warehouse.Id, null, stock, MaterialTransactionDirection.In, 25m, "tester"));
        await db.SaveChangesAsync();

        var sale = await new CreateMaterialSaleCommandHandler(
                db, new StubCurrentUser("tester"), new FixedNumberGenerator())
            .Handle(new CreateMaterialSaleCommand(DateTime.UtcNow, warehouse.Id, "Walk-in buyer"),
                CancellationToken.None);

        return (sale.Id, material.Id);
    }

    private static Task AddIssueLineAsync(ApplicationDbContext db, Guid issueId, Guid materialId, decimal quantity = 5m)
        => new AddSupplyIssueLineCommandHandler(db, new MaterialLedgerService(db))
            .Handle(new AddSupplyIssueLineCommand(issueId, materialId, quantity), CancellationToken.None);

    private static Task AddSaleLineAsync(ApplicationDbContext db, Guid saleId, Guid materialId, decimal quantity = 5m)
        => new AddMaterialSaleLineCommandHandler(db, new MaterialLedgerService(db))
            .Handle(new AddMaterialSaleLineCommand(saleId, materialId, quantity), CancellationToken.None);

    // ---------------------------------------------------------------- H-2e

    [Fact]
    public async Task AddingALineToAPersistedSupplyIssue_InsertsItExactlyOnce()
    {
        using var db = NewDb();
        var (issueId, materialId, _) = await SeedPersistedIssueAsync(db);
        (await db.SupplyIssueLines.CountAsync(l => l.SupplyIssueId == issueId))
            .Should().Be(0, "the issue was created without lines");

        await AddIssueLineAsync(db, issueId, materialId, 5m);

        var lines = await db.SupplyIssueLines.Where(l => l.SupplyIssueId == issueId).ToListAsync();
        lines.Should().HaveCount(1, "the new line must be INSERTed - it used to be saved as an UPDATE and lost");
        lines[0].MaterialId.Should().Be(materialId);
        lines[0].Quantity.Should().Be(5m);
    }

    [Fact]
    public async Task AddingSupplyLinesInSeparateEdits_StoresEachLineExactlyOnce()
    {
        using var db = NewDb();
        var (issueId, materialId, otherMaterialId) = await SeedPersistedIssueAsync(db);

        await AddIssueLineAsync(db, issueId, materialId, 5m);
        var first = (await db.SupplyIssueLines.Where(l => l.SupplyIssueId == issueId).ToListAsync()).Single();

        await AddIssueLineAsync(db, issueId, otherMaterialId, 7m);

        var lines = await db.SupplyIssueLines.Where(l => l.SupplyIssueId == issueId).ToListAsync();
        lines.Should().HaveCount(2, "each edit inserts exactly its own line - nothing is duplicated");
        lines.Select(l => l.Id).Should().OnlyHaveUniqueItems();
        lines.Should().Contain(l => l.Id == first.Id && l.Quantity == 5m,
            "the line added by the earlier edit is neither rewritten nor lost");
        (await db.SupplyIssueLines.CountAsync()).Should().Be(2, "no duplicate line rows anywhere");
    }

    [Fact]
    public async Task AddingASupplyLineToAnUnknownIssue_IsRefused()
    {
        using var db = NewDb();
        var (_, materialId, _) = await SeedPersistedIssueAsync(db);

        var act = () => AddIssueLineAsync(db, Guid.NewGuid(), materialId);

        await act.Should().ThrowAsync<NotFoundException>();
        (await db.SupplyIssueLines.CountAsync()).Should().Be(0, "a refused request writes nothing");
    }

    [Fact]
    public async Task AddingASupplyLineBeyondTheLedgerBalance_IsRefused()
    {
        using var db = NewDb();
        var (issueId, materialId, _) = await SeedPersistedIssueAsync(db, stock: 10m);

        var act = () => AddIssueLineAsync(db, issueId, materialId, 50m);

        await act.Should().ThrowAsync<NegativeStockException>();
        (await db.SupplyIssueLines.CountAsync()).Should().Be(0, "the rejection happens before anything is written");
    }

    [Fact]
    public async Task AddingAProductionChemicalToASupplyIssue_IsRefused()
    {
        using var db = NewDb();
        var (issueId, _, _) = await SeedPersistedIssueAsync(db);

        var warehouse = await db.Warehouses.SingleAsync();
        var chemical = new Material("MAT-CH1", "Caustic", MaterialUnit.KG, 8m, "tester");
        db.Add(chemical);
        await db.SaveChangesAsync();
        db.MaterialTransactions.Add(new MaterialTransaction(
            DocumentType.PurchaseOrder, "PO-SEED", Guid.NewGuid(), DateTime.UtcNow,
            chemical.Id, warehouse.Id, null, 100m, MaterialTransactionDirection.In, 8m, "tester"));
        await db.SaveChangesAsync();

        var act = () => AddIssueLineAsync(db, issueId, chemical.Id);

        await act.Should().ThrowAsync<DomainException>();
        (await db.SupplyIssueLines.CountAsync()).Should().Be(0);
    }

    // ---------------------------------------------------------------- H-2f

    [Fact]
    public async Task AddingALineToAPersistedMaterialSale_InsertsItExactlyOnce()
    {
        using var db = NewDb();
        var (saleId, materialId) = await SeedPersistedSaleAsync(db);
        (await db.MaterialSaleLines.CountAsync(l => l.MaterialSaleId == saleId))
            .Should().Be(0, "the sale was created without lines");

        await AddSaleLineAsync(db, saleId, materialId, 3m);

        var lines = await db.MaterialSaleLines.Where(l => l.MaterialSaleId == saleId).ToListAsync();
        lines.Should().HaveCount(1, "the new line must be INSERTed - it used to be saved as an UPDATE and lost");
        lines[0].MaterialId.Should().Be(materialId);
        lines[0].Quantity.Should().Be(3m);
    }

    [Fact]
    public async Task AddingMaterialSaleLinesInSeparateEdits_StoresEachLineExactlyOnce()
    {
        using var db = NewDb();
        var (saleId, materialId) = await SeedPersistedSaleAsync(db);

        await AddSaleLineAsync(db, saleId, materialId, 3m);
        await AddSaleLineAsync(db, saleId, materialId, 4m);

        var lines = await db.MaterialSaleLines.Where(l => l.MaterialSaleId == saleId).ToListAsync();
        lines.Should().HaveCount(2, "each edit inserts exactly its own line - nothing is duplicated");
        lines.Select(l => l.Id).Should().OnlyHaveUniqueItems();
        lines.Select(l => l.Quantity).Should().BeEquivalentTo(new[] { 3m, 4m });
        (await db.MaterialSaleLines.CountAsync()).Should().Be(2, "no duplicate line rows anywhere");
    }

    [Fact]
    public async Task AddingAMaterialSaleLineToAnUnknownSale_IsRefused()
    {
        using var db = NewDb();
        var (_, materialId) = await SeedPersistedSaleAsync(db);

        var act = () => AddSaleLineAsync(db, Guid.NewGuid(), materialId);

        await act.Should().ThrowAsync<NotFoundException>();
        (await db.MaterialSaleLines.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task AddingAnOperatingSupplyToAMaterialSale_IsRefused()
    {
        using var db = NewDb();
        var (saleId, _) = await SeedPersistedSaleAsync(db);

        var warehouse = await db.Warehouses.SingleAsync();
        var supply = new Material("MAT-OP1", "Spare part", MaterialUnit.KG, 6m, "tester", MaterialKind.OperatingSupply);
        db.Add(supply);
        await db.SaveChangesAsync();
        db.MaterialTransactions.Add(new MaterialTransaction(
            DocumentType.PurchaseOrder, "PO-SEED", Guid.NewGuid(), DateTime.UtcNow,
            supply.Id, warehouse.Id, null, 100m, MaterialTransactionDirection.In, 6m, "tester"));
        await db.SaveChangesAsync();

        var act = () => AddSaleLineAsync(db, saleId, supply.Id);

        await act.Should().ThrowAsync<DomainException>();
        (await db.MaterialSaleLines.CountAsync()).Should().Be(0);
    }
}
