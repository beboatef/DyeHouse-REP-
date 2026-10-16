using DyeHouseERP.Application.Inventory.Queries;
using DyeHouseERP.Application.ProductionOrders.Queries;
using DyeHouseERP.Application.Purchases.Commands;
using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Enums;
using DyeHouseERP.Domain.Exceptions;
using DyeHouseERP.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace DyeHouseERP.UnitTests;

public class StockTraceabilityAndFloorViewsTests
{
    private static ApplicationDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"trace-floor-{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new ApplicationDbContext(options);
    }

    [Fact]
    public async Task RawTraceability_SeparatesMultipleMessagesOfSameItem_TracksMovementsAndRunningBalance()
    {
        using var db = CreateDb();
        var customer = new Customer("CUST-T1", "عميل تتبع", "seed");
        var item = new Item("ITM-T1", "قطن خام", UnitOfMeasure.KG, "seed", secondaryUnit: UnitOfMeasure.Meter);
        var rawWh = new Warehouse("WH-RAW", "مخزن الخام", WarehouseKind.RawMaterial, "seed");
        var readyWh = new Warehouse("WH-RDY", "مخزن الجاهز", WarehouseKind.ReadyGoods, "seed");

        var msg1 = new RawMessage("MSG-001", new DateTime(2026, 10, 1), customer.Id, rawWh.Id, "seed", "seed");
        var msg2 = new RawMessage("MSG-002", new DateTime(2026, 10, 2), customer.Id, rawWh.Id, "seed", "seed");

        var order1 = new ProductionOrder("PO-001", customer.Id, item.Id, new DateTime(2026, 10, 3), "seed",
            requestedQuantityKg: 400m, requestedQuantityMeter: 200m);
        db.RawAllocations.Add(order1.AllocateRaw(msg1.Id, item.Id, 400m, 200m, "seed"));

        db.AddRange(customer, item, rawWh, readyWh, msg1, msg2, order1);

        // Message 1: Receive 1000 KG / 500 M -> Issue 400 KG / 200 M to PO-001 -> Return 50 KG / 25 M
        db.InventoryTransactions.AddRange(
            new InventoryTransaction(
                DocumentType.RawReceiptMessage, msg1.MessageNumber, msg1.Id, new DateTime(2026, 10, 1),
                rawWh.Id, customer.Id, item.Id, rawMessageId: msg1.Id, productionOrderId: null,
                quantityKg: 1000m, quantityMeter: 500m, direction: TransactionDirection.In, createdBy: "user1"),
            new InventoryTransaction(
                DocumentType.ProductionOrder, order1.OrderNumber, order1.Id, new DateTime(2026, 10, 3),
                rawWh.Id, customer.Id, item.Id, rawMessageId: msg1.Id, productionOrderId: order1.Id,
                quantityKg: 400m, quantityMeter: 200m, direction: TransactionDirection.Out, createdBy: "user2"),
            new InventoryTransaction(
                DocumentType.CustomerReturn, "RET-001", Guid.NewGuid(), new DateTime(2026, 10, 4),
                rawWh.Id, customer.Id, item.Id, rawMessageId: msg1.Id, productionOrderId: order1.Id,
                quantityKg: 50m, quantityMeter: 25m, direction: TransactionDirection.In, createdBy: "user1"),
            // Message 2 of the SAME item: Receive 800 KG / 400 M
            new InventoryTransaction(
                DocumentType.RawReceiptMessage, msg2.MessageNumber, msg2.Id, new DateTime(2026, 10, 2),
                rawWh.Id, customer.Id, item.Id, rawMessageId: msg2.Id, productionOrderId: null,
                quantityKg: 800m, quantityMeter: 400m, direction: TransactionDirection.In, createdBy: "user1")
        );

        await db.SaveChangesAsync();

        var handler = new GetStockTraceabilityQueryHandler(db);

        // Full raw view: two separate lot rows for the two messages, never merged
        var allRaw = await handler.Handle(new GetStockTraceabilityQuery(Scope: StockScopes.Raw), CancellationToken.None);
        allRaw.Rows.Should().HaveCount(2);
        allRaw.TotalRemainingKg.Should().Be(650m + 800m);
        allRaw.TotalRemainingMeter.Should().Be(325m + 400m);

        var row1 = allRaw.Rows.Single(r => r.RawMessageId == msg1.Id);
        row1.ReceivedKg.Should().Be(1000m);
        row1.ReceivedMeter.Should().Be(500m);
        row1.IssuedToProductionKg.Should().Be(400m);
        row1.IssuedToProductionMeter.Should().Be(200m);
        row1.ReturnedKg.Should().Be(50m);
        row1.ReturnedMeter.Should().Be(25m);
        row1.UnderProductionKg.Should().Be(350m); // 400 issued - 50 returned on open order
        row1.UnderProductionMeter.Should().Be(175m);
        row1.RemainingKg.Should().Be(650m);
        row1.RemainingMeter.Should().Be(325m);
        row1.OrderNumbers.Should().Be("PO-001");

        // Drilling into Message 1 returns only Message 1's row and its 3 chronological movements with running balances
        var msg1Detail = await handler.Handle(
            new GetStockTraceabilityQuery(Scope: StockScopes.Raw, RawMessageId: msg1.Id), CancellationToken.None);
        msg1Detail.Rows.Should().ContainSingle(r => r.RawMessageId == msg1.Id);
        msg1Detail.Movements.Should().HaveCount(3);
        var chronological = msg1Detail.Movements.OrderBy(m => m.Date).ToList();
        chronological[0].BalanceAfterKg.Should().Be(1000m);
        chronological[1].BalanceAfterKg.Should().Be(600m);
        chronological[2].BalanceAfterKg.Should().Be(650m);
    }

    [Fact]
    public async Task ReadyTraceability_TracksProducedDeliveredReversalsAndPiecesPerProductionOrder()
    {
        using var db = CreateDb();
        var customer = new Customer("CUST-R1", "عميل جاهز", "seed");
        var item = new Item("ITM-R1", "صنف جاهز", UnitOfMeasure.KG, "seed", secondaryUnit: UnitOfMeasure.Meter);
        var readyWh = new Warehouse("WH-RDY", "مخزن الجاهز", WarehouseKind.ReadyGoods, "seed");
        var order = new ProductionOrder("PO-RDY-1", customer.Id, item.Id, new DateTime(2026, 10, 1), "seed",
            requestedQuantityKg: 500m, requestedQuantityMeter: 250m);

        var transfer = new ReadyGoodsTransfer(
            "RGT-001", new DateTime(2026, 10, 2), order.Id, customer.Id, item.Id, readyWh.Id,
            480m, 240m, "seed", pieceCount: 20, notes: null);

        var delivery = new Delivery("DEL-001", new DateTime(2026, 10, 3), customer.Id, "seed", null);
        delivery.AddLine(order.Id, item.Id, "أزرق", 300m, 150m, pieceCount: 12, rawOrigin: null, notes: null);
        delivery.MarkPrepared();
        delivery.MarkDelivered();

        db.AddRange(customer, item, readyWh, order, transfer, delivery);
        db.InventoryTransactions.AddRange(
            new InventoryTransaction(
                DocumentType.ReadyGoodsTransfer, transfer.TransferNumber, transfer.Id, new DateTime(2026, 10, 2),
                readyWh.Id, customer.Id, item.Id, rawMessageId: null, productionOrderId: order.Id,
                quantityKg: 480m, quantityMeter: 240m, direction: TransactionDirection.In, createdBy: "seed"),
            new InventoryTransaction(
                DocumentType.Delivery, delivery.DeliveryNumber, delivery.Id, new DateTime(2026, 10, 3),
                readyWh.Id, customer.Id, item.Id, rawMessageId: null, productionOrderId: order.Id,
                quantityKg: 300m, quantityMeter: 150m, direction: TransactionDirection.Out, createdBy: "seed")
        );

        await db.SaveChangesAsync();

        var res = await new GetStockTraceabilityQueryHandler(db)
            .Handle(new GetStockTraceabilityQuery(Scope: StockScopes.Ready), CancellationToken.None);

        var row = res.Rows.Should().ContainSingle().Subject;
        row.OrderNumber.Should().Be("PO-RDY-1");
        row.ProducedKg.Should().Be(480m);
        row.ProducedMeter.Should().Be(240m);
        row.ProducedPieceCount.Should().Be(20);
        row.DeliveredKg.Should().Be(300m);
        row.DeliveredMeter.Should().Be(150m);
        row.DeliveredPieceCount.Should().Be(12);
        row.DeliveryNumbers.Should().Be("DEL-001");
        row.RemainingKg.Should().Be(180m);
        row.RemainingMeter.Should().Be(90m);
    }

    [Fact]
    public async Task ProductionFloor_HidesDeliveredOrdersFromOpenView_AndShowsThemInDeliveredView()
    {
        using var db = CreateDb();
        var customer = new Customer("CUST-F1", "عميل أرضية", "seed");
        var item = new Item("ITM-F1", "صنف", UnitOfMeasure.KG, "seed");
        var stage1 = new ProductionStageDefinition("ST1", "صباغة", 1, "seed", isFormationStage: true);
        var stage2 = new ProductionStageDefinition("ST2", "تجهيز", 2, "seed", isFormationStage: true);

        var openOrder = new ProductionOrder("PO-OPEN", customer.Id, item.Id, DateTime.UtcNow, "seed", requestedQuantityKg: 200m);
        openOrder.BuildStageRoute(new[] { stage1 }, "seed");
        db.RawAllocations.Add(openOrder.AllocateRaw(Guid.NewGuid(), item.Id, 200m, null, "seed"));

        var deliveredOrder = new ProductionOrder("PO-DELIV", customer.Id, item.Id, DateTime.UtcNow, "seed", requestedQuantityKg: 300m);
        deliveredOrder.BuildStageRoute(new[] { stage2 }, "seed");
        db.RawAllocations.Add(deliveredOrder.AllocateRaw(Guid.NewGuid(), item.Id, 300m, null, "seed"));

        var delivery = new Delivery("DEL-FLR-1", DateTime.UtcNow, customer.Id, "seed", null);
        delivery.AddLine(deliveredOrder.Id, item.Id, null, 290m, null, 10, null, null);
        delivery.MarkPrepared();
        delivery.MarkDelivered();

        db.AddRange(customer, item, stage1, stage2, openOrder, deliveredOrder, delivery);
        await db.SaveChangesAsync();

        var handler = new GetProductionFloorQueryHandler(db);

        var openRows = await handler.Handle(new GetProductionFloorQuery(View: ProductionFloorView.Open), CancellationToken.None);
        openRows.Select(r => r.OrderNumber).Should().ContainSingle().Which.Should().Be("PO-OPEN");

        var deliveredRows = await handler.Handle(new GetProductionFloorQuery(View: ProductionFloorView.Delivered), CancellationToken.None);
        var delRow = deliveredRows.Should().ContainSingle().Subject;
        delRow.OrderNumber.Should().Be("PO-DELIV");
        delRow.IsDelivered.Should().BeTrue();
        delRow.DeliveryNumbers.Should().Be("DEL-FLR-1");
        delRow.DeliveredQuantityKg.Should().Be(290m);

        // Filtering by stage1 returns only openOrder; filtering by stage2 in Open view returns nothing because PO-DELIV is delivered
        var stage1Rows = await handler.Handle(new GetProductionFloorQuery(StageDefinitionId: stage1.Id, View: ProductionFloorView.Open), CancellationToken.None);
        stage1Rows.Select(r => r.OrderNumber).Should().Equal("PO-OPEN");

        var stage2OpenRows = await handler.Handle(new GetProductionFloorQuery(StageDefinitionId: stage2.Id, View: ProductionFloorView.Open), CancellationToken.None);
        stage2OpenRows.Should().BeEmpty();
    }

    [Fact]
    public async Task MaterialStockMovement_RefusesUnitDifferentFromRegisteredMaterialUnit()
    {
        using var db = CreateDb();
        var material = new Material("MAT-L1", "مذيب سائل", MaterialUnit.Liter, 50m, "seed");
        db.Materials.Add(material);
        await db.SaveChangesAsync();

        Func<Task> mismatch = () => PurchaseRules.EnsureMaterialUnitMatchesStockAsync(
            db, material.Id, MaterialUnit.KG, CancellationToken.None);

        await mismatch.Should().ThrowAsync<DomainException>();

        await PurchaseRules.EnsureMaterialUnitMatchesStockAsync(
            db, material.Id, MaterialUnit.Liter, CancellationToken.None);
    }
}
