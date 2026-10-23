using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.ProductionOrders.Commands;
using DyeHouseERP.Application.ReadyGoods.Commands;
using DyeHouseERP.Domain.Common;
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
/// H1 at the HANDLER level: the rule is enforced by the backend, not by the screen.
///
/// <see cref="RawAllocationGateTests"/> pins the decision; these tests pin that every
/// path that can start production, record stage output, post ready-goods stock or
/// complete the order actually CONSULTS it - and that a refusal leaves no document,
/// no IN movement, no separates row and no status change behind:
///
///   start stage / transfer to the next stage / transfer to the ready-goods stage /
///   complete stage / edit or skip a stage / manual ready-goods transfer /
///   complete order / resume.
///
/// The acceptance table is exercised literally: 0% and 50% are refused, 100% proceeds.
/// </summary>
public class RawAllocationGateHandlerTests
{
    private readonly DbContextOptions<ApplicationDbContext> _options;

    public RawAllocationGateHandlerTests()
    {
        _options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"raw-allocation-gate-{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
    }

    private ApplicationDbContext NewDb() => new(_options);

    private sealed class Scene
    {
        public required Customer Customer { get; init; }
        public required Item Item { get; init; }
        public required Warehouse RawWarehouse { get; init; }
        public required Warehouse ReadyWarehouse { get; init; }
        public required RawMessage Message { get; init; }
        public required ProductionStageDefinition Formation { get; init; }
        public required ProductionStageDefinition Next { get; init; }
        public required ProductionStageDefinition ReadyStage { get; init; }
    }

    /// <summary>Master data only: every test creates its own order, so its requested/allocated split is explicit.</summary>
    private static Scene SeedScene(ApplicationDbContext db)
    {
        var customer = new Customer("C-1", "Customer", "seed");
        // KG primary with Meter as the secondary unit: this suite exercises BOTH units as
        // fully independent measurements, and the item-unit rules (spec sections 5-6) only
        // allow a unit the item is actually configured for.
        var item = new Item("IT-1", "Item", UnitOfMeasure.KG, "seed", secondaryUnit: UnitOfMeasure.Meter);
        var raw = new Warehouse("RAW", "Raw Material", WarehouseKind.RawMaterial, "seed");
        var ready = new Warehouse("RDY", "Ready Goods", WarehouseKind.ReadyGoods, "seed");
        var formation = new ProductionStageDefinition("PS001", "التشكيل", 1, "seed", isFormationStage: true);
        var next = new ProductionStageDefinition("PS002", "الصباغة", 2, "seed");
        var readyStage = new ProductionStageDefinition("PS003", "الجاهز", 3, "seed", isReadyGoodsStage: true);

        var message = new RawMessage("RMSG-1", DateTime.UtcNow, customer.Id, raw.Id, "seed", "seed");
        message.AddLine(item.Id, 100_000m, null, null, null);

        db.AddRange(customer, item, raw, ready, formation, next, readyStage, message);
        db.InventoryTransactions.Add(new InventoryTransaction(
            DocumentType.RawReceiptMessage, message.MessageNumber, message.Id, DateTime.UtcNow,
            raw.Id, customer.Id, item.Id, rawMessageId: message.Id, productionOrderId: null,
            quantityKg: 100_000m, quantityMeter: null, direction: TransactionDirection.In, createdBy: "seed"));
        db.SaveChanges();

        return new Scene
        {
            Customer = customer, Item = item, RawWarehouse = raw, ReadyWarehouse = ready, Message = message,
            Formation = formation, Next = next, ReadyStage = readyStage
        };
    }

    /// <summary>
    /// A Job Order whose route is built (formation stage already running) and which holds
    /// exactly the allocation the test asks for - nothing more.
    /// </summary>
    private static ProductionOrder CreateOrder(
        ApplicationDbContext db, Scene scene,
        decimal? requestedKg = 1000m, decimal? requestedMeter = null,
        decimal? allocatedKg = null, decimal? allocatedMeter = null)
    {
        var order = new ProductionOrder(
            "PRD-2026-000001", scene.Customer.Id, scene.Item.Id, DateTime.UtcNow, "seed",
            requestedQuantityKg: requestedKg, requestedQuantityMeter: requestedMeter);
        db.ProductionOrders.Add(order);
        order.BuildStageRoute(new[] { scene.Formation }, "seed");

        if (allocatedKg.HasValue || allocatedMeter.HasValue)
            db.RawAllocations.Add(order.AllocateRaw(scene.Message.Id, scene.Item.Id, allocatedKg, allocatedMeter, "seed"));

        db.SaveChanges();
        return order;
    }

    // ------------------------------------------------------------- handlers

    // Phase 4: the legacy Start / Complete / Skip handlers were deleted, so their gate cases are gone
    // with them. The gate itself is still covered on every surviving production path below (the transfer,
    // the ready-goods transfer, the completion, the resume) and by RawAllocationGateTests at the domain level.

    private static UpdateStageOutputCommandHandler OutputHandler(ApplicationDbContext db) =>
        new(db, new StubCurrentUser("operator"), new FixedClock());

    private static TransferToNextStageCommandHandler TransferHandler(ApplicationDbContext db) =>
        new(db, new StubCurrentUser("operator"), new FixedClock(), new OpenPeriod(), new FixedNumberGenerator());

    private static CreateReadyGoodsTransferCommandHandler ReadyGoodsHandler(ApplicationDbContext db) =>
        new(db, new StubCurrentUser("operator"), new FixedNumberGenerator(), new FixedClock(), new OpenPeriod(),
            new RecordingStockLock());

    private static CompleteProductionOrderCommandHandler CompletionHandler(ApplicationDbContext db) =>
        new(db, new StubCurrentUser("operator"));

    private static ResumeProductionOrderCommandHandler ResumeHandler(ApplicationDbContext db) =>
        new(db, new StubCurrentUser("operator"), new InventoryLedgerService(db), new FixedClock(), new OpenPeriod(),
            new RecordingStockLock());

    private static async Task<ProductionOrderStatus> StatusOf(ApplicationDbContext db, Guid orderId) =>
        (await db.ProductionOrders.AsNoTracking().SingleAsync(o => o.Id == orderId)).Status;

    // =====================================================================
    // Transfer -> the ready-goods stage (the path that completes the order)
    // =====================================================================

    [Fact]
    public async Task TransferToReadyGoods_WithNoAllocation_IsRefused_AndCreatesNoDocumentOrStock()
    {
        using var db = NewDb();
        var scene = SeedScene(db);
        var order = CreateOrder(db, scene); // a Draft order with no allocation at all
        var stage = order.StageExecutions.Single();

        var act = () => TransferHandler(db).Handle(
            new TransferToNextStageCommand(stage.Id, scene.ReadyStage.Id, 900m, null, null, null, "to ready goods"),
            CancellationToken.None);

        (await act.Should().ThrowAsync<DomainException>()).WithMessage("*not fully allocated*");

        (await db.ReadyGoodsTransfers.CountAsync()).Should().Be(0, "no ready-goods document may be created");
        (await db.InventoryTransactions.CountAsync(t => t.SourceDocumentType == DocumentType.ReadyGoodsTransfer))
            .Should().Be(0, "and no IN stock movement either");
        (await StatusOf(db, order.Id)).Should().Be(ProductionOrderStatus.Draft, "the order must not be completed");
        (await db.ProductionOrderStageExecutions.AsNoTracking().SingleAsync(s => s.Id == stage.Id))
            .Status.Should().Be(StageExecutionStatus.InProgress, "the stage must not be closed");
    }

    [Fact]
    public async Task TransferToReadyGoods_AtFiftyPercent_IsRefused()
    {
        using var db = NewDb();
        var scene = SeedScene(db);
        var order = CreateOrder(db, scene, allocatedKg: 500m);

        var act = () => TransferHandler(db).Handle(
            new TransferToNextStageCommand(
                order.StageExecutions.Single().Id, scene.ReadyStage.Id, 450m, null, null, null, "half-allocated"),
            CancellationToken.None);

        (await act.Should().ThrowAsync<DomainException>()).WithMessage("*not fully allocated*");
        (await db.ReadyGoodsTransfers.CountAsync()).Should().Be(0);
        (await db.InventoryTransactions.CountAsync(t => t.SourceDocumentType == DocumentType.ReadyGoodsTransfer))
            .Should().Be(0);
        (await StatusOf(db, order.Id)).Should().Be(ProductionOrderStatus.RawAllocated);
    }

    [Fact]
    public async Task TransferToReadyGoods_AtFullAllocation_PostsTheDocumentAndTheInMovement_AndCompletesTheOrder()
    {
        using var db = NewDb();
        var scene = SeedScene(db);
        var order = CreateOrder(db, scene, allocatedKg: 1000m);
        var stage = order.StageExecutions.Single();

        // The last operation before the Ready Goods warehouse records the FINAL capture: the original raw
        // weight it started from (input) plus the ready weight (output). The waste is derived from them.
        var dto = await TransferHandler(db).Handle(
            new TransferToNextStageCommand(
                stage.Id, scene.ReadyStage.Id, 900m, null, null, null, "final transfer",
                InputKg: 1000m),
            CancellationToken.None);

        dto.Status.Should().Be(ProductionOrderStatus.Completed);

        var transfer = await db.ReadyGoodsTransfers.SingleAsync();
        transfer.StageExecutionId.Should().Be(stage.Id);
        transfer.QuantityKg.Should().Be(900m);

        var movement = await db.InventoryTransactions
            .SingleAsync(t => t.SourceDocumentType == DocumentType.ReadyGoodsTransfer);
        movement.Direction.Should().Be(TransactionDirection.In);
        movement.QuantityKg.Should().Be(900m);
    }

    // =====================================================================
    // Transfer -> an ordinary next stage
    // =====================================================================

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.5)]
    public async Task TransferToAnOrdinaryStage_BelowFullAllocation_IsRefused(double fraction)
    {
        using var db = NewDb();
        var scene = SeedScene(db);
        var order = CreateOrder(db, scene, allocatedKg: fraction == 0 ? null : 500m);
        var stage = order.StageExecutions.Single();

        var act = () => TransferHandler(db).Handle(
            new TransferToNextStageCommand(stage.Id, scene.Next.Id, 480m, null, null, null, "transfer"), CancellationToken.None);

        (await act.Should().ThrowAsync<DomainException>()).WithMessage("*not fully allocated*");

        (await db.ProductionOrderStageExecutions.CountAsync(s => s.ProductionOrderId == order.Id))
            .Should().Be(1, "no next stage may be activated");
        (await db.ProductionOrderStageExecutions.AsNoTracking().SingleAsync(s => s.Id == stage.Id))
            .OutputKg.Should().BeNull("the transfer was refused before the output was recorded");
        (await StatusOf(db, order.Id)).Should().NotBe(ProductionOrderStatus.InProduction);
    }

    [Fact]
    public async Task TransferToAnOrdinaryStage_AtFullAllocation_ActivatesIt()
    {
        using var db = NewDb();
        var scene = SeedScene(db);
        var order = CreateOrder(db, scene, allocatedKg: 1000m);
        var stage = order.StageExecutions.Single();

        var dto = await TransferHandler(db).Handle(
            new TransferToNextStageCommand(stage.Id, scene.Next.Id, 950m, null, null, null, "transfer"),
            CancellationToken.None);

        dto.Status.Should().Be(ProductionOrderStatus.InProduction);
        var stages = await db.ProductionOrderStageExecutions.Where(s => s.ProductionOrderId == order.Id).ToListAsync();
        stages.Should().HaveCount(2);
        stages.Single(s => s.Id == stage.Id).OutputKg.Should().Be(950m);
        stages.Single(s => s.StageDefinitionId == scene.Next.Id).Status.Should().Be(StageExecutionStatus.InProgress);
    }

    [Fact]
    public async Task AnAllocationCompletedBySeveralMovements_OpensTheTransferPath()
    {
        using var db = NewDb();
        var scene = SeedScene(db);
        var order = CreateOrder(db, scene, allocatedKg: 400m);
        db.RawAllocations.Add(order.AllocateRaw(scene.Message.Id, scene.Item.Id, 600m, null, "second message"));
        await db.SaveChangesAsync();

        var dto = await TransferHandler(db).Handle(
            new TransferToNextStageCommand(
                order.StageExecutions.Single().Id, scene.Next.Id, 980m, null, null, null, "two-lot allocation"),
            CancellationToken.None);

        dto.Status.Should().Be(ProductionOrderStatus.InProduction);
    }

    [Fact]
    public async Task OverAllocation_DoesNotBlockTheTransfer()
    {
        using var db = NewDb();
        var scene = SeedScene(db);
        var order = CreateOrder(db, scene, allocatedKg: 1200m);

        var dto = await TransferHandler(db).Handle(
            new TransferToNextStageCommand(
                order.StageExecutions.Single().Id, scene.Next.Id, 900m, null, null, null, "surplus allocated"),
            CancellationToken.None);

        dto.Status.Should().Be(ProductionOrderStatus.InProduction);
    }

    // =====================================================================
    // KG and Meter are independent units
    // =====================================================================

    [Fact]
    public async Task MeterAllocation_NeverOpensAKgOrder()
    {
        using var db = NewDb();
        var scene = SeedScene(db);
        var order = CreateOrder(db, scene, requestedKg: 1000m, allocatedMeter: 1000m);

        var act = () => TransferHandler(db).Handle(
            new TransferToNextStageCommand(
                order.StageExecutions.Single().Id, scene.ReadyStage.Id, 900m, null, null, null, "meter for kg"),
            CancellationToken.None);

        (await act.Should().ThrowAsync<DomainException>())
            .WithMessage("*0 of 1000 KG*", "Meter allocation leaves the KG requirement untouched");
        (await db.ReadyGoodsTransfers.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task KgAllocation_NeverOpensAMeterOrder_AndTheMeterAllocationDoes()
    {
        using var db = NewDb();
        var scene = SeedScene(db);
        var order = CreateOrder(db, scene, requestedKg: null, requestedMeter: 500m, allocatedKg: 500m);

        var refused = () => TransferHandler(db).Handle(
            new TransferToNextStageCommand(
                order.StageExecutions.Single().Id, scene.Next.Id, null, 400m, null, null, "kg for meter"),
            CancellationToken.None);

        (await refused.Should().ThrowAsync<DomainException>()).WithMessage("*not fully allocated*");

        db.RawAllocations.Add(order.AllocateRaw(scene.Message.Id, scene.Item.Id, null, 500m, "meter lot"));
        await db.SaveChangesAsync();

        var dto = await TransferHandler(db).Handle(
            new TransferToNextStageCommand(
                order.StageExecutions.Single().Id, scene.Next.Id, null, 480m, null, null, "meter allocated"),
            CancellationToken.None);

        dto.Status.Should().Be(ProductionOrderStatus.InProduction);
    }

    // =====================================================================
    // Edit the OPEN operation's output
    // =====================================================================

    [Fact]
    public async Task EditStageOutput_BelowFullAllocation_IsRefused()
    {
        using var db = NewDb();
        var scene = SeedScene(db);
        var order = CreateOrder(db, scene, allocatedKg: 500m);
        var stage = order.StageExecutions.Single();

        var act = () => OutputHandler(db).Handle(
            new UpdateStageOutputCommand(stage.Id, 980m, null, "re-weighed"), CancellationToken.None);

        (await act.Should().ThrowAsync<DomainException>()).WithMessage("*not fully allocated*");
        (await db.ProductionOrderStageExecutions.AsNoTracking().SingleAsync(s => s.Id == stage.Id))
            .OutputKg.Should().BeNull();
    }

    [Fact]
    public async Task EditStageOutput_AtFullAllocation_IsAllowed()
    {
        using var db = NewDb();
        var scene = SeedScene(db);
        var order = CreateOrder(db, scene, allocatedKg: 1000m);
        var stage = order.StageExecutions.Single();

        await OutputHandler(db).Handle(
            new UpdateStageOutputCommand(stage.Id, 980m, null, "re-weighed"), CancellationToken.None);

        (await db.ProductionOrderStageExecutions.AsNoTracking().SingleAsync(s => s.Id == stage.Id))
            .OutputKg.Should().Be(980m);
    }

    // =====================================================================
    // Manual ready-goods transfer
    // =====================================================================

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.5)]
    public async Task ReadyGoodsTransfer_BelowFullAllocation_IsRefused_AndPostsNoStock(double fraction)
    {
        using var db = NewDb();
        var scene = SeedScene(db);
        var order = CreateOrder(db, scene, allocatedKg: fraction == 0 ? null : 500m);
        var stage = order.StageExecutions.Single();
        stage.CloseWithOutput(500m, null, null, null, null, "operator");
        await db.SaveChangesAsync();

        var act = () => ReadyGoodsHandler(db).Handle(
            new CreateReadyGoodsTransferCommand(
                order.Id, scene.ReadyWarehouse.Id, 400m, null, null, "manual transfer", StageExecutionId: stage.Id, Category: ReadyGoodsCategory.FirstGrade),
            CancellationToken.None);

        (await act.Should().ThrowAsync<DomainException>()).WithMessage("*not fully allocated*");

        (await db.ReadyGoodsTransfers.CountAsync()).Should().Be(0);
        (await db.InventoryTransactions.CountAsync(t => t.SourceDocumentType == DocumentType.ReadyGoodsTransfer))
            .Should().Be(0);
    }

    [Fact]
    public async Task ReadyGoodsTransfer_AtFullAllocation_IsAllowed()
    {
        using var db = NewDb();
        var scene = SeedScene(db);
        var order = CreateOrder(db, scene, allocatedKg: 1000m);
        var stage = order.StageExecutions.Single();
        stage.CloseWithOutput(500m, null, null, null, null, "operator");
        await db.SaveChangesAsync();

        var dto = await ReadyGoodsHandler(db).Handle(
            new CreateReadyGoodsTransferCommand(
                order.Id, scene.ReadyWarehouse.Id, 400m, null, null, "manual transfer", StageExecutionId: stage.Id, Category: ReadyGoodsCategory.FirstGrade),
            CancellationToken.None);

        dto.QuantityKg.Should().Be(400m);
        (await db.ReadyGoodsTransfers.CountAsync()).Should().Be(1);
        (await db.InventoryTransactions.CountAsync(t => t.SourceDocumentType == DocumentType.ReadyGoodsTransfer))
            .Should().Be(1);
    }

    // =====================================================================
    // Complete order
    // =====================================================================

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.5)]
    public async Task CompleteOrder_BelowFullAllocation_IsRefused_EvenWithEveryStageClosed(double fraction)
    {
        using var db = NewDb();
        var scene = SeedScene(db);
        var order = CreateOrder(db, scene, allocatedKg: fraction == 0 ? null : 500m);

        // The only thing left that could stop the completion is the allocation rule.
        order.StageExecutions.Single().CloseWithOutput(450m, null, null, null, null, "operator");
        await db.SaveChangesAsync();

        var act = () => CompletionHandler(db).Handle(
            new CompleteProductionOrderCommand(order.Id, "closing the job"), CancellationToken.None);

        (await act.Should().ThrowAsync<DomainException>()).WithMessage("*not fully allocated*");

        var reloaded = await db.ProductionOrders.AsNoTracking().SingleAsync(o => o.Id == order.Id);
        reloaded.Status.Should().NotBe(ProductionOrderStatus.Completed);
        reloaded.ZeroOutputReason.Should().BeNull();
    }

    [Fact]
    public async Task CompleteOrder_AtFullAllocation_IsAllowed()
    {
        using var db = NewDb();
        var scene = SeedScene(db);
        var order = CreateOrder(db, scene, allocatedKg: 1000m);
        order.StageExecutions.Single().CloseWithOutput(940m, null, null, null, null, "operator");
        await db.SaveChangesAsync();

        var dto = await CompletionHandler(db).Handle(
            new CompleteProductionOrderCommand(order.Id, "closing the job"), CancellationToken.None);

        dto.Status.Should().Be(ProductionOrderStatus.Completed);
    }

    // =====================================================================
    // The gate reads committed allocations, not a cached aggregate copy
    // =====================================================================

    [Fact]
    public async Task TheGate_JudgesTheCommittedAllocations_NotStaleCopyOnTheAggregate()
    {
        Scene scene;
        Guid orderId, stageId;
        using (var seed = NewDb())
        {
            scene = SeedScene(seed);
            var order = CreateOrder(seed, scene, allocatedKg: 400m);
            orderId = order.Id;
            stageId = order.StageExecutions.Single().Id;
        }

        // A second allocation - another user's session - commits the remainder of the KG.
        using (var otherUser = NewDb())
        {
            var loaded = await otherUser.ProductionOrders.SingleAsync(o => o.Id == orderId);
            otherUser.RawAllocations.Add(loaded.AllocateRaw(scene.Message.Id, scene.Item.Id, 600m, null, "other-user"));
            await otherUser.SaveChangesAsync();
        }

        // A fresh session transfers. Its own copy of the order was never told about that
        // allocation, so a gate reading the aggregate in memory would still refuse.
        using (var db = NewDb())
        {
            var dto = await TransferHandler(db).Handle(
                new TransferToNextStageCommand(stageId, scene.Next.Id, 950m, null, null, null, "after the second lot"),
                CancellationToken.None);

            dto.Status.Should().Be(ProductionOrderStatus.InProduction);
        }

        using var verify = NewDb();
        (await verify.RawAllocations.CountAsync(a => a.ProductionOrderId == orderId)).Should().Be(2);
    }

    // =====================================================================
    // Resume (a pause/restart must not become a way round the rule)
    // =====================================================================

    [Fact]
    public async Task Resume_WhoseReIssueStillLeavesTheOrderShort_IsRefused()
    {
        using var db = NewDb();
        var scene = SeedScene(db);
        var order = CreateOrder(db, scene, allocatedKg: 500m);
        order.Pause("machine breakdown", "supervisor");
        await db.SaveChangesAsync();

        var act = () => ResumeHandler(db).Handle(
            new ResumeProductionOrderCommand(
                order.Id, scene.Message.Id, scene.Item.Id, 300m, null), CancellationToken.None);

        (await act.Should().ThrowAsync<DomainException>())
            .WithMessage("*800 of 1000 KG*", "the re-issued material counts, but it does not cover the gap");

        (await StatusOf(db, order.Id)).Should().Be(ProductionOrderStatus.Paused);
        (await db.RawAllocations.CountAsync(a => a.ProductionOrderId == order.Id))
            .Should().Be(1, "nothing is allocated when the restart is refused");
    }

    [Fact]
    public async Task Resume_WhoseReIssueCompletesTheAllocation_IsAllowed()
    {
        using var db = NewDb();
        var scene = SeedScene(db);
        var order = CreateOrder(db, scene, allocatedKg: 500m);
        order.Pause("machine breakdown", "supervisor");
        await db.SaveChangesAsync();

        var dto = await ResumeHandler(db).Handle(
            new ResumeProductionOrderCommand(
                order.Id, scene.Message.Id, scene.Item.Id, 500m, null), CancellationToken.None);

        dto.Status.Should().Be(ProductionOrderStatus.InProduction);
        (await db.RawAllocations.CountAsync(a => a.ProductionOrderId == order.Id)).Should().Be(2);
    }
}
