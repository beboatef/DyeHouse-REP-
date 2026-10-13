using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.Common.Exceptions;
using DyeHouseERP.Application.CostAccounting.Queries;
using DyeHouseERP.Application.Materials.Commands;
using DyeHouseERP.Application.Materials.DTOs;
using DyeHouseERP.Application.Warehouses.Commands;
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
/// Material issues to a Job Order are stock postings (spec section 27): creating one
/// writes an OUT row the moment it is saved, and there is no draft state. That means a
/// mistake can never be fixed by editing or deleting the document - the only safe
/// correction is a REVERSAL that keeps the original OUT row for audit and appends an
/// equal-and-opposite IN row, exactly as cancelling a supply issue or a material sale
/// does. These tests pin the whole contract:
///
///   * stock comes back and the OUT row is never touched,
///   * the document is only ever flipped to Cancelled (a second cancellation is refused),
///   * a cancelled issue stops counting toward the order's material cost,
///   * a reason is mandatory, an empty one is refused by the validator,
///   * a closed period and a deactivated warehouse refuse the reversal WHOLE - no half-written rows,
///   * and a missing issue is a NotFound, not a silent no-op.
/// </summary>
public class MaterialIssueCancellationTests
{
    private readonly DbContextOptions<ApplicationDbContext> _options;

    public MaterialIssueCancellationTests()
    {
        _options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"material-issue-cancel-{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
    }

    private ApplicationDbContext NewDb() => new(_options);

    private static StubCurrentUser User() => new("tester");

    // --------------------------------------------------------------- seeding helpers

    private static async Task<(Material material, Warehouse warehouse, ProductionOrder order)> SeedStockAsync(
        ApplicationDbContext db, decimal openingStock = 100m, decimal unitCost = 25m)
    {
        var material = new Material("MAT-1", "Dye Red", MaterialUnit.KG, unitCost, "seed", MaterialKind.Chemical);
        var warehouse = new Warehouse("W-1", "Chemical Store", WarehouseKind.Materials, "seed");
        var customer = new Customer("C-1", "Owner", "seed");
        var item = new Item("IT-1", "Item", UnitOfMeasure.KG, "seed");
        var order = new ProductionOrder("PRD-1", customer.Id, item.Id, new FixedClock().UtcNow, "seed");

        db.AddRange(material, warehouse, customer, item, order);
        db.MaterialTransactions.Add(new MaterialTransaction(
            DocumentType.PurchaseReceipt, "PR-1", Guid.NewGuid(), new FixedClock().UtcNow,
            material.Id, warehouse.Id, null, openingStock, MaterialTransactionDirection.In, unitCost, "seed"));
        await db.SaveChangesAsync();
        return (material, warehouse, order);
    }

    private static Task<MaterialIssueDto> IssueAsync(ApplicationDbContext db, Material material,
        Warehouse warehouse, ProductionOrder order, decimal quantity)
        => new CreateMaterialIssueCommandHandler(db, User(), new FixedNumberGenerator(),
                new MaterialLedgerService(db), new FixedClock(), new OpenPeriod(), new RecordingStockLock())
            .Handle(new CreateMaterialIssueCommand(material.Id, warehouse.Id, order.Id, quantity, null, null),
                CancellationToken.None);

    private static Task<MaterialIssueDto> CancelAsync(ApplicationDbContext db, Guid issueId,
        string reason = "weighed wrong", IPeriodCloseService? period = null)
        => new CancelMaterialIssueCommandHandler(db, User(), new FixedClock(), period ?? new OpenPeriod())
            .Handle(new CancelMaterialIssueCommand(issueId, reason), CancellationToken.None);

    private static Task DeactivateAsync(ApplicationDbContext db, Guid warehouseId)
        => new SetWarehouseActiveCommandHandler(db, User())
            .Handle(new SetWarehouseActiveCommand(warehouseId, false), CancellationToken.None);

    // ------------------------------------------------------------------- the happy path

    [Fact]
    public async Task CancellingAPostedIssue_RestoresStockWithACompensatingInRow_AndKeepsTheOriginalOutRow()
    {
        using var db = NewDb();
        var (material, warehouse, order) = await SeedStockAsync(db);
        var issue = await IssueAsync(db, material, warehouse, order, 10m);

        var balanceAfterIssue = await new MaterialLedgerService(db).GetBalanceAsync(material.Id, warehouse.Id);
        balanceAfterIssue.Should().Be(90m, "the issue posted an OUT row of 10");

        var cancelled = await CancelAsync(db, issue.Id, "typing error");

        cancelled.IsCancelled.Should().BeTrue();
        cancelled.CancellationReason.Should().Be("typing error");

        // The reversal is an appended IN row, not a deletion of the OUT row.
        var rows = await db.MaterialTransactions
            .Where(t => t.SourceDocumentId == issue.Id)
            .ToListAsync();
        rows.Should().HaveCount(2, "one original OUT row and one compensating IN row");
        rows.Should().ContainSingle(r => r.Direction == MaterialTransactionDirection.Out && r.Quantity == 10m,
            "the original OUT row survives untouched for audit");
        var reversal = rows.Single(r => r.Direction == MaterialTransactionDirection.In);
        reversal.Quantity.Should().Be(10m);
        reversal.MaterialId.Should().Be(material.Id);
        reversal.WarehouseId.Should().Be(warehouse.Id);
        reversal.ProductionOrderId.Should().Be(order.Id, "the reversal is charged to the same Job Order");
        reversal.SourceDocumentType.Should().Be(DocumentType.MaterialIssue);
        reversal.SourceDocumentNumber.Should().Be(issue.IssueNumber);

        (await new MaterialLedgerService(db).GetBalanceAsync(material.Id, warehouse.Id))
            .Should().Be(100m, "the reversal returns the stock to its pre-issue level");

        // Audit trail on the document itself.
        var stored = await db.MaterialIssues.SingleAsync();
        stored.Status.Should().Be(MaterialIssueStatus.Cancelled);
        stored.CancelledBy.Should().Be("tester");
        stored.CancelledAtUtc.Should().NotBeNull();
        stored.CancellationReason.Should().Be("typing error");

        // The issue document is never removed.
        (await db.MaterialIssues.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task CancellingACancelledIssue_IsRefused_AndWritesNoSecondReversal()
    {
        using var db = NewDb();
        var (material, warehouse, order) = await SeedStockAsync(db);
        var issue = await IssueAsync(db, material, warehouse, order, 10m);
        await CancelAsync(db, issue.Id);

        var act = () => CancelAsync(db, issue.Id, "again");

        await act.Should().ThrowAsync<DomainException>();
        (await db.MaterialTransactions.CountAsync(t => t.SourceDocumentId == issue.Id))
            .Should().Be(2, "the refused second cancellation writes no third row");
        (await new MaterialLedgerService(db).GetBalanceAsync(material.Id, warehouse.Id))
            .Should().Be(100m, "stock is restored exactly once");
    }

    [Fact]
    public async Task ACancelledIssue_StopsCountingTowardTheProductionOrdersMaterialCost()
    {
        using var db = NewDb();
        var (material, warehouse, order) = await SeedStockAsync(db, unitCost: 25m);
        var issue = await IssueAsync(db, material, warehouse, order, 10m);

        var before = await new GetProductionOrderCostQueryHandler(db)
            .Handle(new GetProductionOrderCostQuery(order.Id), CancellationToken.None);
        before.MaterialCost.Should().Be(250m);

        await CancelAsync(db, issue.Id, "not consumed");

        var after = await new GetProductionOrderCostQueryHandler(db)
            .Handle(new GetProductionOrderCostQuery(order.Id), CancellationToken.None);
        after.MaterialCost.Should().Be(0m, "the reversed issue is excluded from the live cost rollup");
    }

    // ---------------------------------------------------------------- refusal cases

    [Fact]
    public async Task CancellingOnADeactivatedWarehouse_IsRefused_AndLeavesTheDocumentAndLedgerUntouched()
    {
        using var db = NewDb();
        var (material, warehouse, order) = await SeedStockAsync(db);
        var issue = await IssueAsync(db, material, warehouse, order, 10m);
        await DeactivateAsync(db, warehouse.Id);

        // The reversal writes a brand-new IN row, so it may not target a retired warehouse.
        var act = () => CancelAsync(db, issue.Id);

        await act.Should().ThrowAsync<DomainException>();
        (await db.MaterialIssues.SingleAsync()).Status.Should().Be(MaterialIssueStatus.Posted,
            "the refusal is whole: the document is not flipped to Cancelled");
        (await db.MaterialTransactions.CountAsync(t => t.SourceDocumentId == issue.Id))
            .Should().Be(1, "no reversal row was written");
        (await new MaterialLedgerService(db).GetBalanceAsync(material.Id, warehouse.Id))
            .Should().Be(90m, "the balance is exactly where the posting left it");
    }

    [Fact]
    public async Task CancellingInAClosedPeriod_IsRefusedBeforeAnythingIsWritten()
    {
        using var db = NewDb();
        var (material, warehouse, order) = await SeedStockAsync(db);
        var issue = await IssueAsync(db, material, warehouse, order, 10m);

        var act = () => CancelAsync(db, issue.Id, period: new ClosedPeriod());

        await act.Should().ThrowAsync<DomainException>();
        (await db.MaterialIssues.SingleAsync()).Status.Should().Be(MaterialIssueStatus.Posted);
        (await db.MaterialTransactions.CountAsync(t => t.SourceDocumentId == issue.Id))
            .Should().Be(1, "the period guard runs before any ledger row is added");
    }

    [Fact]
    public async Task CancellingAnIssueThatDoesNotExist_IsNotFound()
    {
        using var db = NewDb();
        await SeedStockAsync(db);

        var act = () => CancelAsync(db, Guid.NewGuid());

        await act.Should().ThrowAsync<NotFoundException>();
    }

    // --------------------------------------------------------------- validator rules

    [Fact]
    public void TheCancelValidator_RequiresANonEmptyReason()
    {
        var validator = new CancelMaterialIssueCommandValidator();

        validator.Validate(new CancelMaterialIssueCommand(Guid.NewGuid(), "")).IsValid
            .Should().BeFalse("a reversal without a reason leaves no auditable explanation");
        validator.Validate(new CancelMaterialIssueCommand(Guid.NewGuid(), new string('x', 1001))).IsValid
            .Should().BeFalse("the reason column is capped at 1000 characters");
        validator.Validate(new CancelMaterialIssueCommand(Guid.NewGuid(), "weighed wrong")).IsValid
            .Should().BeTrue();
    }

    /// <summary>Period fake that refuses every posting, mirroring PeriodCloseService's DomainException.</summary>
    private sealed class ClosedPeriod : IPeriodCloseService
    {
        public Task EnsureOpenAsync(DateTime date, CancellationToken cancellationToken = default)
            => throw new DomainException("The accounting period is closed.");

        public Task<IAsyncDisposable> BeginPostingAsync(DateTime date, CancellationToken cancellationToken = default)
            => throw new DomainException("The accounting period is closed.");
    }
}
