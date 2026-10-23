using DyeHouseERP.Application.ProductionOrders.Commands;
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
/// The floor's stage selection and the final weights.
///
///   * selecting the operation the job moves to is a ROUTE step: it closes the current operation, activates
///     the chosen one and records NOTHING - no output, no loss, no separates and no stock movement. The
///     figures are recorded where they matter, on the LAST operation before the Ready Goods warehouse;
///   * that final capture asks for the original raw, the ready weight, the final metres and - optionally -
///     the separates, and derives the waste as original raw - ready weight - separates, per unit and never
///     between units;
///   * the ready-goods stock is still posted by the existing mechanism (ReadyGoodsTransfer + ledger IN).
/// </summary>
public class StageCheckboxTransferAndFinalWeightsTests
{
    private readonly DbContextOptions<ApplicationDbContext> _options;

    public StageCheckboxTransferAndFinalWeightsTests()
    {
        _options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"stagecheck-{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
    }

    private ApplicationDbContext NewDb() => new(_options);

    private sealed record Scene(
        Guid CustomerId, Guid ItemId, Guid OrderId, Guid FormationStageId, Guid NextStageId, Guid ReadyStageId);

    /// <summary>
    /// An issued job order at the formation stage: KG + Meter on the item (the second unit OPTIONAL - the
    /// item does not require both), 100% raw allocation so the floor gate is satisfied exactly as it is for
    /// a real issued order, and three active stages (formation, an ordinary next one, ready goods).
    /// </summary>
    private Scene Seed(decimal requestedKg = 1000m, decimal? requestedMeter = 120m)
    {
        var customer = new Customer("C-1", "Buyer", "seed");
        var item = new Item("IT-1", "Fabric", UnitOfMeasure.KG, "seed", secondaryUnit: UnitOfMeasure.Meter);
        var raw = new Warehouse("RAW", "Raw Material", WarehouseKind.RawMaterial, "seed");
        var ready = new Warehouse("RDY", "Ready Goods", WarehouseKind.ReadyGoods, "seed");

        var formation = new ProductionStageDefinition("PS001", "التشكيل", 1, "seed", isFormationStage: true);
        var nextStage = new ProductionStageDefinition("PS002", "الغسيل", 2, "seed");
        var readyStage = new ProductionStageDefinition("PS010", "الجاهز", 10, "seed", isReadyGoodsStage: true);

        var order = new ProductionOrder("PRD-1", customer.Id, item.Id, DateTime.UtcNow, "seed",
            color: "Blue", requestedQuantityKg: requestedKg, requestedQuantityMeter: requestedMeter);

        using var db = NewDb();
        db.Customers.Add(customer);
        db.Items.Add(item);
        db.Warehouses.AddRange(raw, ready);
        db.ProductionStageDefinitions.AddRange(formation, nextStage, readyStage);
        db.ProductionOrders.Add(order);

        order.BuildStageRoute(new[] { formation, nextStage, readyStage }, "seed");
        db.RawAllocations.Add(order.AllocateRaw(Guid.NewGuid(), item.Id, requestedKg, requestedMeter, "seed"));

        db.SaveChanges();
        return new Scene(customer.Id, item.Id, order.Id, formation.Id, nextStage.Id, readyStage.Id);
    }

    private static TransferToNextStageCommandHandler Transferrer(ApplicationDbContext db) => new(
        db, new StubCurrentUser("operator"), new FixedClock(), new OpenPeriod(), new FixedNumberGenerator());

    private static Task<ProductionOrderStageExecution> CurrentStageAsync(ApplicationDbContext db, Guid orderId)
        => db.ProductionOrderStageExecutions
            .FirstAsync(s => s.ProductionOrderId == orderId && s.Status == StageExecutionStatus.InProgress);

    private static Task<int> ReadyGoodsMovementCountAsync(ApplicationDbContext db)
        => db.InventoryTransactions.CountAsync(t => t.SourceDocumentType == DocumentType.ReadyGoodsTransfer);

    // ============================================================ 1. the checkbox is a ROUTE step

    [Fact]
    public async Task SelectingTheNextOperation_MovesTheJobWithoutRecordingProductionOrAnyStockMovement()
    {
        var scene = Seed();
        using var db = NewDb();
        var current = await CurrentStageAsync(db, scene.OrderId);

        var ledgerBefore = await db.InventoryTransactions.CountAsync();
        var transfersBefore = await db.ReadyGoodsTransfers.CountAsync();

        var dto = await Transferrer(db).Handle(
            new TransferToNextStageCommand(current.Id, scene.NextStageId, null, null, null, null, "just moved on"),
            CancellationToken.None);

        // The move is real and saved: the operation is closed and locked, the chosen one is running.
        var closed = await db.ProductionOrderStageExecutions.FirstAsync(s => s.Id == current.Id);
        closed.Status.Should().Be(StageExecutionStatus.Completed);
        closed.CompletedAtUtc.Should().NotBeNull("the move is saved, not merely displayed");

        // ...and it invented NOTHING: no output, no derived loss, no separates, no second grade.
        closed.OutputKg.Should().BeNull("choosing an operation is not a weighing");
        closed.OutputMeter.Should().BeNull();
        closed.LossKg.Should().BeNull();
        closed.LossPercentKg.Should().BeNull();
        closed.SeparatesKg.Should().BeNull();
        closed.SecondGradeKg.Should().BeNull();
        closed.WasteKg.Should().BeNull("there is no recorded input/output to derive a waste from");

        var activated = await CurrentStageAsync(db, scene.OrderId);
        activated.StageDefinitionId.Should().Be(scene.NextStageId);
        activated.BaselineKg.Should().Be(1000m,
            "the material was not weighed at this stop, so it is exactly what the previous operation handed over");
        activated.BaselineMeter.Should().Be(120m);

        (await db.Separates.CountAsync()).Should().Be(0, "no separates were recorded, so none were registered");
        (await db.ReadyGoodsTransfers.CountAsync()).Should().Be(transfersBefore);
        (await db.InventoryTransactions.CountAsync()).Should().Be(ledgerBefore,
            "selecting an operation posts no inventory movement");
        (await ReadyGoodsMovementCountAsync(db)).Should().Be(0);

        (await db.ProductionOrders.FirstAsync(o => o.Id == scene.OrderId)).Status
            .Should().Be(ProductionOrderStatus.InProduction, "the job is on the floor");

        dto.StageExecutions.Single(s => s.Id == current.Id).WasteKg.Should().BeNull();
    }

    [Fact]
    public async Task SelectingTheNextOperation_RefusesHalfRecordedFigures()
    {
        var scene = Seed();
        using var db = NewDb();
        var current = await CurrentStageAsync(db, scene.OrderId);

        // Separates without the output they belong to: the operator asked to record something, but not what
        // the operation produced - a figure nothing can be measured against.
        var act = () => Transferrer(db).Handle(
            new TransferToNextStageCommand(current.Id, scene.NextStageId, null, null, 40m, null, "separates only"),
            CancellationToken.None);

        (await act.Should().ThrowAsync<DomainException>()).WithMessage("*records no production*");
        (await db.ProductionOrderStageExecutions.CountAsync(s => s.Status == StageExecutionStatus.Completed))
            .Should().Be(0, "nothing was written");
        (await db.ProductionOrderStageExecutions.FirstAsync(s => s.Id == current.Id)).Status
            .Should().Be(StageExecutionStatus.InProgress);
    }

    [Fact]
    public async Task SelectingTheNextOperation_StillRecordsFiguresWhenTheOperatorAsksForIt()
    {
        var scene = Seed();
        using var db = NewDb();
        var current = await CurrentStageAsync(db, scene.OrderId);

        // The optional path stays available: an operation may still be closed WITH figures, and then the
        // loss is measured against the immutable baseline and the separates quantity enters the register.
        await Transferrer(db).Handle(
            new TransferToNextStageCommand(
                current.Id, scene.NextStageId, 980m, 118m, 15m, null, "weighed",
                InputKg: 1000m, InputMeter: 120m),
            CancellationToken.None);

        var closed = await db.ProductionOrderStageExecutions.FirstAsync(s => s.Id == current.Id);
        closed.OutputKg.Should().Be(980m);
        closed.InputKg.Should().Be(1000m);
        closed.LossKg.Should().Be(20m, "loss is still measured against the immutable baseline");
        closed.WasteKg.Should().Be(5m, "waste = original raw - ready weight - separates = 1000 - 980 - 15");
        (await db.Separates.CountAsync()).Should().Be(1, "a recorded separates quantity enters the register");
    }

    // ============================================================ 2. the final capture before Ready Goods

    [Fact]
    public async Task TheFinalCapture_DerivesTheWasteAndPostsTheReadyGoodsStock()
    {
        var scene = Seed();
        using var db = NewDb();
        var current = await CurrentStageAsync(db, scene.OrderId);

        await Transferrer(db).Handle(
            new TransferToNextStageCommand(
                current.Id, scene.ReadyStageId, 900m, 115m, 40m, 3m, "final weights",
                InputKg: 1000m, InputMeter: 120m),
            CancellationToken.None);

        var closed = await db.ProductionOrderStageExecutions.FirstAsync(s => s.Id == current.Id);

        // الهالك = أصل الخام - وزن الجاهز - المنفصلات، بالكيلو فقط.
        closed.WasteKg.Should().Be(60m, "1000 - 900 - 40");

        // The ready-goods mechanism is unchanged: one transfer row and one IN movement.
        var transfer = await db.ReadyGoodsTransfers.SingleAsync();
        transfer.StageExecutionId.Should().Be(current.Id);
        transfer.QuantityKg.Should().Be(900m);
        transfer.QuantityMeter.Should().Be(115m);

        var movement = await db.InventoryTransactions
            .SingleAsync(t => t.SourceDocumentType == DocumentType.ReadyGoodsTransfer);
        movement.Direction.Should().Be(TransactionDirection.In);
        movement.QuantityKg.Should().Be(900m);
        movement.QuantityMeter.Should().Be(115m);
        (await db.ProductionOrders.FirstAsync(o => o.Id == scene.OrderId)).Status
            .Should().Be(ProductionOrderStatus.Completed);
    }

    [Fact]
    public async Task TheFinalCapture_CompletesWithoutSeparates()
    {
        var scene = Seed();
        using var db = NewDb();
        var current = await CurrentStageAsync(db, scene.OrderId);

        // No separates at all - deliberately not required: the operator completes without entering them.
        await Transferrer(db).Handle(
            new TransferToNextStageCommand(
                current.Id, scene.ReadyStageId, 900m, null, null, null, "no separates", InputKg: 1000m),
            CancellationToken.None);

        var closed = await db.ProductionOrderStageExecutions.FirstAsync(s => s.Id == current.Id);
        closed.WasteKg.Should().Be(100m, "1000 - 900, with the absent separates counting as nothing kept aside");
        (await db.Separates.CountAsync()).Should().Be(0);
        (await ReadyGoodsMovementCountAsync(db)).Should().Be(1);
    }

    [Fact]
    public async Task TheFinalCapture_RefusesAnOriginalRawSmallerThanTheReadyWeightPlusSeparates()
    {
        var scene = Seed();
        using var db = NewDb();
        var current = await CurrentStageAsync(db, scene.OrderId);

        var act = () => Transferrer(db).Handle(
            new TransferToNextStageCommand(
                current.Id, scene.ReadyStageId, 900m, null, 200m, null, "impossible", InputKg: 1000m),
            CancellationToken.None);

        // 1000 - 900 - 200 would be -100: an impossible waste, refused before anything is written.
        (await act.Should().ThrowAsync<DomainException>()).WithMessage("*cannot be smaller*");
        (await db.ReadyGoodsTransfers.CountAsync()).Should().Be(0);
        (await ReadyGoodsMovementCountAsync(db)).Should().Be(0);
        (await db.ProductionOrderStageExecutions.FirstAsync(s => s.Id == current.Id)).Status
            .Should().Be(StageExecutionStatus.InProgress, "the operation stays open");
    }

    [Fact]
    public async Task TheReadyGoodsStop_RefusesToMoveWithoutTheFinalWeights()
    {
        var scene = Seed();
        using var db = NewDb();
        var current = await CurrentStageAsync(db, scene.OrderId);

        var noWeight = () => Transferrer(db).Handle(
            new TransferToNextStageCommand(current.Id, scene.ReadyStageId, null, null, null, null, "no weights"),
            CancellationToken.None);

        var noOriginalRaw = () => Transferrer(db).Handle(
            new TransferToNextStageCommand(current.Id, scene.ReadyStageId, 900m, null, null, null, "no raw"),
            CancellationToken.None);

        (await noWeight.Should().ThrowAsync<DomainException>()).WithMessage("*ready weight*");
        (await noOriginalRaw.Should().ThrowAsync<DomainException>()).WithMessage("*ORIGINAL RAW*");
        (await ReadyGoodsMovementCountAsync(db)).Should().Be(0);
        (await db.ReadyGoodsTransfers.CountAsync()).Should().Be(0);
    }

    // ============================================================ 3. units: the second one stays optional

    [Fact]
    public async Task TheFinalCapture_SubtractsSecondGradeFromWasteAndRejectsOverlappingOutput()
    {
        var scene = Seed();
        using var db = NewDb();
        var current = await CurrentStageAsync(db, scene.OrderId);

        await Transferrer(db).Handle(
            new TransferToNextStageCommand(
                current.Id, scene.ReadyStageId, 900m, null, 20m, null, "second grade included",
                InputKg: 1000m, SecondGradeKg: 50m),
            CancellationToken.None);

        var closed = await db.ProductionOrderStageExecutions.FirstAsync(s => s.Id == current.Id);
        closed.WasteKg.Should().Be(30m, "1000 - 900 first grade - 50 second grade - 20 separates");

        var invalidScene = Seed(orderNumber: "PRD-2026-OVERLAP");
        using var invalidDb = NewDb();
        var invalidCurrent = await CurrentStageAsync(invalidDb, invalidScene.OrderId);
        var act = () => Transferrer(invalidDb).Handle(
            new TransferToNextStageCommand(
                invalidCurrent.Id, invalidScene.ReadyStageId, 900m, null, 20m, null, "overlap",
                InputKg: 1000m, SecondGradeKg: 100m),
            CancellationToken.None);

        (await act.Should().ThrowAsync<DomainException>()).WithMessage("*cannot be smaller*");
        (await invalidDb.ReadyGoodsTransfers.CountAsync()).Should().Be(0);
        (await ReadyGoodsMovementCountAsync(invalidDb)).Should().Be(0);
    }

    [Fact]
    public async Task AnItemWithASecondaryUnit_StillAcceptsASingleUnit_AndNeverConvertsIt()
    {
        var scene = Seed();
        using var db = NewDb();
        var current = await CurrentStageAsync(db, scene.OrderId);

        // KG only: the item HAS a secondary unit (Meter) but does not require it, so nothing may be refused
        // and no Meter figure may be invented to fill the gap.
        await Transferrer(db).Handle(
            new TransferToNextStageCommand(
                current.Id, scene.ReadyStageId, 900m, null, null, null, "KG only", InputKg: 1000m),
            CancellationToken.None);

        var transfer = await db.ReadyGoodsTransfers.SingleAsync();
        transfer.QuantityKg.Should().Be(900m);
        transfer.QuantityMeter.Should().BeNull("no Meter figure was recorded, so none is stored or derived");

        var movement = await db.InventoryTransactions
            .SingleAsync(t => t.SourceDocumentType == DocumentType.ReadyGoodsTransfer);
        movement.QuantityMeter.Should().BeNull("KG stays independent - nothing is converted into Meter");

        var closed = await db.ProductionOrderStageExecutions.FirstAsync(s => s.Id == current.Id);
        closed.WasteKg.Should().Be(100m);
    }
}
