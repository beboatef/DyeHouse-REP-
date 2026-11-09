using DyeHouseERP.Application.Reports.DTOs;
using DyeHouseERP.Application.Reports.Queries;
using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Enums;
using DyeHouseERP.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace DyeHouseERP.UnitTests;

/// <summary>
/// Approved policy (owner decision): الهالك stays a COMPUTED LOSS and never becomes a posted balance.
///
/// These tests pin both halves of that decision:
///   * the report derives waste from the four streams exactly as the delivery statement does, and
///   * NOTHING is written for it - no ledger row, no document, no balance - so the one thing the
///     report can never do is invent stock.
///
/// They also pin the edge cases the ledger's own rules already impose: no negative waste, no figure
/// without an original raw weight, and no first-grade substitute when the order posted only second
/// grade or separates.
/// </summary>
public class WasteReportTests
{
    private readonly DbContextOptions<ApplicationDbContext> _options;

    public WasteReportTests()
    {
        _options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"waste-report-{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
    }

    private ApplicationDbContext NewDb() => new(_options);

    private sealed class Scene
    {
        public required Customer Customer { get; init; }
        public required Item Item { get; init; }
        public required Warehouse Ready { get; init; }
        public required ProductionStageDefinition Formation { get; init; }
    }

    private static Scene SeedMasterData(ApplicationDbContext db)
    {
        var customer = new Customer("C-1", "Customer", "seed");
        var item = new Item("IT-1", "Item", UnitOfMeasure.KG, "seed");
        var ready = new Warehouse("RDY", "Ready Goods", WarehouseKind.ReadyGoods, "seed");
        var formation = new ProductionStageDefinition("PS001", "التشكيل", 1, "seed", isFormationStage: true);

        db.AddRange(customer, item, ready, formation);
        return new Scene { Customer = customer, Item = item, Ready = ready, Formation = formation };
    }

    private static ProductionOrder NewOrder(
        string number, DateTime date, Scene scene, decimal? requestedKg = 1000m)
        => new(number, scene.Customer.Id, scene.Item.Id, date, "seed", requestedQuantityKg: requestedKg);

    /// <summary>Records the operation's figures the way the transfer workflow does (input, output, separates, second grade).</summary>
    private static void RecordOperation(
        ProductionOrder order, Scene scene, decimal outputKg,
        decimal separatesKg = 0m, decimal secondGradeKg = 0m, decimal inputKg = 1000m)
    {
        order.BuildStageRoute(new[] { scene.Formation }, "seed");
        order.StageExecutions.Single().CloseWithOutput(
            outputKg, null, separatesKg, null, "seed", "seed",
            inputKg: inputKg, secondGradeKg: secondGradeKg);
    }

    /// <summary>Posts a ready-goods transfer and its ledger IN row, exactly as the completion branch does.</summary>
    private static void PostReady(
        ApplicationDbContext db, ProductionOrder order, Scene scene, decimal kg, ReadyGoodsCategory category)
    {
        var transfer = new ReadyGoodsTransfer(
            $"RGT-{Guid.NewGuid():N}", order.OrderDate, order.Id, order.CustomerId, order.ItemId,
            scene.Ready.Id, kg, null, "seed", category: category);

        db.ReadyGoodsTransfers.Add(transfer);
        db.InventoryTransactions.Add(new InventoryTransaction(
            DocumentType.ReadyGoodsTransfer, transfer.TransferNumber, transfer.Id, order.OrderDate,
            scene.Ready.Id, order.CustomerId, order.ItemId, rawMessageId: null, productionOrderId: order.Id,
            quantityKg: kg, quantityMeter: null, direction: TransactionDirection.In, createdBy: "seed",
            readyGoodsCategory: category));
    }

    private static Task<WasteReportDto> RunAsync(
        ApplicationDbContext db, GetWasteReportQuery? query = null)
        => new GetWasteReportQueryHandler(db).Handle(query ?? new GetWasteReportQuery(), CancellationToken.None);

    [Fact]
    public async Task Waste_IsTheFourStreamDifference_AndIsReportedPerOrder()
    {
        await using var db = NewDb();
        var scene = SeedMasterData(db);
        var order = NewOrder("PRD-001", new DateTime(2026, 3, 1), scene);
        RecordOperation(order, scene, outputKg: 900m, separatesKg: 20m, secondGradeKg: 50m);
        db.ProductionOrders.Add(order);
        await db.SaveChangesAsync();

        PostReady(db, order, scene, 900m, ReadyGoodsCategory.FirstGrade);
        PostReady(db, order, scene, 50m, ReadyGoodsCategory.SecondGrade);
        PostReady(db, order, scene, 20m, ReadyGoodsCategory.Separates);
        await db.SaveChangesAsync();

        var report = await RunAsync(db);

        var row = report.Rows.Should().ContainSingle().Subject;
        row.OriginalRawKg.Should().Be(1000m);
        row.FirstGradeReadyKg.Should().Be(900m);
        row.SecondGradeKg.Should().Be(50m);
        row.SeparatesKg.Should().Be(20m);
        row.WasteKg.Should().Be(30m, "1000 - 900 first grade - 50 second grade - 20 separates");
        row.WastePercentKg.Should().Be(3m);
        row.Note.Should().BeNull();

        report.TotalWasteKg.Should().Be(30m);
        report.TotalOriginalRawKg.Should().Be(1000m);
        report.TotalWastePercentKg.Should().Be(3m);
    }

    [Fact]
    public async Task Waste_IsNeverPosted_SoNoLedgerRowOrBalanceCanCarryIt()
    {
        await using var db = NewDb();
        var scene = SeedMasterData(db);
        var order = NewOrder("PRD-002", new DateTime(2026, 3, 2), scene);
        RecordOperation(order, scene, outputKg: 900m, separatesKg: 20m, secondGradeKg: 50m);
        db.ProductionOrders.Add(order);
        await db.SaveChangesAsync();

        PostReady(db, order, scene, 900m, ReadyGoodsCategory.FirstGrade);
        PostReady(db, order, scene, 50m, ReadyGoodsCategory.SecondGrade);
        PostReady(db, order, scene, 20m, ReadyGoodsCategory.Separates);
        await db.SaveChangesAsync();

        var report = await RunAsync(db);
        report.Rows.Single().WasteKg.Should().Be(30m);

        // The 30 KG of waste is real production information and ZERO stock: the ledger still holds only
        // the three posted ready-goods streams (900 + 50 + 20 = 970), never the raw 1000.
        var rows = await db.InventoryTransactions.AsNoTracking().ToListAsync();
        rows.Should().HaveCount(3, "completing an order posts first grade, second grade and separates - and nothing for waste");
        rows.Sum(r => (r.QuantityKg ?? 0m) * (int)r.Direction).Should().Be(970m,
            "waste must never enter the ledger as stock - the raw was already deducted once at issue");
        rows.Should().NotContain(r => r.QuantityKg == 30m);

        // Structural guarantee: there is no category a waste balance could even be filed under, so the
        // report can never quietly become a balance later without this test failing first.
        var categories = Enum.GetNames<ReadyGoodsCategory>();
        categories.Should().BeEquivalentTo(new[] { "FirstGrade", "SecondGrade", "Separates" });

        var transfers = await db.ReadyGoodsTransfers.AsNoTracking().ToListAsync();
        transfers.Should().OnlyContain(t => t.Category != null);
        transfers.Sum(t => t.QuantityKg ?? 0m).Should().Be(970m,
            "no ready-goods transfer document is created for waste");
    }

    [Fact]
    public async Task Waste_IsNull_WhenTheStreamsWouldMakeItNegative()
    {
        await using var db = NewDb();
        var scene = SeedMasterData(db);
        var order = NewOrder("PRD-003", new DateTime(2026, 3, 3), scene);
        // 1000 raw, 980 first grade and 50 second grade = 1030 produced: impossible, and not a waste figure.
        RecordOperation(order, scene, outputKg: 980m, secondGradeKg: 50m);
        db.ProductionOrders.Add(order);
        await db.SaveChangesAsync();

        PostReady(db, order, scene, 980m, ReadyGoodsCategory.FirstGrade);
        await db.SaveChangesAsync();

        var row = (await RunAsync(db)).Rows.Single();
        row.WasteKg.Should().BeNull("a negative difference is a data problem, never a printed waste");
        row.WastePercentKg.Should().BeNull();
        row.Note.Should().NotBeNullOrWhiteSpace("the report explains why the figure is missing");
    }

    [Fact]
    public async Task Waste_FallsBackToTheLastCompletedOperationOutput_WhenNothingWasPosted()
    {
        await using var db = NewDb();
        var scene = SeedMasterData(db);
        var order = NewOrder("PRD-004", new DateTime(2026, 3, 4), scene);
        RecordOperation(order, scene, outputKg: 950m, separatesKg: 10m, secondGradeKg: 40m);
        db.ProductionOrders.Add(order);
        await db.SaveChangesAsync();

        var row = (await RunAsync(db)).Rows.Single();
        row.FirstGradeReadyKg.Should().Be(950m, "the last completed operation's output is the fallback");
        row.WasteKg.Should().Be(0m, "1000 - 950 - 10 - 40");
    }

    [Fact]
    public async Task Waste_IsNull_WhenOnlySecondGradeWasPosted()
    {
        await using var db = NewDb();
        var scene = SeedMasterData(db);
        var order = NewOrder("PRD-005", new DateTime(2026, 3, 5), scene);
        RecordOperation(order, scene, outputKg: 900m, secondGradeKg: 50m);
        db.ProductionOrders.Add(order);
        await db.SaveChangesAsync();

        PostReady(db, order, scene, 50m, ReadyGoodsCategory.SecondGrade);
        await db.SaveChangesAsync();

        var row = (await RunAsync(db)).Rows.Single();
        row.WasteKg.Should().BeNull("second-grade output is not a first-grade substitute");
        row.FirstGradeReadyKg.Should().BeNull();
    }

    [Fact]
    public async Task Waste_IsNull_WhenTheOrderHasNoOriginalRawWeight()
    {
        await using var db = NewDb();
        var scene = SeedMasterData(db);
        var order = NewOrder("PRD-006", new DateTime(2026, 3, 6), scene, requestedKg: null);
        RecordOperation(order, scene, outputKg: 900m);
        db.ProductionOrders.Add(order);
        await db.SaveChangesAsync();

        var row = (await RunAsync(db)).Rows.Single();
        row.WasteKg.Should().BeNull("waste is measured against the original raw weight");
        row.Note.Should().Contain("original raw weight");
    }

    [Fact]
    public async Task Report_HonoursTheDateRange_AndTotalsOnlyThoseOrders()
    {
        await using var db = NewDb();
        var scene = SeedMasterData(db);

        var march = NewOrder("PRD-007", new DateTime(2026, 3, 1), scene);
        RecordOperation(march, scene, outputKg: 900m, secondGradeKg: 50m, separatesKg: 20m);
        var may = NewOrder("PRD-008", new DateTime(2026, 5, 1), scene);
        RecordOperation(may, scene, outputKg: 900m);

        db.ProductionOrders.AddRange(march, may);
        await db.SaveChangesAsync();

        PostReady(db, march, scene, 900m, ReadyGoodsCategory.FirstGrade);
        PostReady(db, may, scene, 900m, ReadyGoodsCategory.FirstGrade);
        await db.SaveChangesAsync();

        var report = await RunAsync(db, new GetWasteReportQuery(
            From: new DateTime(2026, 3, 1), To: new DateTime(2026, 3, 31)));

        report.Rows.Should().ContainSingle().Which.ProductionOrderNumber.Should().Be("PRD-007");
        report.TotalWasteKg.Should().Be(30m, "only the March order is in range");
        report.TotalOriginalRawKg.Should().Be(1000m);
    }
}
