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
/// Spec section 23: Ready Goods traceability.
///
/// Every ready-goods transfer must name the STAGE EXECUTION whose output it
/// moved, so a lot in the ready warehouse traces back to the stage that
/// produced it - not merely to the order - and so the cumulative transfers
/// linked to one stage can never together exceed that stage's own output.
///
/// Back-fill policy: transfers written before the column existed keep a NULL
/// link on purpose (a guessed stage link would be indistinguishable from a
/// real one), so NULL stays legal on the entity but is refused by the create
/// command for anything posted from now on.
/// </summary>
public class ReadyGoodsStageTraceabilityTests
{
    private readonly DbContextOptions<ApplicationDbContext> _options;

    public ReadyGoodsStageTraceabilityTests()
    {
        _options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"ready-stage-{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
    }

    private ApplicationDbContext NewDb() => new(_options);

    private sealed class Fixture
    {
        public required Customer Customer { get; init; }
        public required Item Item { get; init; }
        public required Warehouse RawWarehouse { get; init; }
        public required Warehouse ReadyWarehouse { get; init; }
        public required ProductionOrder Order { get; init; }
        public required ProductionStageDefinition FormationStage { get; init; }
        public required ProductionStageDefinition ReadyGoodsStage { get; init; }
        public required ProductionOrderStageExecution Stage { get; init; }
    }

    /// <summary>
    /// One order whose single formation stage is closed with
    /// <paramref name="outputKg"/> / <paramref name="outputMeter"/> - the shape
    /// a floor transfer leaves behind just before the ready-goods posting.
    /// </summary>
    private Fixture Seed(decimal outputKg = 500m, decimal? outputMeter = 120m, bool closeStage = true)
    {
        using var db = NewDb();

        var customer = new Customer("C-1", "Customer", "seed");
        // KG primary + Meter secondary: the fixture records both a KG and a Meter output,
        // and the unit rules only allow a unit the item is genuinely configured for.
        var item = new Item("IT-1", "Item", UnitOfMeasure.KG, "seed", secondaryUnit: UnitOfMeasure.Meter);
        var rawWarehouse = new Warehouse("RAW", "Raw Material", WarehouseKind.RawMaterial, "seed");
        var readyWarehouse = new Warehouse("RDY", "Ready Goods", WarehouseKind.ReadyGoods, "seed");

        db.Customers.Add(customer);
        db.Items.Add(item);
        db.Warehouses.Add(rawWarehouse);
        db.Warehouses.Add(readyWarehouse);

        var order = new ProductionOrder(
            "PRD-2026-0001", customer.Id, item.Id, DateTime.UtcNow, "seed", requestedQuantityKg: 600m);
        db.ProductionOrders.Add(order);

        // H1: output may only be produced on a fully allocated order, so the fixture
        // carries the 600 KG an operator would have allocated before working the job.
        db.RawAllocations.Add(order.AllocateRaw(Guid.NewGuid(), item.Id, 600m, null, "seed"));

        var formation = new ProductionStageDefinition("PS002", "التشكيل", 2, "seed", isFormationStage: true);
        var readyGoods = new ProductionStageDefinition("PS003", "الجاهز", 3, "seed",
            isReadyGoodsStage: true);
        db.ProductionStageDefinitions.AddRange(formation, readyGoods);
        order.BuildStageRoute(new[] { formation }, "seed");

        var stage = order.StageExecutions.Single();
        if (closeStage)
            stage.CloseWithOutput(outputKg, outputMeter, null, null, null, "operator");

        db.SaveChanges();

        return new Fixture
        {
            Customer = customer, Item = item, RawWarehouse = rawWarehouse, ReadyWarehouse = readyWarehouse,
            Order = order, FormationStage = formation, ReadyGoodsStage = readyGoods, Stage = stage
        };
    }

    private static CreateReadyGoodsTransferCommandHandler TransferHandler(ApplicationDbContext db, string user = "operator")
        => new(db, new StubCurrentUser(user), new FixedNumberGenerator(), new FixedClock(), new OpenPeriod(),
            new RecordingStockLock());

    // ---------------------------------------------------------- validation

    [Fact]
    public void CreateCommand_RequiresAStageExecutionLink()
    {
        var validator = new CreateReadyGoodsTransferCommandValidator();

        validator.Validate(new CreateReadyGoodsTransferCommand(
                Guid.NewGuid(), Guid.NewGuid(), 100m, null, null, null, StageExecutionId: null))
            .IsValid.Should().BeFalse("an untraceable transfer must never reach the database");

        validator.Validate(new CreateReadyGoodsTransferCommand(
                Guid.NewGuid(), Guid.NewGuid(), 100m, null, null, null, StageExecutionId: Guid.NewGuid()))
            .IsValid.Should().BeTrue();
    }

    [Fact]
    public void CreateCommand_RejectsNegativePieceCount()
    {
        var validator = new CreateReadyGoodsTransferCommandValidator();

        validator.Validate(new CreateReadyGoodsTransferCommand(
                Guid.NewGuid(), Guid.NewGuid(), 100m, null, -1, null, StageExecutionId: Guid.NewGuid()))
            .IsValid.Should().BeFalse("piece counts cannot be negative");

        validator.Validate(new CreateReadyGoodsTransferCommand(
                Guid.NewGuid(), Guid.NewGuid(), 100m, null, 0, null, StageExecutionId: Guid.NewGuid()))
            .IsValid.Should().BeTrue("zero is valid when the quantity is tracked without piece count");
    }

    [Fact]
    public void ReadyGoodsTransfer_DomainRejectsNegativePieceCountOnCreateAndUpdate()
    {
        var f = Seed();

        Action create = () => new ReadyGoodsTransfer(
            "RGT-TEST", DateTime.UtcNow, f.Order.Id, f.Customer.Id, f.Item.Id,
            f.ReadyWarehouse.Id, 10m, null, "operator", pieceCount: -1,
            stageExecutionId: f.Stage.Id);
        create.Should().Throw<ArgumentOutOfRangeException>();

        var transfer = new ReadyGoodsTransfer(
            "RGT-TEST-2", DateTime.UtcNow, f.Order.Id, f.Customer.Id, f.Item.Id,
            f.ReadyWarehouse.Id, 10m, null, "operator", pieceCount: 0,
            stageExecutionId: f.Stage.Id);

        Action update = () => transfer.UpdateDetails(DateTime.UtcNow, -1, null);
        update.Should().Throw<ArgumentOutOfRangeException>();
    }

    // ------------------------------------------------------- domain rule

    [Fact]
    public void CumulativeTransfers_MayNeverExceedTheStagesOwnOutput()
    {
        var f = Seed(outputKg: 500m);

        // 300 already transferred, 150 more: 450 of 500 - fine.
        Action ok = () => f.Stage.EnsureTransfersWithinOutput(300m, 0m, 150m, null);
        ok.Should().NotThrow();

        // 300 already transferred, 250 more: 550 of 500 - refused.
        Action over = () => f.Stage.EnsureTransfersWithinOutput(300m, 0m, 250m, null);
        over.Should().Throw<DomainException>()
            .WithMessage("*can never exceed a stage's output*");

        // KG and Meter are independent units: filling up the Meter allowance
        // says nothing about the KG allowance.
        Action kgStillOpen = () => f.Stage.EnsureTransfersWithinOutput(0m, 120m, 400m, null);
        kgStillOpen.Should().NotThrow("the stage produced 500 KG, of which none has been transferred");
    }

    [Fact]
    public void TransferOfAUnitTheStageNeverRecorded_IsRefused()
    {
        // The stage recorded KG only - a Meter figure for it would be invented.
        var f = Seed(outputKg: 500m, outputMeter: null);

        Action meter = () => f.Stage.EnsureTransfersWithinOutput(0m, 0m, null, 10m);
        meter.Should().Throw<DomainException>().WithMessage("*recorded no Meter output*");

        Action kg = () => f.Stage.EnsureTransfersWithinOutput(0m, 0m, 10m, null);
        kg.Should().NotThrow();
    }

    [Fact]
    public void AnOpenStage_HasNoOutputToTransfer()
    {
        var f = Seed(closeStage: false);

        Action act = () => f.Stage.EnsureTransfersWithinOutput(0m, 0m, 10m, null);
        act.Should().Throw<DomainException>().WithMessage("*has not been closed*");
    }

    // ------------------------------------------------- handler end-to-end

    [Fact]
    public async Task APostedTransfer_RemembersTheStageThatProducedIt()
    {
        var f = Seed(outputKg: 500m, outputMeter: 120m);
        using var db = NewDb();

        var dto = await TransferHandler(db).Handle(
            new CreateReadyGoodsTransferCommand(
                f.Order.Id, f.ReadyWarehouse.Id, 400m, 100m, null, "first partial transfer",
                StageExecutionId: f.Stage.Id),
            CancellationToken.None);

        dto.StageExecutionId.Should().Be(f.Stage.Id, "the DTO must carry the link back to the caller");
        dto.StageSequence.Should().Be(f.Stage.Sequence);
        dto.StageOutputKg.Should().Be(500m);

        var stored = await db.ReadyGoodsTransfers.SingleAsync();
        stored.StageExecutionId.Should().Be(f.Stage.Id);
        stored.ProductionOrderId.Should().Be(f.Order.Id);

        // The register reads it back with the stage's identity, not a bare id.
        var reread = await new DyeHouseERP.Application.ReadyGoods.Queries.GetReadyGoodsTransfersQueryHandler(db)
            .Handle(new DyeHouseERP.Application.ReadyGoods.Queries.GetReadyGoodsTransfersQuery(), CancellationToken.None);
        reread.Single().StageExecutionId.Should().Be(f.Stage.Id);
        reread.Single().StageSequence.Should().Be(f.Stage.Sequence);
    }

    [Fact]
    public async Task Transfer_ExceedingTheStagesOutput_IsRefused_AndPostsNothing()
    {
        var f = Seed(outputKg: 500m, outputMeter: 120m);
        using var db = NewDb();

        var act = () => TransferHandler(db).Handle(
            new CreateReadyGoodsTransferCommand(
                f.Order.Id, f.ReadyWarehouse.Id, 501m, null, null, "too much",
                StageExecutionId: f.Stage.Id),
            CancellationToken.None);

        (await act.Should().ThrowAsync<DomainException>())
            .WithMessage("*Cumulative transfers can never exceed a stage's output*");

        (await db.ReadyGoodsTransfers.CountAsync()).Should().Be(0);
        (await db.InventoryTransactions.CountAsync(t =>
            t.SourceDocumentType == DocumentType.ReadyGoodsTransfer)).Should().Be(0);
    }

    [Fact]
    public async Task Transfer_NamingAStageOfADifferentOrder_IsRefused()
    {
        var f = Seed(outputKg: 500m);
        using var db = NewDb();

        // A second order with its own stage - its id must never be usable here.
        var other = new ProductionOrder("PRD-2026-0002", f.Customer.Id, f.Item.Id, DateTime.UtcNow, "seed",
            requestedQuantityKg: 100m);
        db.ProductionOrders.Add(other);
        var otherFormation = new ProductionStageDefinition("PS102", "تشكيل آخر", 102, "seed", isFormationStage: true);
        db.ProductionStageDefinitions.Add(otherFormation);
        other.BuildStageRoute(new[] { otherFormation }, "seed");
        other.StageExecutions.Single().CloseWithOutput(100m, null, null, null, null, "operator");
        await db.SaveChangesAsync();

        var foreignStageId = other.StageExecutions.Single().Id;

        var act = () => TransferHandler(db).Handle(
            new CreateReadyGoodsTransferCommand(
                f.Order.Id, f.ReadyWarehouse.Id, 100m, null, null, "wrong order's stage",
                StageExecutionId: foreignStageId),
            CancellationToken.None);

        (await act.Should().ThrowAsync<DomainException>())
            .WithMessage("*belongs to a different production order*");

        (await db.ReadyGoodsTransfers.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Transfer_LinkedToAnOpenStage_IsRefused()
    {
        var f = Seed(closeStage: false);
        using var db = NewDb();

        var act = () => TransferHandler(db).Handle(
            new CreateReadyGoodsTransferCommand(
                f.Order.Id, f.ReadyWarehouse.Id, 100m, null, null, "stage still running",
                StageExecutionId: f.Stage.Id),
            CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>();
        (await db.ReadyGoodsTransfers.CountAsync()).Should().Be(0);
    }

    /// <summary>
    /// The automatic transfer a ready-goods stage transition posts carries the
    /// same link a manual one does - the quantity it posts IS that stage's
    /// recorded output, so the cap holds by construction.
    /// </summary>
    [Fact]
    public async Task TheAutomaticStageTransfer_IsLinkedToTheStageWhoseOutputItMoves()
    {
        var f = Seed(closeStage: false); // stage still InProgress, ready to transfer
        using var db = NewDb();

        var handler = new TransferToNextStageCommandHandler(
            db, new StubCurrentUser("operator"), new FixedClock(), new OpenPeriod(), new FixedNumberGenerator());

        // The FINAL capture on the last operation before the warehouse: the original raw it started from
        // (input), the ready weight it produced (output) and the final metres. The waste is derived from them.
        await handler.Handle(new TransferToNextStageCommand(
            f.Stage.Id, f.ReadyGoodsStage.Id, 480m, 115m, null, null, "final transfer",
            InputKg: 520m, InputMeter: 120m), CancellationToken.None);

        var transfer = await db.ReadyGoodsTransfers.SingleAsync();
        transfer.StageExecutionId.Should().Be(f.Stage.Id);
        transfer.QuantityKg.Should().Be(480m);
        transfer.QuantityMeter.Should().Be(115m);

        // The linked stage's own output is exactly what was posted, so no later
        // transfer against it can be approved beyond that figure.
        var stage = await db.ProductionOrderStageExecutions.SingleAsync(s => s.Id == f.Stage.Id);
        stage.OutputKg.Should().Be(480m);
        Action within = () => stage.EnsureTransfersWithinOutput(480m, 115m, 1m, null);
        within.Should().Throw<DomainException>();
    }

    /// <summary>
    /// Historical rows: a transfer written before the column existed keeps a
    /// NULL link - the system never invents a stage for it - and the register
    /// still renders it (as "no stage") instead of dropping the row.
    /// </summary>
    [Fact]
    public async Task APreExistingTransfer_KeepsItsNullLink_AndStillReadsBack()
    {
        var f = Seed(outputKg: 500m);
        using var db = NewDb();

        var legacy = new ReadyGoodsTransfer(
            "RGT-LEGACY-1", DateTime.UtcNow, f.Order.Id, f.Customer.Id, f.Item.Id,
            f.ReadyWarehouse.Id, 300m, null, "legacy-import"); // no stageExecutionId
        db.ReadyGoodsTransfers.Add(legacy);
        await db.SaveChangesAsync();

        legacy.StageExecutionId.Should().BeNull("no historical link is ever fabricated");

        var rows = await new DyeHouseERP.Application.ReadyGoods.Queries.GetReadyGoodsTransfersQueryHandler(db)
            .Handle(new DyeHouseERP.Application.ReadyGoods.Queries.GetReadyGoodsTransfersQuery(), CancellationToken.None);

        rows.Should().HaveCount(1);
        rows[0].StageExecutionId.Should().BeNull();
        rows[0].StageName.Should().BeNull();
    }
}
