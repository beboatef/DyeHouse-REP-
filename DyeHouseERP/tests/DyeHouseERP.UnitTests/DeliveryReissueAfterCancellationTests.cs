using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.Deliveries.Commands;
using DyeHouseERP.Application.Deliveries.DTOs;
using DyeHouseERP.Application.Deliveries.Queries;
using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Enums;
using DyeHouseERP.Domain.Exceptions;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace DyeHouseERP.UnitTests;

/// <summary>
/// A cancelled permit must leave the world exactly as usable as it was before it existed: the same
/// ready lot may be delivered again, on a brand new permit.
/// </summary>
public class DeliveryReissueAfterCancellationTests
{
    private readonly DbContextOptions<Persistence.ApplicationDbContext> _options;

    public DeliveryReissueAfterCancellationTests()
    {
        _options = new DbContextOptionsBuilder<Persistence.ApplicationDbContext>()
            .UseInMemoryDatabase($"reissue-{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
    }

    private Persistence.ApplicationDbContext NewDb() => new(_options);

    private sealed record Scene(Guid CustomerId, Guid ItemId, Guid OrderId, Guid ReadyWarehouseId);

    private Scene Seed(decimal readyKg = 500m, decimal requestedKg = 600m)
    {
        var customer = new Customer("C-1", "Buyer", "seed");
        var item = new Item("IT-1", "Fabric", UnitOfMeasure.KG, "seed");
        var warehouse = new Warehouse("RDY", "Ready Goods", WarehouseKind.ReadyGoods, "seed");
        var order = new ProductionOrder("PRD-1", customer.Id, item.Id, DateTime.UtcNow, "seed", requestedQuantityKg: requestedKg);

        using var db = NewDb();
        db.Customers.Add(customer);
        db.Items.Add(item);
        db.Warehouses.Add(warehouse);
        db.ProductionOrders.Add(order);
        db.InventoryTransactions.Add(new InventoryTransaction(
            DocumentType.ReadyGoodsTransfer, "RGT-1", Guid.NewGuid(), DateTime.UtcNow,
            warehouse.Id, customer.Id, item.Id, rawMessageId: null, productionOrderId: order.Id,
            quantityKg: readyKg, quantityMeter: null, direction: TransactionDirection.In, createdBy: "seed"));
        db.SaveChanges();

        return new Scene(customer.Id, item.Id, order.Id, warehouse.Id);
    }

    private static CreateDeliveryCommandHandler Creator(Persistence.ApplicationDbContext db) =>
        new(db, new StubCurrentUser("sales"), new FixedNumberGenerator());

    private static MarkDeliveryPreparedCommandHandler Preparer(Persistence.ApplicationDbContext db) => new(db);

    private static MarkDeliveryDeliveredCommandHandler Deliverer(Persistence.ApplicationDbContext db) => new(
        db, new StubCurrentUser("operator"), new FixedClock(), new OpenPeriod(), new RecordingStockLock());

    private static CancelDeliveryCommandHandler Canceller(Persistence.ApplicationDbContext db) => new(
        db, new StubCurrentUser("manager"), new FixedClock(), new OpenPeriod(), new RecordingStockLock());

    private static async Task<Guid> IssueAsync(
        Persistence.ApplicationDbContext db, Scene scene, decimal kg, decimal? originalRawKg = null)
    {
        var dto = await Creator(db).Handle(new CreateDeliveryCommand(
            scene.CustomerId, new DateTime(2026, 3, 5, 0, 0, 0, DateTimeKind.Utc), null,
            new List<DeliveryLineInput>
            {
                new()
                {
                    ProductionOrderId = scene.OrderId, ItemId = scene.ItemId, Color = "Blue",
                    QuantityKg = kg, PieceCount = 10, OriginalRawQuantityKg = originalRawKg
                }
            }), CancellationToken.None);

        await Preparer(db).Handle(new MarkDeliveryPreparedCommand(dto.Id), CancellationToken.None);
        return dto.Id;
    }

    private static decimal ReadyBalance(Persistence.ApplicationDbContext db, Scene scene) =>
        db.InventoryTransactions
            .Where(t => t.RawMessageId == null && t.ProductionOrderId == scene.OrderId && t.ItemId == scene.ItemId)
            .Select(t => new { t.QuantityKg, t.Direction })
            .AsEnumerable()
            .Sum(t => (t.QuantityKg ?? 0m) * (int)t.Direction);

    [Fact]
    public async Task ACancelledDelivery_DoesNotBlockANewDeliveryOfTheSameLot()
    {
        var scene = Seed();
        using var db = NewDb();

        // 1) A full permit is issued, delivered and then cancelled (its stock comes back).
        var firstId = await IssueAsync(db, scene, kg: 500m, originalRawKg: 600m);
        await Deliverer(db).Handle(new MarkDeliveryDeliveredCommand(firstId, Guid.NewGuid()), CancellationToken.None);
        ReadyBalance(db, scene).Should().Be(0m, "the delivery emptied the ready lot");

        await Canceller(db).Handle(new CancelDeliveryCommand(firstId, "customer refused the goods"), CancellationToken.None);
        ReadyBalance(db, scene).Should().Be(500m, "cancelling reverses the OUT movement");

        // 2) The SAME lot must be deliverable again on a brand new permit.
        var secondId = await IssueAsync(db, scene, kg: 500m, originalRawKg: 600m);
        await Deliverer(db).Handle(new MarkDeliveryDeliveredCommand(secondId, Guid.NewGuid()), CancellationToken.None);

        ReadyBalance(db, scene).Should().Be(0m);
        db.Deliveries.Count(d => d.Status == DeliveryStatus.Delivered).Should().Be(1);
        db.Deliveries.Count(d => d.Status == DeliveryStatus.Cancelled).Should().Be(1);
    }

    [Fact]
    public async Task AfterACancellation_TheOrdersOriginalRawIsFullyAvailableAgain_AndSettlesExactlyOnce()
    {
        var scene = Seed();
        using var db = NewDb();

        var firstId = await IssueAsync(db, scene, kg: 500m, originalRawKg: 600m);
        await Deliverer(db).Handle(new MarkDeliveryDeliveredCommand(firstId, Guid.NewGuid()), CancellationToken.None);

        // The delivered permit settles the order's whole original raw: nothing is outstanding.
        var delivered = await new GetDeliveryByIdQueryHandler(db).Handle(new GetDeliveryByIdQuery(firstId), CancellationToken.None);
        delivered.Lines.Single().RemainingOriginalRawKg.Should().Be(0m);

        await Canceller(db).Handle(new CancelDeliveryCommand(firstId, "customer refused the goods"), CancellationToken.None);

        // A cancelled permit settles NOTHING: the order's original raw is fully available again, so the
        // same goods can be delivered on a new permit without the cancelled one still holding the account.
        var cancelled = await new GetDeliveryByIdQueryHandler(db).Handle(new GetDeliveryByIdQuery(firstId), CancellationToken.None);
        cancelled.Status.Should().Be(DeliveryStatus.Cancelled);
        cancelled.Lines.Single().RemainingOriginalRawKg.Should().Be(600m);

        var secondId = await IssueAsync(db, scene, kg: 500m, originalRawKg: 600m);
        await Deliverer(db).Handle(new MarkDeliveryDeliveredCommand(secondId, Guid.NewGuid()), CancellationToken.None);

        var reissued = await new GetDeliveryByIdQueryHandler(db).Handle(new GetDeliveryByIdQuery(secondId), CancellationToken.None);
        reissued.Lines.Single().RemainingOriginalRawKg.Should().Be(0m,
            "the re-delivery settles the original raw once, exactly as the cancelled one would have");

        // The cancelled permit's stock is reversed exactly once: one OUT and its one reversal, no more.
        db.InventoryTransactions.Count(t => t.SourceDocumentId == firstId).Should().Be(2);
        db.InventoryTransactions
            .Count(t => t.SourceDocumentId == firstId && t.ReversesTransactionId == null)
            .Should().Be(1, "the original movement is kept as history and never edited or deleted");
        ReadyBalance(db, scene).Should().Be(0m);
    }

    [Fact]
    public async Task CancellingTheSamePermitTwice_IsRefused_AndPostsNoSecondReversal()
    {
        var scene = Seed();
        using var db = NewDb();

        var permitId = await IssueAsync(db, scene, kg: 500m, originalRawKg: 600m);
        await Deliverer(db).Handle(new MarkDeliveryDeliveredCommand(permitId, Guid.NewGuid()), CancellationToken.None);
        await Canceller(db).Handle(new CancelDeliveryCommand(permitId, "first cancellation"), CancellationToken.None);

        var rowsAfterFirst = db.InventoryTransactions.Count();
        var balanceAfterFirst = ReadyBalance(db, scene);

        var again = () => Canceller(db).Handle(
            new CancelDeliveryCommand(permitId, "second cancellation"), CancellationToken.None);

        await again.Should().ThrowAsync<DomainException>();
        db.InventoryTransactions.Count().Should().Be(rowsAfterFirst, "a repeated cancel must not reverse the stock twice");
        ReadyBalance(db, scene).Should().Be(balanceAfterFirst,
            "the ready lot was credited back once and stays there");
    }
}
