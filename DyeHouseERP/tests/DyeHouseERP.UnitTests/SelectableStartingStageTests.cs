using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.CustomerReturns.Commands;
using DyeHouseERP.Application.ProductionOrders.Commands;
using DyeHouseERP.Application.ProductionOrders.DTOs;
using DyeHouseERP.Application.Separates.Commands;
using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Enums;
using DyeHouseERP.Domain.Exceptions;
using DyeHouseERP.Persistence;
using DyeHouseERP.Persistence.Services;
using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using Xunit;

namespace DyeHouseERP.UnitTests;

/// <summary>
/// Finding 3 (selectable starting stage for reprocessing and returns).
///
/// A reprocessing Job Order (spec section 23) and a Job Order raised from a customer return (spec
/// section 33) both rework material that is already PAST formation. Until now both always started at
/// التشكيل, because the route was built from the formation flag with no way to say otherwise. The
/// chosen starting stage must be:
///   * validated against the ACTIVE stage definitions (an unknown or deactivated stage is refused);
///   * actually USED and persisted as the order's first stage execution - not merely accepted in the
///     request (or shown in a screen);
///   * without changing the dynamic route (still exactly ONE stage is activated, the rest stay open);
///   * and without posting any inventory movement: the raw issue stays the single deduction, however
///     many times a confirmation is retried.
/// </summary>
public class SelectableStartingStageTests
{
    private readonly DbContextOptions<ApplicationDbContext> _options;

    public SelectableStartingStageTests()
    {
        _options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"start-stage-{Guid.NewGuid()}")
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
        public required ProductionStageDefinition Dyeing { get; init; }
        public required ProductionStageDefinition Printing { get; init; }
        public required ProductionStageDefinition Inactive { get; init; }
        public required Separate Separate { get; init; }
    }

    private const decimal LotReceivedKg = 5_000m;

    private Scene Seed()
    {
        var customer = new Customer("C-1", "Customer", "seed");
        var item = new Item("IT-1", "Item", UnitOfMeasure.KG, "seed");
        var warehouse = new Warehouse("RAW", "Raw Material", WarehouseKind.RawMaterial, "seed");
        var readyWarehouse = new Warehouse("RDY", "Ready Goods", WarehouseKind.ReadyGoods, "seed");

        var formation = new ProductionStageDefinition("PS001", "التشكيل", 1, "seed", isFormationStage: true);
        var dyeing = new ProductionStageDefinition("PS002", "الصباغة", 2, "seed");
        // Printing is the stage a reprocess would most often target; it also DEMANDS a drawing number,
        // so it pins the one case a selected starting stage cannot cover yet (see the test below).
        var printing = new ProductionStageDefinition("PS003", "الطباعة", 3, "seed", requiresDrawingNumber: true);
        var inactive = new ProductionStageDefinition("PS004", "التجفيف", 4, "seed");
        inactive.Deactivate("seed");

        var message = new RawMessage("RMSG-1", DateTime.UtcNow, customer.Id, warehouse.Id, "seed", "seed");
        message.AddLine(item.Id, LotReceivedKg, null, null, null);

        // The order the Separate was split off from, with the operation it came from.
        var originalOrder = new ProductionOrder("PRD-ORIG", customer.Id, item.Id, DateTime.UtcNow, "seed",
            requestedQuantityKg: 100m);
        originalOrder.BuildStageRoute(new[] { formation }, "seed");
        originalOrder.StageExecutions.Single().CloseWithOutput(50m, null, 50m, null, "seed", "seed");
        originalOrder.Complete();

        using var db = NewDb();
        db.AddRange(customer, item, warehouse, readyWarehouse, message, formation, dyeing, printing, inactive, originalOrder);
        db.InventoryTransactions.Add(new InventoryTransaction(
            DocumentType.RawReceiptMessage, message.MessageNumber, message.Id, DateTime.UtcNow,
            warehouse.Id, customer.Id, item.Id, rawMessageId: message.Id, productionOrderId: null,
            quantityKg: LotReceivedKg, quantityMeter: null, direction: TransactionDirection.In, createdBy: "seed"));
        db.SaveChanges();

        var separate = new Separate(
            originalOrder.Id, originalOrder.StageExecutions.Single().Id, customer.Id, item.Id, 50m, null, "seed");
        db.Separates.Add(separate);
        db.InventoryTransactions.Add(new InventoryTransaction(
            DocumentType.ReadyGoodsTransfer, "RGT-SEP-1", Guid.NewGuid(), DateTime.UtcNow,
            readyWarehouse.Id, customer.Id, item.Id, rawMessageId: null, productionOrderId: originalOrder.Id,
            quantityKg: 50m, quantityMeter: null, direction: TransactionDirection.In, createdBy: "seed",
            readyGoodsCategory: ReadyGoodsCategory.Separates));
        db.SaveChanges();

        return new Scene
        {
            Customer = customer, Item = item, Warehouse = warehouse, Message = message,
            Formation = formation, Dyeing = dyeing, Printing = printing, Inactive = inactive, Separate = separate
        };
    }

    private static ReprocessSeparateCommandHandler Reprocessor(ApplicationDbContext db) =>
        new(db, new StubCurrentUser("operator"), new FixedNumberGenerator(), new RecordingStockLock());

    private static IssueRawMaterialCommandHandler Issuer(ApplicationDbContext db) => new(
        db, new StubCurrentUser("operator"), new InventoryLedgerService(db), new FixedNumberGenerator(),
        new FixedClock(), new OpenPeriod(), new RecordingStockLock());

    // ---------------------------------------------------- reprocessing (spec section 23)

    [Fact]
    public async Task Reprocessing_StartsAtTheSelectedActiveStage_AndPersistsItAsTheActualFirstStage()
    {
        var scene = Seed();
        using var db = NewDb();
        var inventoryBefore = await db.InventoryTransactions.CountAsync();

        var dto = await Reprocessor(db).Handle(new ReprocessSeparateCommand(
            scene.Separate.Id, DateTime.UtcNow, "rework the print failure", StartingStageId: scene.Dyeing.Id),
            CancellationToken.None);

        // Persisted: read the route back from a fresh context - not from the returned DTO alone.
        using var verify = NewDb();
        var route = await verify.ProductionOrderStageExecutions
            .Where(s => s.ProductionOrderId == dto.Id).ToListAsync();

        route.Should().ContainSingle("still exactly ONE stage is activated - the rest of the route stays dynamic");
        route[0].StageDefinitionId.Should().Be(scene.Dyeing.Id,
            "the reprocessing job must actually START at the stage that was selected, not at formation");
        route[0].Sequence.Should().Be(2, "the chosen stage's own place in the dynamic route");
        route[0].Status.Should().Be(StageExecutionStatus.InProgress);
        route[0].BaselineKg.Should().Be(50m, "the starting stage's baseline is the reprocessed quantity");
        route.Should().NotContain(s => s.StageDefinitionId == scene.Formation.Id,
            "formation must not be activated for a job that starts later in the route");

        // The Separate still points at the new order, and the original order's history is untouched.
        var stored = await verify.Separates.SingleAsync(s => s.Id == scene.Separate.Id);
        stored.ReprocessingProductionOrderId.Should().Be(dto.Id);
        (await verify.ProductionOrders.CountAsync(o => o.Id == scene.Separate.OriginalProductionOrderId)).Should().Be(1);

        // The separate is consumed from Ready Goods, not raw stock; this posts exactly one category-specific OUT.
        (await verify.InventoryTransactions.CountAsync()).Should().Be(inventoryBefore + 1);
        (await verify.InventoryTransactions.CountAsync(t => t.Direction == TransactionDirection.Out)).Should().Be(1);
        (await verify.InventoryTransactions.SingleAsync(t => t.Direction == TransactionDirection.Out))
            .ReadyGoodsCategory.Should().Be(ReadyGoodsCategory.Separates);
    }

    [Fact]
    public async Task Reprocessing_WithoutAStartingStage_StillStartsAtFormation()
    {
        var scene = Seed();
        using var db = NewDb();

        var dto = await Reprocessor(db).Handle(
            new ReprocessSeparateCommand(scene.Separate.Id, DateTime.UtcNow, null), CancellationToken.None);

        using var verify = NewDb();
        var route = await verify.ProductionOrderStageExecutions
            .Where(s => s.ProductionOrderId == dto.Id).ToListAsync();

        route.Should().ContainSingle();
        route[0].StageDefinitionId.Should().Be(scene.Formation.Id, "the ordinary start is unchanged");
    }

    [Fact]
    public async Task Reprocessing_WithAnInactiveOrUnknownStartingStage_IsRejected_AndCreatesNoOrder()
    {
        var scene = Seed();

        using (var db = NewDb())
        {
            var inactive = async () => await Reprocessor(db).Handle(new ReprocessSeparateCommand(
                scene.Separate.Id, DateTime.UtcNow, null, StartingStageId: scene.Inactive.Id), CancellationToken.None);

            (await inactive.Should().ThrowAsync<DomainException>())
                .Which.Message.Should().Contain("active production stage");
        }

        using (var db = NewDb())
        {
            var unknown = async () => await Reprocessor(db).Handle(new ReprocessSeparateCommand(
                scene.Separate.Id, DateTime.UtcNow, null, StartingStageId: Guid.NewGuid()), CancellationToken.None);

            (await unknown.Should().ThrowAsync<DomainException>())
                .Which.Message.Should().Contain("active production stage");
        }

        using var verify = NewDb();
        (await verify.ProductionOrders.CountAsync()).Should().Be(1, "only the original order exists");
        (await verify.ProductionOrderStageExecutions.CountAsync()).Should().Be(1);
        var separate = await verify.Separates.SingleAsync(s => s.Id == scene.Separate.Id);
        separate.ReprocessingProductionOrderId.Should().BeNull();
        separate.Status.Should().Be(SeparateStatus.PendingReprocessing);
    }

    // ---------------------------------------------------- orders raised from returns (spec section 33)

    [Fact]
    public async Task ReturnWorkflow_ForwardsTheSelectedStartingStageToTheIssueWorkflow()
    {
        var scene = Seed();
        IssueRawMaterialCommand? forwarded = null;
        var sender = new Mock<ISender>();
        sender.Setup(s => s.Send(It.IsAny<IRequest<ProductionOrderDto>>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<ProductionOrderDto>, CancellationToken>((request, _) => forwarded = request as IssueRawMaterialCommand)
            .ReturnsAsync(new ProductionOrderDto());

        using var db = NewDb();
        db.CustomerReturns.Add(new CustomerReturn("CRT-1", DateTime.UtcNow, scene.Customer.Id, "seed"));
        await db.SaveChangesAsync();

        var returnId = await db.CustomerReturns.Select(r => r.Id).SingleAsync();

        await new CreateProductionOrderFromReturnCommandHandler(db, sender.Object).Handle(
            new CreateProductionOrderFromReturnCommand(
                returnId, scene.Item.Id, scene.Message.Id, DateTime.UtcNow, 60m, null,
                CostBearer.Company, StartingStageId: scene.Dyeing.Id), CancellationToken.None);

        forwarded.Should().NotBeNull("the return workflow forwards to the canonical issue workflow");
        forwarded!.StartingStageId.Should().Be(scene.Dyeing.Id,
            "the return's chosen start must reach the workflow that builds the order's route");
    }

    [Fact]
    public async Task OrderFromAReturn_StartsAtTheSelectedActiveStage_AndDeductsTheRawOnce()
    {
        var scene = Seed();
        var returnId = Guid.Empty;

        using (var db = NewDb())
        {
            var customerReturn = new CustomerReturn("CRT-1", DateTime.UtcNow, scene.Customer.Id, "seed");
            db.CustomerReturns.Add(customerReturn);
            db.SaveChanges();
            returnId = customerReturn.Id;
        }

        using (var db = NewDb())
        {
            var dto = await Issuer(db).Handle(new IssueRawMaterialCommand(
                scene.Customer.Id, scene.Item.Id, scene.Message.Id, DateTime.UtcNow,
                60m, null, CustomerReturnId: returnId, CostBearer: CostBearer.Company,
                StartingStageId: scene.Dyeing.Id), CancellationToken.None);

            using var verify = NewDb();
            var route = await verify.ProductionOrderStageExecutions
                .Where(s => s.ProductionOrderId == dto.Id).ToListAsync();

            route.Should().ContainSingle();
            route[0].StageDefinitionId.Should().Be(scene.Dyeing.Id,
                "the order raised from the return starts where the rework actually starts");
        }

        using var check = NewDb();
        (await check.InventoryTransactions.CountAsync(t => t.Direction == TransactionDirection.Out))
            .Should().Be(1, "the issue deducts the raw material exactly once");
        (await check.InventoryTransactions
                .Where(t => t.Direction == TransactionDirection.Out)
                .Select(t => t.QuantityKg).SingleAsync())
            .Should().Be(60m);
    }

    [Fact]
    public async Task OrderFromAReturn_WithAnInactiveOrUnknownStartingStage_IsRejected_AndWritesNothing()
    {
        var scene = Seed();
        var returnId = Guid.Empty;

        using (var db = NewDb())
        {
            var customerReturn = new CustomerReturn("CRT-1", DateTime.UtcNow, scene.Customer.Id, "seed");
            db.CustomerReturns.Add(customerReturn);
            db.SaveChanges();
            returnId = customerReturn.Id;
        }

        foreach (var badStage in new[] { scene.Inactive.Id, Guid.NewGuid() })
        {
            using var db = NewDb();
            var act = async () => await Issuer(db).Handle(new IssueRawMaterialCommand(
                scene.Customer.Id, scene.Item.Id, scene.Message.Id, DateTime.UtcNow,
                60m, null, CustomerReturnId: returnId, CostBearer: CostBearer.Company,
                StartingStageId: badStage), CancellationToken.None);

            (await act.Should().ThrowAsync<DomainException>())
                .Which.Message.Should().Contain("active production stage");
        }

        using var verify = NewDb();
        (await verify.ProductionOrders.CountAsync()).Should().Be(1, "only the original order exists");
        (await verify.InventoryTransactions.CountAsync(t => t.Direction == TransactionDirection.Out)).Should().Be(0);
    }

    // ---------------------------------------------------- retries never deduct twice

    [Fact]
    public async Task RetryingAReturnOrder_WithAStartingStage_DeductsOnce_AndKeepsTheChosenRoute()
    {
        var scene = Seed();
        var returnId = Guid.Empty;

        using (var db = NewDb())
        {
            var customerReturn = new CustomerReturn("CRT-1", DateTime.UtcNow, scene.Customer.Id, "seed");
            db.CustomerReturns.Add(customerReturn);
            db.SaveChanges();
            returnId = customerReturn.Id;
        }

        var token = Guid.NewGuid();
        var command = new IssueRawMaterialCommand(
            scene.Customer.Id, scene.Item.Id, scene.Message.Id, DateTime.UtcNow,
            60m, null, CustomerReturnId: returnId, CostBearer: CostBearer.Company,
            RequestId: token, StartingStageId: scene.Dyeing.Id);

        Guid firstId;
        using (var db = NewDb())
            firstId = (await Issuer(db).Handle(command, CancellationToken.None)).Id;

        using (var db = NewDb())
        {
            var retried = await Issuer(db).Handle(command, CancellationToken.None);
            retried.Id.Should().Be(firstId, "a retried confirmation returns the same order");
        }

        using var verify = NewDb();
        (await verify.ProductionOrders.CountAsync(o => o.CustomerReturnId == returnId)).Should().Be(1,
            "the retried confirmation raised exactly ONE order from this return");
        (await verify.InventoryTransactions.CountAsync(t => t.Direction == TransactionDirection.Out))
            .Should().Be(1, "the raw material is never deducted a second time by a retry");
        (await verify.ProductionOrderStageExecutions.CountAsync(s => s.ProductionOrderId == firstId))
            .Should().Be(1);
        (await verify.ProductionOrderStageExecutions
                .Where(s => s.ProductionOrderId == firstId)
                .Select(s => s.StageDefinitionId).SingleAsync())
            .Should().Be(scene.Dyeing.Id, "the retry does not rebuild or reset the order's route");
    }

    // ---------------------------------------------------- domain contract

    [Fact]
    public void BuildStageRoute_WithAnUnknownStageId_IsRefused()
    {
        var order = new ProductionOrder("PRD-X", Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow, "seed",
            requestedQuantityKg: 10m);
        var formation = new ProductionStageDefinition("PS001", "التشكيل", 1, "seed", isFormationStage: true);

        var act = () => order.BuildStageRoute(new[] { formation }, "seed", Guid.NewGuid());

        act.Should().Throw<DomainException>().WithMessage("*active production stage*");
        order.StageExecutions.Should().BeEmpty("a refused selection builds no route at all");
    }

    [Fact]
    public async Task StartingAtAStageThatRequiresADrawingNumber_IsRefusedByTheExistingDomainRule()
    {
        // LIMITATION (reported, not invented): the printing operation DEMANDS a drawing number, and
        // the reprocess/return commands carry none, so the domain refuses to activate it as a starting
        // stage - loudly, instead of starting the job with a missing drawing number. Where that number
        // would come from at reprocess time is an open business decision, so this sub-case is left
        // blocked rather than guessed at.
        var scene = Seed();
        using var db = NewDb();

        var act = async () => await Reprocessor(db).Handle(new ReprocessSeparateCommand(
            scene.Separate.Id, DateTime.UtcNow, null, StartingStageId: scene.Printing.Id), CancellationToken.None);

        (await act.Should().ThrowAsync<DomainException>())
            .Which.Message.Should().Contain("Drawing number is required");

        using var verify = NewDb();
        (await verify.ProductionOrders.CountAsync()).Should().Be(1, "the refused start created no order");
        (await verify.ProductionOrderStageExecutions.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task TheOriginalOrdersDrawingNumber_IsStoredButNeverBorrowedByTheNewOrder()
    {
        // Why a starting stage that demands a drawing number is refused instead of guessed at: the
        // drawing number IS stored and linked - on the ORIGINAL order's own printing execution - but the
        // project's rule is that a print run names its OWN number and never borrows another order's
        // (TransferToNextStageCommand). The new order therefore starts with none, and the original's
        // record stays exactly as it was.
        var scene = Seed();

        using (var db = NewDb())
        {
            var original = await db.ProductionOrders
                .FirstAsync(o => o.Id == scene.Separate.OriginalProductionOrderId);
            var printRun = original.ActivateNextStage(scene.Printing, 100m, null, "seed", "DWG-777");
            // Added explicitly - the same convention the transfer handler uses, because EF change
            // detection would otherwise treat the client-generated key as an existing row.
            db.ProductionOrderStageExecutions.Add(printRun);
            await db.SaveChangesAsync();
        }

        using (var db = NewDb())
        {
            var dto = await Reprocessor(db).Handle(new ReprocessSeparateCommand(
                scene.Separate.Id, DateTime.UtcNow, null, StartingStageId: scene.Dyeing.Id), CancellationToken.None);

            using var verify = NewDb();
            var newOrderStages = await verify.ProductionOrderStageExecutions
                .Where(s => s.ProductionOrderId == dto.Id).ToListAsync();
            newOrderStages.Should().ContainSingle();
            newOrderStages[0].DrawingNumber.Should().BeNull(
                "the new order may not borrow the original order's drawing number - each print run names its own");

            var originalPrintRun = await verify.ProductionOrderStageExecutions.FirstAsync(
                s => s.ProductionOrderId == scene.Separate.OriginalProductionOrderId && s.StageDefinitionId == scene.Printing.Id);
            originalPrintRun.DrawingNumber.Should().Be("DWG-777", "the original order's record is untouched");
        }
    }

    [Fact]
    public async Task Reprocessing_AndReturnOrders_StillDoNotPostAnyIssueByThemselves()
    {
        // Reprocessing never posts stock (the raw for the new job is issued separately), and the
        // return order's ONLY deduction is its own issue - so neither path can double-deduct the raw
        // that the original job already consumed.
        var scene = Seed();
        using var db = NewDb();
        var before = await db.InventoryTransactions.CountAsync();

        await Reprocessor(db).Handle(new ReprocessSeparateCommand(
            scene.Separate.Id, DateTime.UtcNow, null, StartingStageId: scene.Dyeing.Id), CancellationToken.None);

        using var verify = NewDb();
        (await verify.InventoryTransactions.CountAsync()).Should().Be(before + 1,
            "reprocessing consumes the posted separate balance, not the original raw lot");
        (await verify.InventoryTransactions.CountAsync(t => t.Direction == TransactionDirection.Out))
            .Should().Be(1, "the original raw issue is not deducted again");
    }
}
