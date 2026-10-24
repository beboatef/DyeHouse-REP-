using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.ProductionOrders.Commands;
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
/// The canonical workflow (spec sections 5-6): issuing raw material CREATES the Job Order.
///
/// What is pinned here:
///   * the OUT ledger row, the Job Order and its raw-allocation row are one transaction;
///   * the order's OPENING quantities are the ACTUAL issued ones (never the planned ones);
///   * the order is linked to the issue transaction, and optionally to a Formation
///     Request / basin - a direct issue leaves both planning references null and never
///     fabricates a request;
///   * a retried confirmation is idempotent, concurrent issues cannot overspend, and a
///     failed step rolls everything back;
///   * the old create-then-allocate path can no longer cause a SECOND stock OUT.
/// </summary>
public class IssueRawMaterialWorkflowTests
{
    private readonly DbContextOptions<ApplicationDbContext> _options;
    private const decimal LotReceivedKg = 10_000m;

    public IssueRawMaterialWorkflowTests()
    {
        _options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"raw-issue-{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
    }

    private ApplicationDbContext NewDb() => new(_options);

    private sealed class Scene
    {
        public required Customer Customer { get; init; }
        public required Item Item { get; init; }
        public required Warehouse Warehouse { get; init; }
        public required RawMessage Message { get; init; }
        public required ProductionStageDefinition Formation { get; init; }
    }

    private Scene Seed(ApplicationDbContext db, UnitOfMeasure itemUnit = UnitOfMeasure.KG,
        UnitOfMeasure? secondaryUnit = null, bool includeFormationStage = true)
    {
        var customer = new Customer("C-1", "Customer", "seed");
        var item = new Item("IT-1", "Item", itemUnit, "seed", secondaryUnit: secondaryUnit);
        var warehouse = new Warehouse("RAW", "Raw Material", WarehouseKind.RawMaterial, "seed");
        var formation = new ProductionStageDefinition("PS001", "التشكيل", 1, "seed", isFormationStage: true);

        var message = new RawMessage("RMSG-1", DateTime.UtcNow, customer.Id, warehouse.Id, "seed", "seed");
        message.AddLine(item.Id, LotReceivedKg, null, null, null);

        db.AddRange(customer, item, warehouse, message);
        if (includeFormationStage) db.ProductionStageDefinitions.Add(formation);
        db.InventoryTransactions.Add(new InventoryTransaction(
            DocumentType.RawReceiptMessage, message.MessageNumber, message.Id, DateTime.UtcNow,
            warehouse.Id, customer.Id, item.Id, rawMessageId: message.Id, productionOrderId: null,
            quantityKg: LotReceivedKg, quantityMeter: null, direction: TransactionDirection.In, createdBy: "seed"));
        db.SaveChanges();

        return new Scene { Customer = customer, Item = item, Warehouse = warehouse, Message = message, Formation = formation };
    }

    private static IssueRawMaterialCommandHandler Handler(
        ApplicationDbContext db, IAllocationLockService? stockLock = null) => new(
        db, new StubCurrentUser("operator"), new InventoryLedgerService(db), new FixedNumberGenerator(),
        new FixedClock(), new OpenPeriod(), stockLock ?? new RecordingStockLock());

    private static decimal LotBalanceKg(ApplicationDbContext db, Scene scene) =>
        db.InventoryTransactions
            .Where(t => t.WarehouseId == scene.Warehouse.Id && t.CustomerId == scene.Customer.Id
                && t.ItemId == scene.Item.Id && t.RawMessageId == scene.Message.Id)
            .AsEnumerable()
            .Sum(t => (t.QuantityKg ?? 0m) * (t.Direction == TransactionDirection.In ? 1m : -1m));

    // ---------------------------------------------------------------- workflow

    [Fact]
    public async Task DirectIssue_CreatesTheLedgerMovement_AndTheOrderLinkedToIt_WithNoFormationLinks()
    {
        using var db = NewDb();
        var scene = Seed(db);

        var dto = await Handler(db).Handle(new IssueRawMaterialCommand(
            scene.Customer.Id, scene.Item.Id, scene.Message.Id, DateTime.UtcNow,
            ActualQuantityKg: 108m, ActualQuantityMeter: null, Notes: "direct issue"), CancellationToken.None);

        // The frontend gets the created order back with its id and document number.
        dto.Id.Should().NotBeEmpty();
        dto.OrderNumber.Should().NotBeNullOrWhiteSpace("the created order's document number is returned to the caller");
        dto.RequestedQuantityKg.Should().Be(108m);
        dto.Status.Should().Be(ProductionOrderStatus.RawAllocated,
            "raw has been issued but no stage movement exists yet - this is the case-B state");

        // Direct issue: NO fake Formation Request and NO basin are attached.
        var order = await db.ProductionOrders.SingleAsync(o => o.Id == dto.Id);
        order.FormationRequestId.Should().BeNull();
        order.FormationBasinId.Should().BeNull();
        order.SourceIssueTransactionId.Should().NotBeNull();

        // The OUT movement is the issue, traceable to the order and the customer's lot.
        var outRow = await db.InventoryTransactions.SingleAsync(t => t.Direction == TransactionDirection.Out);
        outRow.SourceDocumentType.Should().Be(DocumentType.ProductionOrder);
        outRow.SourceDocumentId.Should().Be(order.Id);
        outRow.RawMessageId.Should().Be(scene.Message.Id);
        outRow.QuantityKg.Should().Be(108m);
        outRow.QuantityMeter.Should().BeNull();
        order.SourceIssueTransactionId.Should().Be(outRow.Id, "the order is linked to the issue that created it");

        // The issue IS the allocation, so the existing 100% rule is satisfied by design.
        (await db.RawAllocations.CountAsync(a => a.ProductionOrderId == order.Id)).Should().Be(1);
        LotBalanceKg(db, scene).Should().Be(LotReceivedKg - 108m);
    }

    [Fact]
    public async Task Issue_From_A_Formation_Request_Links_Request_Basin_And_Issue()
    {
        using var db = NewDb();
        var scene = Seed(db);

        var request = new FormationRequest("FRM-1", DateTime.UtcNow, scene.Customer.Id, scene.Item.Id,
            UnitOfMeasure.KG, "planner");
        var group = request.AddGroup("Group 1", 120m, UnitOfMeasure.KG, 2, "Red", null, "planner");
        var basin = request.AddBasin(group.Id, "Basin 1", 100m, UnitOfMeasure.KG, 1, "Red", null, "planner");
        db.FormationRequests.Add(request);
        await db.SaveChangesAsync();

        // The formation request is planning-only: creating it never touched the ledger.
        (await db.InventoryTransactions.CountAsync(t => t.ProductionOrderId != null)).Should().Be(0);

        var dto = await Handler(db).Handle(new IssueRawMaterialCommand(
            scene.Customer.Id, scene.Item.Id, scene.Message.Id, DateTime.UtcNow,
            ActualQuantityKg: 108m, ActualQuantityMeter: null,
            FormationRequestId: request.Id, FormationBasinId: basin.Id), CancellationToken.None);

        var order = await db.ProductionOrders.SingleAsync(o => o.Id == dto.Id);
        order.FormationRequestId.Should().Be(request.Id);
        order.FormationBasinId.Should().Be(basin.Id);
        order.SourceIssueTransactionId.Should().NotBeNull();
    }

    [Fact]
    public async Task The_Order_Opening_Quantity_Is_The_ACTUAL_Issued_Quantity_Not_The_Planned()
    {
        using var db = NewDb();
        var scene = Seed(db);

        var request = new FormationRequest("FRM-2", DateTime.UtcNow, scene.Customer.Id, scene.Item.Id,
            UnitOfMeasure.KG, "planner");
        var group = request.AddGroup("Group 1", 100m, UnitOfMeasure.KG, 1, null, null, "planner");
        var basin = request.AddBasin(group.Id, "Basin 1", 100m, UnitOfMeasure.KG, 1, null, null, "planner");
        db.FormationRequests.Add(request);
        await db.SaveChangesAsync();

        // Planned 100 KG / actual 108 KG: no tolerance, no rounding, no inference.
        var dto = await Handler(db).Handle(new IssueRawMaterialCommand(
            scene.Customer.Id, scene.Item.Id, scene.Message.Id, DateTime.UtcNow,
            ActualQuantityKg: 108m, ActualQuantityMeter: null,
            FormationRequestId: request.Id, FormationBasinId: basin.Id), CancellationToken.None);

        dto.RequestedQuantityKg.Should().Be(108m, "the order starts from the ACTUAL issued weight, never the planned one");

        // The planned figure stays available on the planning document, untouched.
        var storedBasin = await db.FormationBasins.AsNoTracking().SingleAsync(b => b.Id == basin.Id);
        storedBasin.PlannedQuantity.Should().Be(100m);
    }

    [Fact]
    public async Task Advancing_The_Order_Does_Not_Deduct_The_Raw_Material_Again()
    {
        using var db = NewDb();
        var scene = Seed(db);

        var dto = await Handler(db).Handle(new IssueRawMaterialCommand(
            scene.Customer.Id, scene.Item.Id, scene.Message.Id, DateTime.UtcNow,
            ActualQuantityKg: 108m, ActualQuantityMeter: null), CancellationToken.None);

        var nextStage = new ProductionStageDefinition("PS002", "الطباعة", 2, "seed");
        db.ProductionStageDefinitions.Add(nextStage);
        await db.SaveChangesAsync();

        var stageId = await db.ProductionOrderStageExecutions
            .Where(s => s.ProductionOrderId == dto.Id).Select(s => s.Id).SingleAsync();

        // Phase 4: the operation is advanced and closed by the single TRANSFER transaction - the legacy
        // start/complete/skip paths were deleted - so that is the production step that must not deduct.
        await new TransferToNextStageCommandHandler(
                db, new StubCurrentUser("operator"), new FixedClock(), new OpenPeriod(), new FixedNumberGenerator(), new RecordingStockLock())
            .Handle(new TransferToNextStageCommand(stageId, nextStage.Id, 108m, null, null, null, null),
                CancellationToken.None);

        (await db.InventoryTransactions.CountAsync(t => t.ProductionOrderId == dto.Id))
            .Should().Be(1, "the issue was the only deduction - advancing production never deducts again");
    }

    // ------------------------------------------------- idempotency / rollback

    [Fact]
    public async Task A_Retried_Confirmation_With_The_Same_RequestId_Creates_No_Duplicate_Issue_Or_Order()
    {
        using var db = NewDb();
        var scene = Seed(db);
        var requestId = Guid.NewGuid();

        var command = new IssueRawMaterialCommand(
            scene.Customer.Id, scene.Item.Id, scene.Message.Id, DateTime.UtcNow,
            ActualQuantityKg: 108m, ActualQuantityMeter: null, RequestId: requestId);

        var first = await Handler(db).Handle(command, CancellationToken.None);
        var second = await Handler(db).Handle(command, CancellationToken.None);

        second.Id.Should().Be(first.Id, "a retried confirmation returns the SAME order");
        (await db.ProductionOrders.CountAsync()).Should().Be(1);
        (await db.InventoryTransactions.CountAsync(t => t.Direction == TransactionDirection.Out))
            .Should().Be(1, "the customer's stock is deducted exactly once");
        LotBalanceKg(db, scene).Should().Be(LotReceivedKg - 108m);
    }

    [Fact]
    public async Task Issuing_A_Quantity_In_An_Unconfigured_Unit_Leaves_No_Partial_Effects()
    {
        using var db = NewDb();
        var scene = Seed(db); // KG-only item

        var act = () => Handler(db).Handle(new IssueRawMaterialCommand(
            scene.Customer.Id, scene.Item.Id, scene.Message.Id, DateTime.UtcNow,
            ActualQuantityKg: null, ActualQuantityMeter: 372m), CancellationToken.None);

        (await act.Should().ThrowAsync<DomainException>()).WithMessage("*not configured to use Meter*");

        (await db.ProductionOrders.CountAsync()).Should().Be(0, "no order without its issue");
        (await db.InventoryTransactions.CountAsync(t => t.Direction == TransactionDirection.Out)).Should().Be(0,
            "no OUT movement without its order");
        LotBalanceKg(db, scene).Should().Be(LotReceivedKg);
    }

    [Fact]
    public async Task A_Failed_Order_Creation_Rolls_The_Out_Row_Back()
    {
        using var db = NewDb();
        // No formation stage is configured, so building the order's route fails.
        var scene = Seed(db, includeFormationStage: false);

        var act = () => Handler(db).Handle(new IssueRawMaterialCommand(
            scene.Customer.Id, scene.Item.Id, scene.Message.Id, DateTime.UtcNow,
            ActualQuantityKg: 108m, ActualQuantityMeter: null), CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>();

        (await db.ProductionOrders.CountAsync()).Should().Be(0);
        (await db.InventoryTransactions.CountAsync(t => t.Direction == TransactionDirection.Out))
            .Should().Be(0, "the OUT row must never survive without its order");
        (await db.RawAllocations.CountAsync()).Should().Be(0);
        LotBalanceKg(db, scene).Should().Be(LotReceivedKg);
    }

    [Fact]
    public async Task Issuing_Beyond_The_Lot_Without_An_Override_Is_Refused_And_Posts_Nothing()
    {
        using var db = NewDb();
        var scene = Seed(db);

        var act = () => Handler(db).Handle(new IssueRawMaterialCommand(
            scene.Customer.Id, scene.Item.Id, scene.Message.Id, DateTime.UtcNow,
            ActualQuantityKg: 12_000m, ActualQuantityMeter: null), CancellationToken.None);

        await act.Should().ThrowAsync<NegativeStockException>();
        (await db.ProductionOrders.CountAsync()).Should().Be(0);
        (await db.InventoryTransactions.CountAsync(t => t.Direction == TransactionDirection.Out)).Should().Be(0);
    }

    // ----------------------------------------------- no duplicate deduction

    [Fact]
    public async Task AnIssuedOrder_IsDeductedExactlyOnce_AndNoAllocationStepExistsToDeductItAgain()
    {
        using var db = NewDb();
        var scene = Seed(db);

        await Handler(db).Handle(new IssueRawMaterialCommand(
            scene.Customer.Id, scene.Item.Id, scene.Message.Id, DateTime.UtcNow,
            ActualQuantityKg: 108m, ActualQuantityMeter: null), CancellationToken.None);

        // The issue IS the allocation: exactly one OUT row and one allocation row, committed together.
        (await db.InventoryTransactions.CountAsync(t => t.Direction == TransactionDirection.Out))
            .Should().Be(1, "the material is deducted exactly once");
        (await db.RawAllocations.CountAsync()).Should().Be(1, "the issue records the order's single allocation");
        LotBalanceKg(db, scene).Should().Be(LotReceivedKg - 108m);

        // The old order-first-then-allocate route is REMOVED, not merely hidden: with no allocation command,
        // validator or handler left in the application layer there is no code path that could post a SECOND
        // deduction for material this order has already been debited for - which is exactly what the old
        // endpoint allowed on a legacy order that carried no issue link.
        var applicationTypeNames = typeof(IssueRawMaterialCommand).Assembly.GetTypes()
            .Select(t => t.Name).ToList();

        applicationTypeNames.Should().NotContain("AllocateRawCommand");
        applicationTypeNames.Should().NotContain("AllocateRawCommandValidator");
        applicationTypeNames.Should().NotContain("AllocateRawCommandHandler");
    }

    // ------------------------------------------------------------- concurrency

    /// <summary>Runs another request the moment this handler asks for its stock lock.</summary>
    private sealed class RacingIssueLock : IAllocationLockService
    {
        private readonly Func<Task> _beforeAcquire;
        public RacingIssueLock(Func<Task> beforeAcquire) => _beforeAcquire = beforeAcquire;

        public async Task<IAsyncDisposable> AcquireAsync(StockLockKey key, CancellationToken cancellationToken = default)
        {
            await _beforeAcquire();
            return Noop.Instance;
        }

        public Task<IAsyncDisposable> AcquireManyAsync(IEnumerable<StockLockKey> keys, CancellationToken cancellationToken = default)
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

    [Fact]
    public async Task Concurrent_Issues_Cannot_Overspend_The_Lot()
    {
        using var db = NewDb();
        var scene = Seed(db);

        // While this request queues for the lot lock, another issue of 9_500 KG commits.
        var racingLock = new RacingIssueLock(async () =>
        {
            using var other = NewDb();
            await Handler(other).Handle(new IssueRawMaterialCommand(
                scene.Customer.Id, scene.Item.Id, scene.Message.Id, DateTime.UtcNow,
                ActualQuantityKg: 9_500m, ActualQuantityMeter: null, Notes: "the other request"), CancellationToken.None);
        });

        // 9_500 + 9_500 is more than the 10_000 the customer owns: the second must be refused.
        var act = () => Handler(db, racingLock).Handle(new IssueRawMaterialCommand(
            scene.Customer.Id, scene.Item.Id, scene.Message.Id, DateTime.UtcNow,
            ActualQuantityKg: 9_500m, ActualQuantityMeter: null, Notes: "this request"), CancellationToken.None);

        await act.Should().ThrowAsync<NegativeStockException>();

        (await db.ProductionOrders.CountAsync()).Should().Be(1, "only the winning issue produced an order");
        (await db.InventoryTransactions.CountAsync(t => t.Direction == TransactionDirection.Out)).Should().Be(1);
        LotBalanceKg(db, scene).Should().Be(500m, "the lot is never overspent");
    }

    // ----------------------------------------------------------- validation

    [Fact]
    public void Issue_Requires_An_Actual_Quantity()
    {
        var validator = new IssueRawMaterialCommandValidator();

        validator.Validate(new IssueRawMaterialCommand(
                Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow, null, null)).IsValid
            .Should().BeFalse("the actual issued quantity is the order's opening quantity - it cannot be omitted");

        validator.Validate(new IssueRawMaterialCommand(
                Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow, 108m, null)).IsValid
            .Should().BeTrue();
    }
}
