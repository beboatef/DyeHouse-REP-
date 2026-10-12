using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.ProductionOrders.Commands;
using DyeHouseERP.Application.ReadyGoods.Commands;
using DyeHouseERP.Domain.Common;
using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Enums;
using DyeHouseERP.Domain.Exceptions;
using DyeHouseERP.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace DyeHouseERP.UnitTests;

/// <summary>
/// S24 - Production Order cancellation. The invariants under test:
///
///   * cancellation RESTORES stock through the append-only ledger - a new
///     compensating IN row per source lot, never an edit of a balance or of an
///     existing row (spec section 18);
///   * the return is the LEDGER-DERIVED outstanding issue (OUT minus earlier
///     pause releases), so a cancellation after a partial pause cannot credit
///     the same material twice;
///   * a repeated or concurrent cancellation is refused and can never post a
///     second reversal set;
///   * KG and Meter stay in their own columns - never converted;
///   * a completed order can never be cancelled, and a cancelled order can
///     never come back (no completion, no further allocation, no ready-goods
///     posting against it).
/// </summary>
public class ProductionOrderCancellationTests
{
    private readonly DbContextOptions<ApplicationDbContext> _options;

    public ProductionOrderCancellationTests()
    {
        _options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"cancel-order-{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
    }

    private ApplicationDbContext NewDb() => new(_options);

    private sealed class Fixture
    {
        public required Customer Customer { get; init; }
        public required Item Item { get; init; }
        public required Warehouse Warehouse { get; init; }
        public required List<RawMessage> Messages { get; init; }
        public required ProductionOrder Order { get; init; }
    }

    /// <summary>
    /// Builds an order issued from <paramref name="lots"/> separate raw messages,
    /// each carrying an IN receipt row and an OUT issue row for this order -
    /// exactly the ledger shape a real allocation leaves behind. lots == 0 seeds
    /// a plain draft with no material at all.
    /// </summary>
    private Fixture Seed(int lots, decimal issuedKgPerLot, decimal? issuedMeterPerLot = null,
        decimal? completedStageOutputKg = null, bool completeOrder = false, decimal? transferOutputKg = null)
    {
        using var db = NewDb();

        var customer = new Customer("C-1", "Customer", "seed");
        var item = new Item("IT-1", "Item", UnitOfMeasure.KG, "seed");
        var warehouse = new Warehouse("RAW", "Raw Material", WarehouseKind.RawMaterial, "seed");

        db.Customers.Add(customer);
        db.Items.Add(item);
        db.Warehouses.Add(warehouse);

        var order = new ProductionOrder(
            "PRD-2026-0001", customer.Id, item.Id, DateTime.UtcNow, "seed",
            requestedQuantityKg: lots == 0 ? null : issuedKgPerLot * lots,
            requestedQuantityMeter: lots == 0 ? null : issuedMeterPerLot * lots);

        db.ProductionOrders.Add(order);

        var formation = new ProductionStageDefinition("PS002", "التشكيل", 2, "seed", isFormationStage: true);
        db.ProductionStageDefinitions.Add(formation);
        order.BuildStageRoute(new[] { formation }, "seed");

        var messages = new List<RawMessage>();
        for (var i = 0; i < lots; i++)
        {
            var message = new RawMessage($"RMSG-{i + 1}", DateTime.UtcNow, customer.Id, warehouse.Id, "seed", "seed");
            message.AddLine(item.Id, 10_000m, null, null, null);
            messages.Add(message);
            db.RawMessages.Add(message);

            // The customer's receipt (the pool the issue draws from).
            db.InventoryTransactions.Add(new InventoryTransaction(
                DocumentType.RawReceiptMessage, message.MessageNumber, message.Id, DateTime.UtcNow,
                warehouse.Id, customer.Id, item.Id, rawMessageId: message.Id, productionOrderId: null,
                quantityKg: 10_000m, quantityMeter: null,
                direction: TransactionDirection.In, createdBy: "seed"));

            var allocation = order.AllocateRaw(message.Id, item.Id, issuedKgPerLot, issuedMeterPerLot, "seed");
            db.RawAllocations.Add(allocation);

            db.InventoryTransactions.Add(new InventoryTransaction(
                DocumentType.ProductionOrder, order.OrderNumber, order.Id, DateTime.UtcNow,
                warehouse.Id, customer.Id, item.Id, rawMessageId: message.Id, productionOrderId: order.Id,
                quantityKg: issuedKgPerLot, quantityMeter: issuedMeterPerLot,
                direction: TransactionDirection.Out, createdBy: "seed"));
        }

        if (lots > 0)
            order.MarkInProduction();

        // Stage history and completion come AFTER the allocations - the real flow
        // issues material first, works it, then closes the order.
        if (completedStageOutputKg.HasValue)
            order.StageExecutions.Single().CloseWithOutput(completedStageOutputKg.Value, null, null, null, null, "operator");
        if (completeOrder)
            order.Complete();

        // A stage transfer, built the way the floor does it and in the SAME unit
        // of work: the current stage closes with its output and only the chosen
        // next stage is activated - the route stays open after it.
        if (transferOutputKg.HasValue)
        {
            var next = new ProductionStageDefinition("PS003", "الصباغة", 3, "seed");
            db.ProductionStageDefinitions.Add(next);

            var current = order.StageExecutions.Single();
            if (current.Status == StageExecutionStatus.InProgress)
                current.CloseWithOutput(transferOutputKg.Value, null, null, null, null, "operator");

            order.ActivateNextStage(next, transferOutputKg.Value, null, "seed");
        }

        db.SaveChanges();

        return new Fixture
        {
            Customer = customer, Item = item, Warehouse = warehouse, Messages = messages, Order = order
        };
    }

    private static CancelProductionOrderCommandHandler CancelHandler(
        ApplicationDbContext db, string user = "supervisor",
        IAllocationLockService? stockLock = null, IPeriodCloseService? period = null)
        => new(db, new StubCurrentUser(user), new FixedClock(), period ?? new OpenPeriod(),
            stockLock ?? new RecordingStockLock());

    private static PauseProductionOrderCommandHandler PauseHandler(ApplicationDbContext db, string user = "supervisor")
        => new(db, new StubCurrentUser(user), new FixedClock(), new OpenPeriod(), new RecordingStockLock());

    /// <summary>The order's own ledger rows (issues, pause releases and cancellations alike).</summary>
    private static IQueryable<InventoryTransaction> OrderRows(ApplicationDbContext db, Guid orderId)
        => db.InventoryTransactions.Where(t => t.ProductionOrderId == orderId);

    /// <summary>IN rows the cancellation itself posted (DocumentType.ProductionOrder, direction In).</summary>
    private static IQueryable<InventoryTransaction> CancelReversals(ApplicationDbContext db, Guid orderId)
        => OrderRows(db, orderId)
            .Where(t => t.SourceDocumentType == DocumentType.ProductionOrder
                && t.Direction == TransactionDirection.In);

    /// <summary>The customer's live balance on one raw lot, summed from ledger rows only.</summary>
    private static decimal RawBalanceKg(ApplicationDbContext db, Warehouse warehouse, Customer customer, Item item, RawMessage message)
        => db.InventoryTransactions
            .Where(t => t.WarehouseId == warehouse.Id && t.CustomerId == customer.Id
                && t.ItemId == item.Id && t.RawMessageId == message.Id)
            .Sum(t => (t.QuantityKg ?? 0m) * (t.Direction == TransactionDirection.In ? 1m : -1m));

    /// <summary>Closes the current stage so the order can be driven into a terminal or late state.</summary>
    private static void CloseStage(ProductionOrder order, decimal outputKg)
        => order.StageExecutions.Single().CloseWithOutput(outputKg, null, null, null, null, "operator");

    /// <summary>Period fake that refuses every posting, mirroring PeriodCloseService's DomainException.</summary>
    private sealed class ClosedPeriod : IPeriodCloseService
    {
        public Task EnsureOpenAsync(DateTime date, CancellationToken cancellationToken = default)
            => throw new DomainException("The accounting period is closed.");

        public Task<IAsyncDisposable> BeginPostingAsync(DateTime date, CancellationToken cancellationToken = default)
            => throw new DomainException("The accounting period is closed.");
    }

    /// <summary>
    /// Runs <paramref name="beforeAcquire"/> the moment the handler asks for its
    /// stock locks - the deterministic shape of "somebody else's request commits
    /// while this one is queueing". The handler must re-read state INSIDE the
    /// lock and refuse to run a second time.
    /// </summary>
    private sealed class RacingLock : IAllocationLockService
    {
        private readonly Func<Task> _beforeAcquire;
        public RacingLock(Func<Task> beforeAcquire) => _beforeAcquire = beforeAcquire;

        public async Task<IAsyncDisposable> AcquireManyAsync(IEnumerable<StockLockKey> keys, CancellationToken cancellationToken = default)
        {
            await _beforeAcquire();
            return Noop.Instance;
        }

        public Task<IAsyncDisposable> AcquireAsync(StockLockKey key, CancellationToken cancellationToken = default)
            => Task.FromResult<IAsyncDisposable>(Noop.Instance);

        public Task<IAsyncDisposable> AcquireNamedAsync(string resource, CancellationToken cancellationToken = default)
            => Task.FromResult<IAsyncDisposable>(Noop.Instance);

        public Task<IAsyncDisposable> AcquireExclusiveAsync(string resource, CancellationToken cancellationToken = default)
            => Task.FromResult<IAsyncDisposable>(Noop.Instance);

        public Task<IAsyncDisposable> AcquireSharedAsync(string resource, CancellationToken cancellationToken = default)
            => Task.FromResult<IAsyncDisposable>(Noop.Instance);

        private sealed class Noop : IAsyncDisposable
        {
            public static readonly Noop Instance = new();
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    // ------------------------------------------------------- happy path

    [Fact]
    public async Task Cancel_AnAllocatedOrder_ReturnsTheFullOutstandingIssue_WithoutEditingAnyExistingRow()
    {
        var f = Seed(lots: 1, issuedKgPerLot: 800m);
        using var db = NewDb();
        var stockLock = new RecordingStockLock();

        var dto = await CancelHandler(db, stockLock: stockLock).Handle(
            new CancelProductionOrderCommand(f.Order.Id, "customer withdrew the job"), CancellationToken.None);

        dto.Status.Should().Be(ProductionOrderStatus.Cancelled);

        var reversal = await CancelReversals(db, f.Order.Id).SingleAsync();
        reversal.QuantityKg.Should().Be(800m);
        reversal.QuantityMeter.Should().BeNull();
        reversal.RawMessageId.Should().Be(f.Messages[0].Id);
        reversal.SourceDocumentId.Should().Be(f.Order.Id, "the reversal is traceable to the order it reverses");
        reversal.CreatedBy.Should().Be("supervisor");

        // Append-only: the original OUT row is still there, untouched.
        (await OrderRows(db, f.Order.Id).CountAsync(t => t.Direction == TransactionDirection.Out))
            .Should().Be(1, "cancellation posts a compensating row - it never edits or deletes the original");

        // The customer's raw balance is back to exactly what was received.
        RawBalanceKg(db, f.Warehouse, f.Customer, f.Item, f.Messages[0]).Should().Be(10_000m);

        // The reversal runs under the raw-lot lock every other poster of this
        // dimension uses (R1), so it serializes against allocation/release/adjust.
        stockLock.Dimensions.Should().ContainSingle()
            .Which.RawMessageId.Should().Be(f.Messages[0].Id);

        // Audit: who, when, why, and exactly what went back.
        var audit = await db.AuditLogEntries
            .SingleAsync(a => a.Action == "Cancel" && a.EntityName == "ProductionOrder");
        audit.Reason.Should().Be("customer withdrew the job");
        audit.UserName.Should().Be("supervisor");
        audit.BeforeDataJson.Should().Contain(nameof(ProductionOrderStatus.InProduction));
        audit.AfterDataJson.Should().Contain(nameof(ProductionOrderStatus.Cancelled));
        audit.AfterDataJson.Should().Contain("quantityKg");
    }

    [Fact]
    public async Task Cancel_AfterAPartialPauseRelease_ReturnsOnlyWhatIsStillOutstanding()
    {
        var f = Seed(lots: 1, issuedKgPerLot: 800m);
        using var db = NewDb();

        // The job paused and gave 300 KG back; 500 KG is still held.
        await PauseHandler(db).Handle(
            new PauseProductionOrderCommand(f.Order.Id, "machine breakdown", f.Messages[0].Id, f.Item.Id, ReleaseKg: 300m),
            CancellationToken.None);

        await CancelHandler(db).Handle(
            new CancelProductionOrderCommand(f.Order.Id, "job scrapped"), CancellationToken.None);

        var reversals = await CancelReversals(db, f.Order.Id).ToListAsync();
        reversals.Should().ContainSingle("the cancellation returns the NET claim, once");
        reversals[0].QuantityKg.Should().Be(500m,
            "800 issued minus the 300 the pause already returned - the released 300 must not be credited twice");

        RawBalanceKg(db, f.Warehouse, f.Customer, f.Item, f.Messages[0]).Should().Be(10_000m,
            "10000 received - 800 issued + 300 paused back + 500 cancelled back");
    }

    [Fact]
    public async Task Cancel_FromAPurePause_ReturnsEverythingTheOrderStillHolds()
    {
        var f = Seed(lots: 1, issuedKgPerLot: 800m);
        using var db = NewDb();

        await PauseHandler(db).Handle(
            new PauseProductionOrderCommand(f.Order.Id, "waiting for dye"), CancellationToken.None);

        var dto = await CancelHandler(db).Handle(
            new CancelProductionOrderCommand(f.Order.Id, "order withdrawn"), CancellationToken.None);

        dto.Status.Should().Be(ProductionOrderStatus.Cancelled, "Paused -> Cancelled is a legal transition");
        (await CancelReversals(db, f.Order.Id).SingleAsync()).QuantityKg.Should().Be(800m);
        RawBalanceKg(db, f.Warehouse, f.Customer, f.Item, f.Messages[0]).Should().Be(10_000m);
    }

    [Fact]
    public async Task Cancel_FromSeveralLots_ReturnsEachLotSeparately()
    {
        var f = Seed(lots: 2, issuedKgPerLot: 400m);
        using var db = NewDb();

        await CancelHandler(db).Handle(
            new CancelProductionOrderCommand(f.Order.Id, "cancelled"), CancellationToken.None);

        var reversals = await CancelReversals(db, f.Order.Id).ToListAsync();
        reversals.Should().HaveCount(2, "one compensating row per source lot - lots are never merged");

        foreach (var message in f.Messages)
        {
            var row = reversals.Single(r => r.RawMessageId == message.Id);
            row.QuantityKg.Should().Be(400m);
            row.WarehouseId.Should().Be(f.Warehouse.Id);
            RawBalanceKg(db, f.Warehouse, f.Customer, f.Item, message).Should().Be(10_000m);
        }
    }

    [Fact]
    public async Task Cancel_KeepsKgAndMeterInTheirOwnColumns_AndNeverConverts()
    {
        var f = Seed(lots: 1, issuedKgPerLot: 800m, issuedMeterPerLot: 1200m);
        using var db = NewDb();

        await CancelHandler(db).Handle(
            new CancelProductionOrderCommand(f.Order.Id, "cancelled"), CancellationToken.None);

        var reversal = await CancelReversals(db, f.Order.Id).SingleAsync();
        reversal.QuantityKg.Should().Be(800m);
        reversal.QuantityMeter.Should().Be(1200m, "KG and Meter are independent ledgers - no conversion anywhere");
    }

    [Fact]
    public async Task Cancel_ADraftOrderWithNoMaterial_StillCancels_AndPostsNoStockMovement()
    {
        var f = Seed(lots: 0, issuedKgPerLot: 0m);
        using var db = NewDb();
        var stockLock = new RecordingStockLock();

        var dto = await CancelHandler(db, stockLock: stockLock).Handle(
            new CancelProductionOrderCommand(f.Order.Id, "never started"), CancellationToken.None);

        dto.Status.Should().Be(ProductionOrderStatus.Cancelled);
        (await OrderRows(db, f.Order.Id).CountAsync()).Should().Be(0,
            "there is no material to return, so cancellation posts nothing at all");
        stockLock.Dimensions.Should().BeEmpty("no lot is involved, so no lot is locked");
    }

    // --------------------------------------------------- idempotency

    [Fact]
    public async Task Cancel_Repeated_IsRefused_AndNeverPostsASecondReversal()
    {
        var f = Seed(lots: 1, issuedKgPerLot: 800m);
        using var db = NewDb();

        await CancelHandler(db).Handle(
            new CancelProductionOrderCommand(f.Order.Id, "first"), CancellationToken.None);

        var act = () => CancelHandler(db).Handle(
            new CancelProductionOrderCommand(f.Order.Id, "again"), CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>()
            .WithMessage("*already cancelled*");

        (await CancelReversals(db, f.Order.Id).CountAsync()).Should().Be(1,
            "the repeat must not create a duplicate reversal movement");
        (await db.AuditLogEntries.CountAsync(a => a.Action == "Cancel" && a.EntityName == "ProductionOrder"))
            .Should().Be(1);
        RawBalanceKg(db, f.Warehouse, f.Customer, f.Item, f.Messages[0]).Should().Be(10_000m,
            "a second reversal would push the balance above the receipts");
    }

    [Fact]
    public async Task Cancel_RacingAnotherCancellation_ResultsInExactlyOneReversalSet()
    {
        var f = Seed(lots: 1, issuedKgPerLot: 800m);
        using var db = NewDb();

        // While this handler queues for its stock locks, a second cancellation of
        // the SAME order commits to the database - the shape of two supervisors
        // hitting cancel at the same moment.
        var racingCancelRan = false;
        var racingLock = new RacingLock(async () =>
        {
            racingCancelRan = true;
            using var other = NewDb();
            await CancelHandler(other, "racer").Handle(
                new CancelProductionOrderCommand(f.Order.Id, "cancelled by the other request"), CancellationToken.None);
        });

        var act = () => CancelHandler(db, stockLock: racingLock).Handle(
            new CancelProductionOrderCommand(f.Order.Id, "cancelled by this request"), CancellationToken.None);

        // The handler must re-read the live status INSIDE the lock and refuse.
        await act.Should().ThrowAsync<DomainException>().WithMessage("*already cancelled*");
        racingCancelRan.Should().BeTrue("the test only proves something if the other request actually won");

        (await CancelReversals(db, f.Order.Id).CountAsync()).Should().Be(1,
            "exactly one effective reversal set, however many attempts raced");
        RawBalanceKg(db, f.Warehouse, f.Customer, f.Item, f.Messages[0]).Should().Be(10_000m);
    }

    [Fact]
    public async Task Cancel_InAClosedPeriod_IsRefusedBeforeAnythingIsPosted()
    {
        var f = Seed(lots: 1, issuedKgPerLot: 800m);
        using var db = NewDb();

        var act = () => CancelHandler(db, period: new ClosedPeriod()).Handle(
            new CancelProductionOrderCommand(f.Order.Id, "cancelled"), CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>();

        (await OrderRows(db, f.Order.Id).CountAsync()).Should().Be(1, "only the original OUT row exists");
        (await db.ProductionOrders.SingleAsync(o => o.Id == f.Order.Id)).Status
            .Should().Be(ProductionOrderStatus.InProduction);
    }

    // --------------------------------------- which states may be cancelled

    [Fact]
    public async Task Cancel_ACompletedOrder_IsRefused_AndPostsNothing()
    {
        var f = Seed(lots: 1, issuedKgPerLot: 800m, completedStageOutputKg: 750m, completeOrder: true);
        using var db = NewDb();

        var act = () => CancelHandler(db).Handle(
            new CancelProductionOrderCommand(f.Order.Id, "too late"), CancellationToken.None);

        await act.Should().ThrowAsync<DocumentLockedException>();

        (await CancelReversals(db, f.Order.Id).CountAsync()).Should().Be(0);
        (await db.ProductionOrders.SingleAsync(o => o.Id == f.Order.Id)).Status
            .Should().Be(ProductionOrderStatus.Completed, "an output that already reached ready goods cannot be un-cancelled into raw stock");
    }

    [Fact]
    public async Task Cancel_WithPartialProductionHistory_KeepsTheStageRecordIntact()
    {
        // Stage closed at 700 of an 800 baseline - real partial history.
        var f = Seed(lots: 1, issuedKgPerLot: 800m, completedStageOutputKg: 700m);
        using var db = NewDb();

        var dto = await CancelHandler(db).Handle(
            new CancelProductionOrderCommand(f.Order.Id, "quality failure"), CancellationToken.None);

        dto.Status.Should().Be(ProductionOrderStatus.Cancelled);

        var stage = await db.ProductionOrderStageExecutions
            .AsNoTracking().SingleAsync(s => s.ProductionOrderId == f.Order.Id);
        stage.Status.Should().Be(StageExecutionStatus.Completed, "the stage history is preserved, never rewritten");
        stage.BaselineKg.Should().Be(800m);
        stage.OutputKg.Should().Be(700m);
        stage.LossKg.Should().Be(100m);

        (await CancelReversals(db, f.Order.Id).SingleAsync()).QuantityKg.Should().Be(800m,
            "the ledger return is unaffected by stage figures - it is what the order still holds");
    }

    // --------------------------------------------- a cancelled order is terminal

    [Fact]
    public async Task Cancel_AfterTransferringToAnotherStage_KeepsTheRouteAndReturnsTheIssue()
    {
        // The order was transferred the way the floor does it: the formation
        // stage closed with 780 KG of an 800 KG issue and ONLY the chosen next
        // stage activated, which is still InProgress when the order is cancelled.
        var f = Seed(lots: 1, issuedKgPerLot: 800m, transferOutputKg: 780m);
        using var db = NewDb();

        var dto = await CancelHandler(db).Handle(
            new CancelProductionOrderCommand(f.Order.Id, "stopped after transfer"), CancellationToken.None);

        dto.Status.Should().Be(ProductionOrderStatus.Cancelled);

        // The whole route survives as history - transferred stages are not unwound.
        var stages = await db.ProductionOrderStageExecutions
            .AsNoTracking()
            .Where(s => s.ProductionOrderId == f.Order.Id)
            .OrderBy(s => s.Sequence)
            .ToListAsync();
        stages.Should().HaveCount(2);
        stages[0].Status.Should().Be(StageExecutionStatus.Completed);
        stages[0].OutputKg.Should().Be(780m);
        stages[1].Status.Should().Be(StageExecutionStatus.InProgress);

        // The stock return is driven by the ledger, not by the route.
        (await CancelReversals(db, f.Order.Id).SingleAsync()).QuantityKg.Should().Be(800m);
        RawBalanceKg(db, f.Warehouse, f.Customer, f.Item, f.Messages[0]).Should().Be(10_000m);
    }

    [Fact]
    public async Task ACancelledOrder_CanNeverBeCompleted()
    {
        var f = Seed(lots: 1, issuedKgPerLot: 800m, completedStageOutputKg: 700m);
        using var db = NewDb();

        await CancelHandler(db).Handle(
            new CancelProductionOrderCommand(f.Order.Id, "cancelled"), CancellationToken.None);

        // Via the application command...
        var act = () => new CompleteProductionOrderCommandHandler(db, new StubCurrentUser("operator"))
            .Handle(new CompleteProductionOrderCommand(f.Order.Id, "zero output"), CancellationToken.None);
        await act.Should().ThrowAsync<DocumentLockedException>();

        // ...and at the domain level, where the guard lives.
        var reloaded = await db.ProductionOrders.SingleAsync(o => o.Id == f.Order.Id);
        reloaded.Status.Should().Be(ProductionOrderStatus.Cancelled);
        Action complete = () => reloaded.Complete();
        complete.Should().Throw<DocumentLockedException>("a cancelled order cannot come back");
    }

    [Fact]
    public void ACancelledOrder_CanNeverBeGivenMoreRawMaterial()
    {
        var f = Seed(lots: 1, issuedKgPerLot: 800m);
        using var db = NewDb();

        var reloaded = db.ProductionOrders.Single(o => o.Id == f.Order.Id);
        reloaded.Cancel("cancelled", "supervisor");

        Action allocate = () => reloaded.AllocateRaw(f.Messages[0].Id, f.Item.Id, 100m, null, "operator");
        allocate.Should().Throw<DocumentLockedException>();
    }

    [Fact]
    public async Task ReadyGoodsTransfer_ForACancelledOrder_IsRefused()
    {
        // All stages closed - the only thing stopping a ready-goods posting is
        // the cancellation itself.
        var f = Seed(lots: 1, issuedKgPerLot: 800m, completedStageOutputKg: 700m);
        using var db = NewDb();
        db.Warehouses.Add(new Warehouse("RDY", "Ready Goods", WarehouseKind.ReadyGoods, "seed"));
        await db.SaveChangesAsync();

        await CancelHandler(db).Handle(
            new CancelProductionOrderCommand(f.Order.Id, "cancelled"), CancellationToken.None);

        var readyWarehouse = await db.Warehouses.SingleAsync(w => w.Kind == WarehouseKind.ReadyGoods);
        var act = () => new CreateReadyGoodsTransferCommandHandler(
                db, new StubCurrentUser("operator"), new FixedNumberGenerator(),
                new FixedClock(), new OpenPeriod(), new RecordingStockLock())
            .Handle(new CreateReadyGoodsTransferCommand(
                f.Order.Id, readyWarehouse.Id, 700m, null, null, "post cancelled output",
                StageExecutionId: f.Order.StageExecutions.Single().Id), CancellationToken.None);

        await act.Should().ThrowAsync<DocumentLockedException>();
        (await db.ReadyGoodsTransfers.CountAsync()).Should().Be(0);
        (await db.InventoryTransactions.CountAsync(t =>
            t.SourceDocumentType == DocumentType.ReadyGoodsTransfer && t.ProductionOrderId == f.Order.Id))
            .Should().Be(0, "no ready-goods stock may be created against a dead order");
    }

    // ------------------------------------------------------- validation

    [Fact]
    public void Cancel_RequiresAReason_AndARealOrder()
    {
        var validator = new CancelProductionOrderCommandValidator();

        validator.Validate(new CancelProductionOrderCommand(Guid.NewGuid(), "  "))
            .IsValid.Should().BeFalse("a cancellation must be explained - it is audited");
        validator.Validate(new CancelProductionOrderCommand(Guid.Empty, "reason"))
            .IsValid.Should().BeFalse();
        validator.Validate(new CancelProductionOrderCommand(Guid.NewGuid(), "customer withdrew"))
            .IsValid.Should().BeTrue();
    }
}
