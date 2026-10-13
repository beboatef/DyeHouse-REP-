using DyeHouseERP.Application.Deliveries.Queries;
using DyeHouseERP.Application.ProductionOrders.Commands;
using DyeHouseERP.Application.ProductionOrders.Queries;
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
/// D3 - multiple printing executions per Job Order.
///
/// A Production Order may take the PRINTING stage more than once: one execution per
/// drawing, each with its own drawing number and its own recorded quantities, produced
/// through the ORDINARY transfer workflow (there is no new capability flag and no fixed
/// route). These tests pin that:
///   * a second print run is a NEW execution and never overwrites the first one's
///     drawing number or output,
///   * every print run must name its own drawing - it never inherits the previous run's,
///   * the new run is baselined on the previous run's output (carry-forward traceability),
///   * no OTHER stage gains the same-stage repeat ability,
///   * every run's drawing reaches the delivery statement.
/// </summary>
public class MultiplePrintingExecutionsTests
{
    private readonly DbContextOptions<ApplicationDbContext> _options;

    public MultiplePrintingExecutionsTests()
    {
        _options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"multi-print-{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
    }

    private ApplicationDbContext NewDb() => new(_options);

    private static TransferToNextStageCommandHandler Transfer(ApplicationDbContext db) =>
        new(db, new StubCurrentUser("operator"), new FixedClock(), new OpenPeriod(), new FixedNumberGenerator());

    private static async Task<ProductionOrderStatus> StatusOf(ApplicationDbContext db, Guid orderId) =>
        (await db.ProductionOrders.AsNoTracking().SingleAsync(o => o.Id == orderId)).Status;

    private sealed record Scene(
        Guid OrderId, Guid CustomerId, Guid ItemId,
        Guid FirstPrintExecutionId, Guid PrintingId, Guid FinishingId);

    /// <summary>
    /// A Job Order that is fully allocated and already on its FIRST printing run
    /// (drawing DR-A, 1000 KG output not yet recorded). The run was opened directly on
    /// the aggregate so the tests exercise the transfer handler itself: printing -> printing.
    /// </summary>
    private static Scene SeedFullyAllocatedOrderOnFirstPrintRun(ApplicationDbContext db)
    {
        var customer = new Customer("C-1", "Customer", "seed");
        var item = new Item("IT-1", "Item", UnitOfMeasure.KG, "seed");
        var raw = new Warehouse("RAW", "Raw Material", WarehouseKind.RawMaterial, "seed");
        var formation = new ProductionStageDefinition("PS001", "التشكيل", 1, "seed", isFormationStage: true);
        var printing = new ProductionStageDefinition("PS002", "الطباعة", 2, "seed", requiresDrawingNumber: true);
        var finishing = new ProductionStageDefinition("PS003", "التجهيز", 3, "seed");

        var message = new RawMessage("RMSG-1", DateTime.UtcNow, customer.Id, raw.Id, "seed", "seed");
        message.AddLine(item.Id, 100_000m, null, null, null);

        db.AddRange(customer, item, raw, formation, printing, finishing, message);
        db.InventoryTransactions.Add(new InventoryTransaction(
            DocumentType.RawReceiptMessage, message.MessageNumber, message.Id, DateTime.UtcNow,
            raw.Id, customer.Id, item.Id, rawMessageId: message.Id, productionOrderId: null,
            quantityKg: 100_000m, quantityMeter: null, direction: TransactionDirection.In, createdBy: "seed"));

        var order = new ProductionOrder("PRD-2026-000001", customer.Id, item.Id, DateTime.UtcNow, "seed",
            requestedQuantityKg: 1000m, requestedQuantityMeter: null);
        db.ProductionOrders.Add(order);
        order.BuildStageRoute(new[] { formation }, "seed");
        db.RawAllocations.Add(order.AllocateRaw(message.Id, item.Id, 1000m, null, "seed"));

        var firstPrint = order.ActivateNextStage(printing, 1000m, null, "seed", "DR-A");
        db.ProductionOrderStageExecutions.Add(firstPrint);
        db.SaveChanges();

        return new Scene(order.Id, customer.Id, item.Id, firstPrint.Id, printing.Id, finishing.Id);
    }

    // =====================================================================
    // A second print run is its own execution, with its own drawing
    // =====================================================================

    [Fact]
    public async Task Second_Print_Run_Is_A_New_Execution_With_Its_Own_Drawing_And_Quantity()
    {
        using var db = NewDb();
        var scene = SeedFullyAllocatedOrderOnFirstPrintRun(db);

        var dto = await Transfer(db).Handle(
            new TransferToNextStageCommand(scene.FirstPrintExecutionId, scene.PrintingId,
                OutputKg: 950m, OutputMeter: null, SeparatesKg: null, SeparatesMeter: null,
                Notes: "second drawing run", DrawingNumber: "DR-B"),
            CancellationToken.None);

        var prints = dto.StageExecutions.Where(s => s.StageDefinitionId == scene.PrintingId).ToList();

        prints.Should().HaveCount(2, "the second print run is a new execution, not an overwrite");
        var firstRun = prints.Single(p => p.DrawingNumber == "DR-A");
        var secondRun = prints.Single(p => p.DrawingNumber == "DR-B");
        firstRun.OutputKg.Should().Be(950m, "the first run keeps the output it was closed with");
        firstRun.Status.Should().Be(StageExecutionStatus.Completed);
        secondRun.Status.Should().Be(StageExecutionStatus.InProgress);

        var second = await db.ProductionOrderStageExecutions.AsNoTracking().SingleAsync(s => s.Id == secondRun.Id);
        second.BaselineKg.Should().Be(950m, "the new run carries the previous run's output forward as its baseline");

        (await StatusOf(db, scene.OrderId)).Should().Be(ProductionOrderStatus.InProduction);
    }

    [Fact]
    public async Task Second_Print_Run_Without_Its_Own_Drawing_Is_Refused_And_Changes_Nothing()
    {
        using var db = NewDb();
        var scene = SeedFullyAllocatedOrderOnFirstPrintRun(db);

        var act = () => Transfer(db).Handle(
            new TransferToNextStageCommand(scene.FirstPrintExecutionId, scene.PrintingId,
                900m, null, null, null, "no drawing supplied", DrawingNumber: null),
            CancellationToken.None);

        (await act.Should().ThrowAsync<DomainException>())
            .WithMessage("*drawing number*", "each printing run names its own drawing - DR-A is never inherited");

        (await db.ProductionOrderStageExecutions.CountAsync(s => s.StageDefinitionId == scene.PrintingId))
            .Should().Be(1, "no second execution is created when the drawing is missing");
        var first = await db.ProductionOrderStageExecutions.AsNoTracking().SingleAsync(s => s.Id == scene.FirstPrintExecutionId);
        first.DrawingNumber.Should().Be("DR-A");
        first.Status.Should().Be(StageExecutionStatus.InProgress, "the refused run is not closed");
        first.OutputKg.Should().BeNull("the refused run records no output");
        (await StatusOf(db, scene.OrderId)).Should().Be(ProductionOrderStatus.RawAllocated);
    }

    // =====================================================================
    // No other stage gains a repeat ability
    // =====================================================================

    [Fact]
    public async Task A_NonPrinting_Stage_Cannot_Be_Transferred_To_Itself()
    {
        using var db = NewDb();
        var scene = SeedFullyAllocatedOrderOnFirstPrintRun(db);

        var afterPrint = await Transfer(db).Handle(
            new TransferToNextStageCommand(scene.FirstPrintExecutionId, scene.FinishingId,
                900m, null, null, null, null),
            CancellationToken.None);
        var finishing = afterPrint.StageExecutions.Single(s => s.StageDefinitionId == scene.FinishingId);

        var act = () => Transfer(db).Handle(
            new TransferToNextStageCommand(finishing.Id, scene.FinishingId,
                880m, null, null, null, null),
            CancellationToken.None);

        (await act.Should().ThrowAsync<DomainException>())
            .WithMessage("*already on*", "only a drawing-requiring stage may be taken more than once");
    }

    // =====================================================================
    // Every run's drawing reaches the delivery statement
    // =====================================================================

    [Fact]
    public async Task Every_Print_Runs_Drawing_Reaches_The_Delivery_Statement()
    {
        using var db = NewDb();
        var scene = SeedFullyAllocatedOrderOnFirstPrintRun(db);

        var afterSecondRun = await Transfer(db).Handle(
            new TransferToNextStageCommand(scene.FirstPrintExecutionId, scene.PrintingId,
                950m, null, null, null, "second drawing run", DrawingNumber: "DR-B"),
            CancellationToken.None);
        var secondRun = afterSecondRun.StageExecutions.Single(s => s.DrawingNumber == "DR-B");

        // Finish the second run into a non-printing stage so the order holds two completed prints.
        await Transfer(db).Handle(
            new TransferToNextStageCommand(secondRun.Id, scene.FinishingId, 880m, null, null, null, null),
            CancellationToken.None);

        var delivery = new Delivery("DEL-MULTI", DateTime.UtcNow, scene.CustomerId, "operator");
        delivery.AddLine(scene.OrderId, scene.ItemId, null, 20m, null, null, null, null);
        db.Deliveries.Add(delivery);
        await db.SaveChangesAsync();

        var loaded = await new GetDeliveryByIdQueryHandler(db).Handle(new(delivery.Id), CancellationToken.None);
        var line = loaded.Lines.Single(l => l.ProductionOrderId == scene.OrderId);
        line.DrawingNumbers.Should().HaveCount(2, "each printing execution is reported with its own drawing");
        line.DrawingNumbers.Should().Contain("DR-A").And.Contain("DR-B");
    }
}
