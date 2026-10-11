using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.ProductionOrders.Commands;
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
/// Pause / Resume (S5). The releasable quantity must come from the INVENTORY
/// LEDGER, never from a stage's InputKg, so material that was already processed
/// can still be returned; a release must never exceed what the order actually
/// took from the chosen lot; and neither a repeated pause nor a repeated resume
/// may post the same movement twice.
/// </summary>
public class PauseResumeLedgerTests
{
    private readonly DbContextOptions<ApplicationDbContext> _options;

    public PauseResumeLedgerTests()
    {
        _options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"pause-resume-{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
    }

    private ApplicationDbContext NewDb() => new(_options);

    /// <summary>Minimal current-user fake - avoids Moq matcher quirks in Mock.Of.</summary>
    private sealed class TestUser : ICurrentUserService
    {
        public TestUser(string userName) => UserName = userName;
        public string UserName { get; }
        public Guid? UserId => null;
        public string? IpAddress => null;
        public bool IsInRole(string role) => false;
        public bool HasPermission(string permission) => true;
    }

    private sealed class Fixture
    {
        public required Customer Customer { get; init; }
        public required Item Item { get; init; }
        public required Warehouse Warehouse { get; init; }
        public required List<RawMessage> Messages { get; init; }
        public required ProductionOrder Order { get; init; }
    }

    /// <summary>
    /// Builds an InProduction order issued from <paramref name="lots"/> separate raw
    /// messages, each carrying an IN receipt row and an OUT issue row for this order -
    /// exactly the ledger shape a real allocation leaves behind.
    /// </summary>
    private Fixture Seed(int lots, decimal issuedKgPerLot, decimal? issuedMeterPerLot = null,
        decimal? completedStageOutputKg = null)
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
            requestedQuantityKg: issuedKgPerLot * lots,
            requestedQuantityMeter: issuedMeterPerLot * lots);

        db.ProductionOrders.Add(order);

        // The route is built when the order is created (before any allocation), which
        // is how the real conversion flow does it - and it means the first stage's
        // baseline is the order's own weight.
        var formation = new ProductionStageDefinition("PS002", "التشكيل", 2, "seed", isFormationStage: true);
        db.ProductionStageDefinitions.Add(formation);
        order.BuildStageRoute(new[] { formation }, "seed");

        if (completedStageOutputKg.HasValue)
            order.StageExecutions.Single().CloseWithOutput(completedStageOutputKg.Value, null, null, null, null, "operator");

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

            // The issue to this Job Order (OUT) plus its allocation row.
            var allocation = order.AllocateRaw(message.Id, item.Id, issuedKgPerLot, issuedMeterPerLot, "seed");
            db.RawAllocations.Add(allocation);

            db.InventoryTransactions.Add(new InventoryTransaction(
                DocumentType.ProductionOrder, order.OrderNumber, order.Id, DateTime.UtcNow,
                warehouse.Id, customer.Id, item.Id, rawMessageId: message.Id, productionOrderId: order.Id,
                quantityKg: issuedKgPerLot, quantityMeter: issuedMeterPerLot,
                direction: TransactionDirection.Out, createdBy: "seed"));
        }

        order.MarkInProduction();
        db.SaveChanges();

        return new Fixture
        {
            Customer = customer, Item = item, Warehouse = warehouse, Messages = messages, Order = order
        };
    }

    private static PauseProductionOrderCommandHandler PauseHandler(ApplicationDbContext db, string user = "supervisor")
        => new(db, new TestUser(user), new FixedClock(), new OpenPeriod(), new RecordingStockLock());

    private static ResumeProductionOrderCommandHandler ResumeHandler(ApplicationDbContext db, string user = "supervisor")
        => new(db, new TestUser(user), new InventoryLedgerService(db),
            new FixedClock(), new OpenPeriod(), new RecordingStockLock());

    private static decimal OutstandingKg(ApplicationDbContext db, Guid orderId)
        => db.InventoryTransactions
            .Where(t => t.ProductionOrderId == orderId
                && t.SourceDocumentType == DocumentType.ProductionOrder
                && t.QuantityKg != null)
            .Sum(t => t.QuantityKg!.Value)
            - db.InventoryTransactions
                .Where(t => t.ProductionOrderId == orderId
                    && t.SourceDocumentType == DocumentType.ProductionOrderPause
                    && t.QuantityKg != null)
                .Sum(t => t.QuantityKg!.Value);

    // ---------------------------------------------------------------- pause

    [Fact]
    public async Task Pause_WithZeroRelease_PausesWithoutAnyStockMovement()
    {
        var f = Seed(lots: 1, issuedKgPerLot: 800m);
        using var db = NewDb();

        var dto = await PauseHandler(db).Handle(
            new PauseProductionOrderCommand(f.Order.Id, "machine breakdown"), CancellationToken.None);

        dto.Status.Should().Be(ProductionOrderStatus.Paused);

        (await db.InventoryTransactions.CountAsync(t => t.SourceDocumentType == DocumentType.ProductionOrderPause))
            .Should().Be(0, "a pause with nothing returned is not a stock movement");
    }

    [Fact]
    public async Task Pause_Release_CannotExceedTheOutstandingLedgerIssue()
    {
        var f = Seed(lots: 1, issuedKgPerLot: 800m);
        using var db = NewDb();

        var act = () => PauseHandler(db).Handle(
            new PauseProductionOrderCommand(f.Order.Id, "return too much", ReleaseKg: 801m), CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>();

        (await db.InventoryTransactions.CountAsync(t => t.SourceDocumentType == DocumentType.ProductionOrderPause))
            .Should().Be(0);
    }

    [Fact]
    public async Task Pause_Release_Equal_To_The_Outstanding_Issue_PostsOneInMovement()
    {
        var f = Seed(lots: 1, issuedKgPerLot: 800m);
        using var db = NewDb();

        await PauseHandler(db).Handle(
            new PauseProductionOrderCommand(
                f.Order.Id, "job stopped", f.Messages[0].Id, f.Item.Id, ReleaseKg: 800m),
            CancellationToken.None);

        var releases = await db.InventoryTransactions
            .Where(t => t.SourceDocumentType == DocumentType.ProductionOrderPause)
            .ToListAsync();

        releases.Should().HaveCount(1);
        releases[0].Direction.Should().Be(TransactionDirection.In);
        releases[0].QuantityKg.Should().Be(800m);
        releases[0].QuantityMeter.Should().BeNull();
        releases[0].RawMessageId.Should().Be(f.Messages[0].Id);
        releases[0].ProductionOrderId.Should().Be(f.Order.Id);

        // The original OUT is untouched - nothing was edited or deleted.
        (await db.InventoryTransactions.CountAsync(t => t.SourceDocumentType == DocumentType.ProductionOrder))
            .Should().Be(1);
    }

    [Fact]
    public async Task Pause_WithSeveralSourceLots_RefusesToPickABatchItself()
    {
        var f = Seed(lots: 2, issuedKgPerLot: 400m);
        using var db = NewDb();

        var act = () => PauseHandler(db).Handle(
            new PauseProductionOrderCommand(f.Order.Id, "return 100", ReleaseKg: 100m), CancellationToken.None);

        // No FIFO and no automatic source selection: the user must name the lot.
        await act.Should().ThrowAsync<DomainException>()
            .WithMessage("*more than one raw message*");
    }

    [Fact]
    public async Task Pause_WithSeveralSourceLots_ExplicitSource_TouchesOnlyThatLot()
    {
        var f = Seed(lots: 2, issuedKgPerLot: 400m);
        using var db = NewDb();

        await PauseHandler(db).Handle(
            new PauseProductionOrderCommand(
                f.Order.Id, "return from lot 2", f.Messages[1].Id, f.Item.Id, ReleaseKg: 250m),
            CancellationToken.None);

        var release = await db.InventoryTransactions
            .SingleAsync(t => t.SourceDocumentType == DocumentType.ProductionOrderPause);

        release.RawMessageId.Should().Be(f.Messages[1].Id);
        release.QuantityKg.Should().Be(250m);
    }

    [Fact]
    public async Task Pause_MayReturnMaterialThatWasAlreadyProcessed()
    {
        // A completed stage recorded only 700 KG of an 800 KG issue. The old rule
        // (allocated - stage input) would have capped the return at 100 KG and
        // forbidden returning processed material; the ledger rule allows the full
        // 800 KG because that is what the order actually holds from the lot.
        var f = Seed(lots: 1, issuedKgPerLot: 800m, completedStageOutputKg: 700m);
        using var db = NewDb();

        await PauseHandler(db).Handle(
            new PauseProductionOrderCommand(
                f.Order.Id, "scrap the run", f.Messages[0].Id, f.Item.Id, ReleaseKg: 800m),
            CancellationToken.None);

        var release = await db.InventoryTransactions
            .SingleAsync(t => t.SourceDocumentType == DocumentType.ProductionOrderPause);
        release.QuantityKg.Should().Be(800m);
    }

    [Fact]
    public async Task Pause_Repeated_IsRefused_SoTheReleaseCannotPostTwice()
    {
        var f = Seed(lots: 1, issuedKgPerLot: 500m);
        using var db = NewDb();

        await PauseHandler(db).Handle(
            new PauseProductionOrderCommand(f.Order.Id, "first", f.Messages[0].Id, f.Item.Id, ReleaseKg: 100m),
            CancellationToken.None);

        var act = () => PauseHandler(db).Handle(
            new PauseProductionOrderCommand(f.Order.Id, "again", f.Messages[0].Id, f.Item.Id, ReleaseKg: 100m),
            CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>();

        (await db.InventoryTransactions.CountAsync(t => t.SourceDocumentType == DocumentType.ProductionOrderPause))
            .Should().Be(1);
    }

    [Fact]
    public async Task Pause_KeepsKgAndMeterSeparate_AndNeverConvertsBetweenThem()
    {
        var f = Seed(lots: 1, issuedKgPerLot: 800m, issuedMeterPerLot: 1200m);
        using var db = NewDb();

        await PauseHandler(db).Handle(
            new PauseProductionOrderCommand(
                f.Order.Id, "KG only", f.Messages[0].Id, f.Item.Id, ReleaseKg: 300m),
            CancellationToken.None);

        var release = await db.InventoryTransactions
            .SingleAsync(t => t.SourceDocumentType == DocumentType.ProductionOrderPause);

        release.QuantityKg.Should().Be(300m);
        release.QuantityMeter.Should().BeNull("KG and Meter are independent - nothing was converted");
    }

    // --------------------------------------------------------------- resume

    [Fact]
    public async Task Resume_RequiresAPausedOrder_SoItCanNeverDoublePost()
    {
        var f = Seed(lots: 1, issuedKgPerLot: 800m);
        using var db = NewDb();

        var act = () => ResumeHandler(db).Handle(
            new ResumeProductionOrderCommand(f.Order.Id, f.Messages[0].Id, f.Item.Id, 200m, null),
            CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>();

        (await db.InventoryTransactions.CountAsync(t => t.SourceDocumentType == DocumentType.ProductionOrder))
            .Should().Be(1, "the running order's single original issue is unchanged");
    }

    [Fact]
    public async Task Resume_PostsExactlyOneAllocationAndOneIssue_AndContinuesTheSameOrder()
    {
        var f = Seed(lots: 1, issuedKgPerLot: 800m);
        using var db = NewDb();

        await PauseHandler(db).Handle(
            new PauseProductionOrderCommand(f.Order.Id, "stop", f.Messages[0].Id, f.Item.Id, ReleaseKg: 800m),
            CancellationToken.None);

        var dto = await ResumeHandler(db).Handle(
            new ResumeProductionOrderCommand(f.Order.Id, f.Messages[0].Id, f.Item.Id, 400m, null),
            CancellationToken.None);

        dto.Status.Should().Be(ProductionOrderStatus.InProduction);
        dto.OrderNumber.Should().Be(f.Order.OrderNumber, "the SAME order continues - no new document");

        (await db.InventoryTransactions.CountAsync(t => t.SourceDocumentType == DocumentType.ProductionOrder))
            .Should().Be(2, "one original issue plus exactly one resumed issue");
        (await db.RawAllocations.CountAsync(a => a.ProductionOrderId == f.Order.Id))
            .Should().Be(2);

        // The resumed issue is a NEW OUT row; the pause release is untouched.
        (await db.InventoryTransactions.CountAsync(t => t.SourceDocumentType == DocumentType.ProductionOrderPause))
            .Should().Be(1);
    }

    [Fact]
    public async Task Resume_Repeated_IsRefused_AndPostsNoSecondIssue()
    {
        var f = Seed(lots: 1, issuedKgPerLot: 800m);
        using var db = NewDb();

        await PauseHandler(db).Handle(
            new PauseProductionOrderCommand(f.Order.Id, "stop"), CancellationToken.None);
        await ResumeHandler(db).Handle(
            new ResumeProductionOrderCommand(f.Order.Id, f.Messages[0].Id, f.Item.Id, 400m, null),
            CancellationToken.None);

        var act = () => ResumeHandler(db).Handle(
            new ResumeProductionOrderCommand(f.Order.Id, f.Messages[0].Id, f.Item.Id, 400m, null),
            CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>();

        (await db.InventoryTransactions.CountAsync(t => t.SourceDocumentType == DocumentType.ProductionOrder))
            .Should().Be(2, "the repeated resume must not post a third issue");
    }

    [Fact]
    public async Task Resume_PreservesCompletedStageHistoryAndItsWastePercentage()
    {
        // Spec section 15's worked example: baseline 783, output 775 -> 8 KG loss, 1.02 %.
        var f = Seed(lots: 1, issuedKgPerLot: 783m, completedStageOutputKg: 775m);
        using var db = NewDb();

        await PauseHandler(db).Handle(
            new PauseProductionOrderCommand(f.Order.Id, "machine breakdown"), CancellationToken.None);

        var dto = await ResumeHandler(db).Handle(
            new ResumeProductionOrderCommand(f.Order.Id, f.Messages[0].Id, f.Item.Id, 500m, null),
            CancellationToken.None);

        // The previous cycle's history is intact...
        var preserved = await db.ProductionOrderStageExecutions
            .AsNoTracking().SingleAsync(s => s.ProductionOrderId == f.Order.Id);
        preserved.Status.Should().Be(StageExecutionStatus.Completed);
        preserved.BaselineKg.Should().Be(783m, "the baseline stays fixed for waste calculation");
        preserved.LossPercentKg.Should().Be(1.02m);

        // ...and it is reported back as the resume basis, measured against the BASELINE
        // (the old code divided by the output, which understated the waste).
        dto.PreviousCycleLossPercent.Should().Be(1.02m);
    }

    [Fact]
    public async Task Pause_Then_Resume_KeepsTheLedgerConsistent_WithNoForgottenQuantity()
    {
        var f = Seed(lots: 1, issuedKgPerLot: 800m);
        using var db = NewDb();

        await PauseHandler(db).Handle(
            new PauseProductionOrderCommand(f.Order.Id, "stop", f.Messages[0].Id, f.Item.Id, ReleaseKg: 300m),
            CancellationToken.None);

        OutstandingKg(db, f.Order.Id).Should().Be(500m, "800 issued minus 300 returned");

        await ResumeHandler(db).Handle(
            new ResumeProductionOrderCommand(f.Order.Id, f.Messages[0].Id, f.Item.Id, 500m, null),
            CancellationToken.None);

        OutstandingKg(db, f.Order.Id).Should().Be(1000m, "500 outstanding plus the 500 re-issued");
    }

    [Fact]
    public void Pause_RequiresAReason_AndRejectsNegativeQuantities()
    {
        var validator = new PauseProductionOrderCommandValidator();

        validator.Validate(new PauseProductionOrderCommand(Guid.NewGuid(), "   "))
            .IsValid.Should().BeFalse("a pause must be explained");

        validator.Validate(new PauseProductionOrderCommand(Guid.NewGuid(), "ok", ReleaseKg: -1m))
            .IsValid.Should().BeFalse("a negative return is never valid");

        // Zero is explicitly allowed: pausing with nothing going back to stock.
        validator.Validate(new PauseProductionOrderCommand(Guid.NewGuid(), "ok"))
            .IsValid.Should().BeTrue();
    }
}
