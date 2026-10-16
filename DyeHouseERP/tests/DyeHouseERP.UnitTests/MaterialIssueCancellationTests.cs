using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.Common.Exceptions;
using DyeHouseERP.Application.CostAccounting.Queries;
using DyeHouseERP.Application.Materials.Commands;
using DyeHouseERP.Application.Materials.DTOs;
using DyeHouseERP.Application.Supplies.Commands;
using DyeHouseERP.Application.Supplies.DTOs;
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
        string reason = "weighed wrong", IPeriodCloseService? period = null,
        IAllocationLockService? stockLock = null)
        => new CancelMaterialIssueCommandHandler(
                db, User(), new FixedClock(), period ?? new OpenPeriod(), stockLock ?? new RecordingStockLock())
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

    [Fact]
    public async Task CancellingMaterialIssue_AcquiresMaterialLotLock()
    {
        using var db = NewDb();
        var (material, warehouse, order) = await SeedStockAsync(db);
        var issue = await IssueAsync(db, material, warehouse, order, 10m);
        var stockLock = new RecordingStockLock();

        await CancelAsync(db, issue.Id, "lock check", stockLock: stockLock);

        stockLock.Dimensions.Should().ContainSingle(k =>
            k == StockLockKey.MaterialLot(warehouse.Id, material.Id),
            "cancellation must lock the exact (warehouse, material) dimension that issue commands lock");
    }

    [Fact]
    public async Task ConcurrentCancelMaterialIssue_WhileAnotherCancelCommitsUnderLock_IsRefusedAndDoesNotDoubleCredit()
    {
        using var db = NewDb();
        var (material, warehouse, order) = await SeedStockAsync(db, openingStock: 100m);
        var issue = await IssueAsync(db, material, warehouse, order, 30m);

        // Simulate a competing cancellation committing right as this request acquires the lot lock.
        var competingLock = new CommitOnAcquireLock(async () =>
        {
            using var competingDb = NewDb();
            await CancelAsync(competingDb, issue.Id, "won the race");
        });

        using var loserDb = NewDb();
        var act = () => CancelAsync(loserDb, issue.Id, "lost the race", stockLock: competingLock);

        await act.Should().ThrowAsync<DomainException>()
            .WithMessage("*already cancelled*");

        using var verifyDb = NewDb();
        (await verifyDb.MaterialTransactions.CountAsync(t => t.SourceDocumentId == issue.Id))
            .Should().Be(2, "only one reversal IN row is posted alongside the original OUT row");
        (await new MaterialLedgerService(verifyDb).GetBalanceAsync(material.Id, warehouse.Id))
            .Should().Be(100m, "stock is restored once, never doubled to 130");
    }

    [Fact]
    public async Task ConcurrentCancelMaterialIssue_AndNewIssueOnSameLot_SerializeAndProduceExactNetBalance()
    {
        using var db = NewDb();
        // Start with 100, issue 80 -> remaining 20.
        var (material, warehouse, order) = await SeedStockAsync(db, openingStock: 100m);
        var firstIssue = await IssueAsync(db, material, warehouse, order, 80m);

        // A new issue for 60 (which needs the 80 reversal to succeed) queues behind the cancel lock:
        // when the cancel finishes, the new issue runs against the restored 100 balance, leaving 40.
        var cancelLock = new RecordingStockLock();
        await CancelAsync(db, firstIssue.Id, "wrong batch", stockLock: cancelLock);

        var secondIssueLock = new RecordingStockLock();
        var secondIssue = await new CreateMaterialIssueCommandHandler(
                db, User(), new FixedNumberGenerator(), new MaterialLedgerService(db),
                new FixedClock(), new OpenPeriod(), secondIssueLock)
            .Handle(new CreateMaterialIssueCommand(material.Id, warehouse.Id, order.Id, 60m, null, null),
                CancellationToken.None);

        cancelLock.Dimensions.Single().Resource.Should().Be(secondIssueLock.Dimensions.Single().Resource,
            "cancel and issue lock the identical StockLockKey.MaterialLot resource so they serialize");
        secondIssue.Quantity.Should().Be(60m);
        (await new MaterialLedgerService(db).GetBalanceAsync(material.Id, warehouse.Id))
            .Should().Be(40m, "100 - 80 + 80 (cancel) - 60 (new issue) = 40");
    }

    // --------------------------------------------------------------- SupplyIssue lock & race tests

    private static async Task<(Warehouse warehouse, Material matA, Material matB, SupplyIssueDto posted)> SeedPostedSupplyIssueAsync(
        ApplicationDbContext db, decimal openingA = 100m, decimal openingB = 100m, decimal issueA = 30m, decimal issueB = 20m)
    {
        var warehouse = new Warehouse("SUP-W", "Operating Supplies WH", WarehouseKind.OperatingSupplies, "seed");
        var matA = new Material("SUP-A", "Belt A", MaterialUnit.KG, 10m, "seed", MaterialKind.OperatingSupply);
        var matB = new Material("SUP-B", "Bearing B", MaterialUnit.KG, 15m, "seed", MaterialKind.OperatingSupply);
        db.AddRange(warehouse, matA, matB);
        db.MaterialTransactions.AddRange(
            new MaterialTransaction(DocumentType.PurchaseReceipt, "PR-A", Guid.NewGuid(), new FixedClock().UtcNow,
                matA.Id, warehouse.Id, null, openingA, MaterialTransactionDirection.In, 10m, "seed"),
            new MaterialTransaction(DocumentType.PurchaseReceipt, "PR-B", Guid.NewGuid(), new FixedClock().UtcNow,
                matB.Id, warehouse.Id, null, openingB, MaterialTransactionDirection.In, 15m, "seed"));
        await db.SaveChangesAsync();

        var draft = await new CreateSupplyIssueCommandHandler(db, User(), new FixedNumberGenerator())
            .Handle(new CreateSupplyIssueCommand(new FixedClock().UtcNow, warehouse.Id, "Maintenance", "Repair"), CancellationToken.None);

        var ledger = new MaterialLedgerService(db);
        await new AddSupplyIssueLineCommandHandler(db, ledger)
            .Handle(new AddSupplyIssueLineCommand(draft.Id, matA.Id, issueA), CancellationToken.None);
        await new AddSupplyIssueLineCommandHandler(db, ledger)
            .Handle(new AddSupplyIssueLineCommand(draft.Id, matB.Id, issueB), CancellationToken.None);

        var posted = await new PostSupplyIssueCommandHandler(db, User(), ledger, new FixedClock(), new OpenPeriod(), new RecordingStockLock())
            .Handle(new PostSupplyIssueCommand(draft.Id), CancellationToken.None);

        return (warehouse, matA, matB, posted);
    }

    [Fact]
    public async Task CancellingPostedSupplyIssue_AcquiresMaterialLotLocksForAllLines()
    {
        using var db = NewDb();
        var (warehouse, matA, matB, posted) = await SeedPostedSupplyIssueAsync(db);
        var stockLock = new RecordingStockLock();

        var cancelled = await new CancelSupplyIssueCommandHandler(db, User(), new FixedClock(), new OpenPeriod(), stockLock)
            .Handle(new CancelSupplyIssueCommand(posted.Id, "issued in error"), CancellationToken.None);

        cancelled.Status.Should().Be(SupplyIssueStatus.Cancelled);
        stockLock.Dimensions.Should().HaveCount(2);
        stockLock.Dimensions.Should().Contain(StockLockKey.MaterialLot(warehouse.Id, matA.Id));
        stockLock.Dimensions.Should().Contain(StockLockKey.MaterialLot(warehouse.Id, matB.Id));
    }

    [Fact]
    public async Task ConcurrentCancelSupplyIssue_WhileAnotherCancelCommitsUnderLock_IsRefusedAndDoesNotDoubleCredit()
    {
        using var db = NewDb();
        var (warehouse, matA, matB, posted) = await SeedPostedSupplyIssueAsync(db, openingA: 100m, openingB: 100m, issueA: 30m, issueB: 20m);

        var competingLock = new CommitOnAcquireLock(async () =>
        {
            using var competingDb = NewDb();
            await new CancelSupplyIssueCommandHandler(competingDb, User(), new FixedClock(), new OpenPeriod(), new RecordingStockLock())
                .Handle(new CancelSupplyIssueCommand(posted.Id, "first cancel"), CancellationToken.None);
        });

        using var loserDb = NewDb();
        var act = () => new CancelSupplyIssueCommandHandler(loserDb, User(), new FixedClock(), new OpenPeriod(), competingLock)
            .Handle(new CancelSupplyIssueCommand(posted.Id, "second cancel"), CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>()
            .WithMessage("*already cancelled*");

        using var verifyDb = NewDb();
        var ledger = new MaterialLedgerService(verifyDb);
        (await ledger.GetBalanceAsync(matA.Id, warehouse.Id)).Should().Be(100m, "restored once from 70 to 100");
        (await ledger.GetBalanceAsync(matB.Id, warehouse.Id)).Should().Be(100m, "restored once from 80 to 100");
        (await verifyDb.MaterialTransactions.CountAsync(t => t.SourceDocumentId == posted.Id && t.Direction == MaterialTransactionDirection.In))
            .Should().Be(2, "one reversal IN row per line, never doubled");
    }

    [Fact]
    public async Task ConcurrentCancelSupplyIssue_AndNewPostOnSameMaterial_ShareIdenticalLocksAndProduceExactBalance()
    {
        using var db = NewDb();
        var (warehouse, matA, matB, firstPosted) = await SeedPostedSupplyIssueAsync(db, openingA: 100m, openingB: 100m, issueA: 80m, issueB: 50m);

        // Create a second draft issue needing 70 of matA (only 20 is available before firstPosted is cancelled).
        var secondDraft = await new CreateSupplyIssueCommandHandler(db, User(), new FixedNumberGenerator())
            .Handle(new CreateSupplyIssueCommand(new FixedClock().UtcNow, warehouse.Id, "Production", "Shift supplies"), CancellationToken.None);

        // When firstPosted is cancelled under the lock, matA returns to 100 so secondDraft can add & post 70 -> leaving 30.
        var cancelLock = new RecordingStockLock();
        await new CancelSupplyIssueCommandHandler(db, User(), new FixedClock(), new OpenPeriod(), cancelLock)
            .Handle(new CancelSupplyIssueCommand(firstPosted.Id, "return to store"), CancellationToken.None);

        var ledger = new MaterialLedgerService(db);
        await new AddSupplyIssueLineCommandHandler(db, ledger)
            .Handle(new AddSupplyIssueLineCommand(secondDraft.Id, matA.Id, 70m), CancellationToken.None);

        var postLock = new RecordingStockLock();
        await new PostSupplyIssueCommandHandler(db, User(), ledger, new FixedClock(), new OpenPeriod(), postLock)
            .Handle(new PostSupplyIssueCommand(secondDraft.Id), CancellationToken.None);

        cancelLock.Dimensions.Should().Contain(postLock.Dimensions.Single(),
            "cancel and post lock the same StockLockKey.MaterialLot key");
        (await ledger.GetBalanceAsync(matA.Id, warehouse.Id)).Should().Be(30m, "100 - 80 + 80 (cancel) - 70 (second post) = 30");
    }

    // ------------------------------------------------- genuine cancel-vs-new-posting interleaving

    /// <summary>
    /// The race that matters: a cancellation and a NEW issue of the same (warehouse, material) are in
    /// flight at once. Here the cancellation COMMITS at the exact moment the new issue takes the lot
    /// lock - the interleaving a real SQL Server serializes - so the issue must see the RESTORED
    /// balance and post cleanly. The net balance is then exact and nothing is counted twice.
    ///
    /// (This is not "the lock service was called": the competing write really runs, and the assertion
    /// is on the resulting ledger balance, not on the lock's arguments. What only a SQL Server can add
    /// is that sp_getapplock makes this ordering physically exclusive; see the integration suite.)
    /// </summary>
    [Fact]
    public async Task CancelCommitting_WhileANewIssueTakesTheLotLock_LetsTheIssueSeeTheRestoredBalance()
    {
        using var db = NewDb();
        // 100 in stock, 80 issued -> 20 left, so a new issue of 60 only fits once the 80 comes back.
        var (material, warehouse, order) = await SeedStockAsync(db, openingStock: 100m);
        var firstIssue = await IssueAsync(db, material, warehouse, order, 80m);
        (await new MaterialLedgerService(db).GetBalanceAsync(material.Id, warehouse.Id)).Should().Be(20m);

        var cancelCommitsDuringIssueLock = new CommitOnAcquireLock(async () =>
        {
            using var cancelDb = NewDb();
            await CancelAsync(cancelDb, firstIssue.Id, "released the wrong batch");
        });

        using var issueDb = NewDb();
        var secondIssue = await new CreateMaterialIssueCommandHandler(
                issueDb, User(), new FixedNumberGenerator(), new MaterialLedgerService(issueDb),
                new FixedClock(), new OpenPeriod(), cancelCommitsDuringIssueLock)
            .Handle(new CreateMaterialIssueCommand(material.Id, warehouse.Id, order.Id, 60m, null, null),
                CancellationToken.None);

        secondIssue.Quantity.Should().Be(60m, "the issue runs AFTER the reversal committed, so it fits");

        using var verifyDb = NewDb();
        (await new MaterialLedgerService(verifyDb).GetBalanceAsync(material.Id, warehouse.Id))
            .Should().Be(40m, "100 - 80 + 80 (reversal) - 60 (new issue) = 40, counted exactly once");
        (await verifyDb.MaterialTransactions.CountAsync(t => t.SourceDocumentId == firstIssue.Id))
            .Should().Be(2, "the cancellation reversed once, never twice");
    }

    /// <summary>
    /// The SAME race in the other order: the new issue takes the lot lock FIRST, so the reversal has
    /// not committed yet. The issue must be refused as negative stock - it may never borrow a reversal
    /// that is not in the ledger yet - and the cancellation afterwards still restores the stock once.
    /// </summary>
    [Fact]
    public async Task NewIssueRunningFirst_IsRefused_AndTheLaterCancelRestoresExactlyOnce()
    {
        using var db = NewDb();
        var (material, warehouse, order) = await SeedStockAsync(db, openingStock: 100m);
        var firstIssue = await IssueAsync(db, material, warehouse, order, 80m);

        using var issueDb = NewDb();
        var act = () => new CreateMaterialIssueCommandHandler(
                issueDb, User(), new FixedNumberGenerator(), new MaterialLedgerService(issueDb),
                new FixedClock(), new OpenPeriod(), new RecordingStockLock())
            .Handle(new CreateMaterialIssueCommand(material.Id, warehouse.Id, order.Id, 60m, null, null),
                CancellationToken.None);

        (await act.Should().ThrowAsync<NegativeStockException>())
            .WithMessage("*negative stock*");

        (await new MaterialLedgerService(issueDb).GetBalanceAsync(material.Id, warehouse.Id))
            .Should().Be(20m, "the refused issue wrote nothing");

        using var cancelDb = NewDb();
        await CancelAsync(cancelDb, firstIssue.Id, "released after the refusal");

        using var verifyDb = NewDb();
        (await new MaterialLedgerService(verifyDb).GetBalanceAsync(material.Id, warehouse.Id))
            .Should().Be(100m, "the reversal restores the full 100 exactly once");
    }

    /// <summary>
    /// The supply-issue equivalent: posting a draft while a cancellation of an EARLIER, already-posted
    /// issue of the same material is in flight. The cancellation commits at the exact moment the post
    /// takes its material locks, so the post's POSTING-TIME re-check sees the restored balance and the
    /// net figure is exact. The draft's line is typed while the store is still full - the line check
    /// against the live balance is a real rule, so the race has to be built the way it really happens.
    /// </summary>
    [Fact]
    public async Task CancelSupplyIssue_CommittingWhileANewPostTakesTheMaterialLocks_SerializesExactly()
    {
        using var db = NewDb();
        var warehouse = new Warehouse("SUP-RACE", "Operating Supplies WH", WarehouseKind.OperatingSupplies, "seed");
        var matA = new Material("RACE-A", "Belt A", MaterialUnit.KG, 10m, "seed", MaterialKind.OperatingSupply);
        db.AddRange(warehouse, matA);
        db.MaterialTransactions.Add(new MaterialTransaction(
            DocumentType.PurchaseReceipt, "PR-RACE", Guid.NewGuid(), new FixedClock().UtcNow,
            matA.Id, warehouse.Id, null, 100m, MaterialTransactionDirection.In, 10m, "seed"));
        await db.SaveChangesAsync();

        var ledger = new MaterialLedgerService(db);

        // Draft 2 is prepared while the store is FULL, so its 70 KG line validates against 100.
        var draft2 = await new CreateSupplyIssueCommandHandler(db, User(), new FixedNumberGenerator())
            .Handle(new CreateSupplyIssueCommand(new FixedClock().UtcNow, warehouse.Id, "Production", "Shift supplies"),
                CancellationToken.None);
        await new AddSupplyIssueLineCommandHandler(db, ledger)
            .Handle(new AddSupplyIssueLineCommand(draft2.Id, matA.Id, 70m), CancellationToken.None);

        // Draft 1 posts 80, leaving only 20: draft 2 can no longer post until the reversal lands.
        var draft1 = await new CreateSupplyIssueCommandHandler(db, User(), new FixedNumberGenerator())
            .Handle(new CreateSupplyIssueCommand(new FixedClock().UtcNow, warehouse.Id, "Maintenance", "Repair"),
                CancellationToken.None);
        await new AddSupplyIssueLineCommandHandler(db, ledger)
            .Handle(new AddSupplyIssueLineCommand(draft1.Id, matA.Id, 80m), CancellationToken.None);
        var posted1 = await new PostSupplyIssueCommandHandler(
                db, User(), ledger, new FixedClock(), new OpenPeriod(), new RecordingStockLock())
            .Handle(new PostSupplyIssueCommand(draft1.Id), CancellationToken.None);

        (await ledger.GetBalanceAsync(matA.Id, warehouse.Id)).Should().Be(20m);

        var cancelCommitsDuringPostLock = new CommitOnAcquireLock(async () =>
        {
            using var cancelDb = NewDb();
            await new CancelSupplyIssueCommandHandler(cancelDb, User(), new FixedClock(), new OpenPeriod(), new RecordingStockLock())
                .Handle(new CancelSupplyIssueCommand(posted1.Id, "return to store"), CancellationToken.None);
        });

        using var postDb = NewDb();
        var postLedger = new MaterialLedgerService(postDb);
        await new PostSupplyIssueCommandHandler(
                postDb, User(), postLedger, new FixedClock(), new OpenPeriod(), cancelCommitsDuringPostLock)
            .Handle(new PostSupplyIssueCommand(draft2.Id), CancellationToken.None);

        (await postLedger.GetBalanceAsync(matA.Id, warehouse.Id))
            .Should().Be(30m, "100 - 80 + 80 (reversal) - 70 (new post) = 30, counted exactly once");

        using var verifyDb = NewDb();
        (await verifyDb.MaterialTransactions.CountAsync(t =>
            t.SourceDocumentId == posted1.Id && t.Direction == MaterialTransactionDirection.In))
            .Should().Be(1, "the posted issue was reversed once, never twice");
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

    private sealed class CommitOnAcquireLock : IAllocationLockService
    {
        private readonly Func<Task> _onAcquire;
        public CommitOnAcquireLock(Func<Task> onAcquire) => _onAcquire = onAcquire;

        public async Task<IAsyncDisposable> AcquireAsync(StockLockKey key, CancellationToken cancellationToken = default)
        {
            await _onAcquire();
            return Noop.Instance;
        }

        public async Task<IAsyncDisposable> AcquireManyAsync(IEnumerable<StockLockKey> keys, CancellationToken cancellationToken = default)
        {
            await _onAcquire();
            return Noop.Instance;
        }

        public async Task<IAsyncDisposable> AcquireNamedAsync(string resource, CancellationToken cancellationToken = default)
        {
            await _onAcquire();
            return Noop.Instance;
        }

        public async Task<IAsyncDisposable> AcquireExclusiveAsync(string resource, CancellationToken cancellationToken = default)
        {
            await _onAcquire();
            return Noop.Instance;
        }

        public async Task<IAsyncDisposable> AcquireSharedAsync(string resource, CancellationToken cancellationToken = default)
        {
            await _onAcquire();
            return Noop.Instance;
        }

        private sealed class Noop : IAsyncDisposable
        {
            public static readonly Noop Instance = new();
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
