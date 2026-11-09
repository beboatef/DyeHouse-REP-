using DyeHouseERP.API.Controllers;
using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.Inventory.DTOs;
using DyeHouseERP.Application.Inventory.Queries;
using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Enums;
using DyeHouseERP.Domain.Exceptions;
using DyeHouseERP.Persistence;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using Xunit;

namespace DyeHouseERP.UnitTests;

/// <summary>
/// STOCK IS PER WAREHOUSE. One receipt message (raw) or one job order (ready) whose stock sits in two
/// warehouses is TWO balances, two running balances, two item cards and two report rows - never one
/// combined figure named after whichever warehouse happened to come first.
///
/// The warehouse is part of the balance's INTERNAL grouping key only: it is not a lot number, not a roll
/// number, not a document number, it is never stored, and nothing here changes how fabric is received,
/// issued, produced or owned. These tests therefore assert the figures, not a new operational concept.
///
/// Nothing needs SQL Server: the rules are the application's own, over an in-memory ledger.
/// </summary>
public class WarehouseBalanceIndependenceTests
{
    private static ApplicationDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"warehouse-independence-{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new ApplicationDbContext(options);
    }

    private static InventoryTransaction Row(
        DocumentType type, string number, Guid documentId, DateTime date, Warehouse warehouse,
        Customer customer, Item item, Guid? messageId, Guid? orderId,
        decimal? kg, decimal? meter, TransactionDirection direction) =>
        new(type, number, documentId, date, warehouse.Id, customer.Id, item.Id,
            rawMessageId: messageId, productionOrderId: orderId,
            quantityKg: kg, quantityMeter: meter, direction: direction, createdBy: "clerk");

    // =============================================================== raw material, two warehouses

    /// <summary>
    /// ONE receipt message, TWO raw warehouses. Warehouse A holds 100 and issued 40; warehouse B holds the
    /// 20 that came back into it. The screen used to show ONE row of 80 labelled with A, and a running
    /// balance that crossed both warehouses (100 → 60 → 80).
    /// </summary>
    private static async Task<(ApplicationDbContext Db, Warehouse WhA, Warehouse WhB, RawMessage Message, Item Item)>
        SeedRawMessageInTwoWarehousesAsync()
    {
        var db = CreateDb();
        var customer = new Customer("CUST-W1", "عميل مخزنين", "seed");
        var item = new Item("IT-W1", "قطن", UnitOfMeasure.KG, "seed", secondaryUnit: UnitOfMeasure.Meter);
        var whA = new Warehouse("WH-RAW-A", "مخزن خام أ", WarehouseKind.RawMaterial, "seed");
        var whB = new Warehouse("WH-RAW-B", "مخزن خام ب", WarehouseKind.RawMaterial, "seed");
        var message = new RawMessage("MSG-W1", new DateTime(2026, 10, 1), customer.Id, whA.Id, "seed", "seed");

        db.AddRange(customer, item, whA, whB, message);
        db.InventoryTransactions.AddRange(
            // A: received 100/50, issued 40/20 to production.
            Row(DocumentType.RawReceiptMessage, message.MessageNumber, message.Id, new DateTime(2026, 10, 1),
                whA, customer, item, message.Id, null, 100m, 50m, TransactionDirection.In),
            Row(DocumentType.ProductionOrder, "PRD-W1", Guid.NewGuid(), new DateTime(2026, 10, 3),
                whA, customer, item, message.Id, null, 40m, 20m, TransactionDirection.Out),
            // B: 20/10 came back into the OTHER warehouse. Same message, same item, same customer.
            Row(DocumentType.CustomerReturn, "RET-W1", Guid.NewGuid(), new DateTime(2026, 10, 4),
                whB, customer, item, message.Id, null, 20m, 10m, TransactionDirection.In));

        await db.SaveChangesAsync();
        return (db, whA, whB, message, item);
    }

    [Fact]
    public async Task OneMessageInTwoWarehouses_IsTwoBalancesWithTwoRunningBalances()
    {
        var (db, whA, whB, _, item) = await SeedRawMessageInTwoWarehousesAsync();
        using var _db = db;

        var res = await new GetStockTraceabilityQueryHandler(db)
            .Handle(new GetStockTraceabilityQuery(Scope: StockScopes.Raw), CancellationToken.None);

        res.Rows.Should().HaveCount(2, "the same message in two warehouses is two independent balances");
        res.Rows.Select(r => r.LotKey).Should().OnlyHaveUniqueItems();

        var rowA = res.Rows.Single(r => r.WarehouseId == whA.Id);
        var rowB = res.Rows.Single(r => r.WarehouseId == whB.Id);

        rowA.RemainingKg.Should().Be(60m);
        rowA.RemainingMeter.Should().Be(30m);
        rowB.RemainingKg.Should().Be(20m, "warehouse B holds only what came into it");
        rowB.RemainingMeter.Should().Be(10m);
        rowB.ReceivedKg.Should().BeNull("nothing was received into B - the return is B's own row");
        rowB.ReturnedKg.Should().Be(20m);

        // The total across the two warehouses is honest BECAUSE the two rows are separate.
        res.TotalRemainingKg.Should().Be(80m);

        // Each warehouse's movements carry that warehouse only, and the running balance never crosses.
        var movementsA = res.Movements.Where(m => m.WarehouseId == whA.Id).OrderBy(m => m.Date).ToList();
        var movementsB = res.Movements.Where(m => m.WarehouseId == whB.Id).ToList();
        movementsA.Should().HaveCount(2);
        movementsB.Should().ContainSingle();
        movementsA[0].BalanceAfterKg.Should().Be(100m);
        movementsA[1].BalanceAfterKg.Should().Be(60m);
        movementsB.Single().BalanceAfterKg.Should().Be(20m, "B's running balance starts from B's own stock, not A's 100");
        movementsB.Single().BalanceAfterMeter.Should().Be(10m);
        res.Movements.Should().HaveCount(3);

        // Filtering by one warehouse returns that warehouse's balance and nothing else.
        var onlyB = await new GetStockTraceabilityQueryHandler(db)
            .Handle(new GetStockTraceabilityQuery(Scope: StockScopes.Raw, WarehouseId: whB.Id), CancellationToken.None);
        var onlyBRow = onlyB.Rows.Should().ContainSingle().Subject;
        onlyBRow.WarehouseId.Should().Be(whB.Id);
        onlyBRow.RemainingKg.Should().Be(20m);
        onlyB.TotalRemainingKg.Should().Be(20m);
        onlyB.Movements.Should().ContainSingle();
    }

    // ================================================================= ready goods, two warehouses

    [Fact]
    public async Task OneJobOrderInTwoReadyWarehouses_IsTwoBalances()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"ready-warehouse-independence-{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        using var db = new ApplicationDbContext(options);

        var customer = new Customer("CUST-W2", "عميل جاهز", "seed");
        var item = new Item("IT-W2", "قماش جاهز", UnitOfMeasure.KG, "seed");
        var whA = new Warehouse("WH-RDY-A", "جاهز أ", WarehouseKind.ReadyGoods, "seed");
        var whB = new Warehouse("WH-RDY-B", "جاهز ب", WarehouseKind.ReadyGoods, "seed");
        var order = new ProductionOrder("PRD-W2", customer.Id, item.Id, new DateTime(2026, 10, 2), "seed",
            requestedQuantityKg: 150m);

        db.AddRange(customer, item, whA, whB, order);
        db.InventoryTransactions.AddRange(
            Row(DocumentType.ReadyGoodsTransfer, "RGT-W2-A", Guid.NewGuid(), new DateTime(2026, 10, 5),
                whA, customer, item, null, order.Id, 100m, null, TransactionDirection.In),
            Row(DocumentType.ReadyGoodsTransfer, "RGT-W2-B", Guid.NewGuid(), new DateTime(2026, 10, 6),
                whB, customer, item, null, order.Id, 50m, null, TransactionDirection.In),
            Row(DocumentType.Delivery, "DEL-W2", Guid.NewGuid(), new DateTime(2026, 10, 7),
                whA, customer, item, null, order.Id, 30m, null, TransactionDirection.Out));
        await db.SaveChangesAsync();

        var res = await new GetStockTraceabilityQueryHandler(db)
            .Handle(new GetStockTraceabilityQuery(Scope: StockScopes.Ready), CancellationToken.None);

        res.Rows.Should().HaveCount(2);
        res.Rows.Single(r => r.WarehouseId == whA.Id).RemainingKg.Should().Be(70m);
        res.Rows.Single(r => r.WarehouseId == whB.Id).RemainingKg.Should().Be(50m);
        res.TotalRemainingKg.Should().Be(120m);

        // Category of movement is unchanged by the warehouse dimension: A produced, A delivered, B produced.
        res.Movements.Where(m => m.WarehouseId == whA.Id).Select(m => m.Kind)
            .Should().BeEquivalentTo(new[] { "Produced", "Delivered" });
        res.Movements.Single(m => m.WarehouseId == whB.Id).BalanceAfterKg.Should().Be(50m);
    }

    // ======================================================================== item card per warehouse

    [Fact]
    public async Task ItemCard_ReadsOnlyItsOwnWarehouse_WithOpeningAndClosingBalances()
    {
        var (db, whA, whB, _, item) = await SeedRawMessageInTwoWarehousesAsync();
        using var _db = db;
        var handler = new GetItemCardQueryHandler(db);

        // A card for warehouse A, starting after the receipt: the receipt is the OPENING balance and the
        // issue is the only movement of the period, so closing = 100 - 40 = 60.
        var cardA = await handler.Handle(
            new GetItemCardQuery(WarehouseId: whA.Id, ItemId: item.Id, From: new DateTime(2026, 10, 2)),
            CancellationToken.None);

        cardA.WarehouseId.Should().Be(whA.Id);
        cardA.Scope.Should().Be(StockScopes.Raw);
        cardA.OpeningKg.Should().Be(100m);
        cardA.OpeningMeter.Should().Be(50m);
        cardA.InKg.Should().Be(0m);
        cardA.OutKg.Should().Be(40m);
        cardA.OutMeter.Should().Be(20m);
        cardA.ClosingKg.Should().Be(60m, "closing = opening + in - out, for THIS warehouse");
        cardA.ClosingMeter.Should().Be(30m);
        cardA.Movements.Should().ContainSingle();
        cardA.Movements.Should().OnlyContain(m => m.WarehouseId == whA.Id);
        cardA.Movements.Single().BalanceAfterKg.Should().Be(60m);

        // The card for B is a DIFFERENT balance, and it never borrows A's opening or A's movements.
        var cardB = await handler.Handle(
            new GetItemCardQuery(WarehouseId: whB.Id, ItemId: item.Id), CancellationToken.None);
        cardB.WarehouseId.Should().Be(whB.Id);
        cardB.OpeningKg.Should().Be(0m, "warehouse B had nothing before its own first movement");
        cardB.InKg.Should().Be(20m);
        cardB.ClosingKg.Should().Be(20m);
        cardB.Movements.Should().ContainSingle();
        cardB.Movements.Single().WarehouseId.Should().Be(whB.Id);
        cardB.Movements.Single().BalanceAfterKg.Should().Be(20m);
    }

    [Fact]
    public async Task ItemCard_KeepsKgAndMeterIndependent_AndRefusesAForeignIdentifier()
    {
        var (db, whA, _, _, item) = await SeedRawMessageInTwoWarehousesAsync();
        using var _db = db;
        var handler = new GetItemCardQueryHandler(db);

        var card = await handler.Handle(
            new GetItemCardQuery(WarehouseId: whA.Id, ItemId: item.Id), CancellationToken.None);

        // KG and Meter are summed in their own columns - never converted, never added together.
        card.InKg.Should().Be(100m);
        card.InMeter.Should().Be(50m);
        card.ClosingKg.Should().Be(60m);
        card.ClosingMeter.Should().Be(30m);
        card.IsMaterials.Should().BeFalse();

        // A material identifier on a fabric card is REFUSED rather than ignored: ignoring it would answer
        // about some other stock entirely.
        var act = () => handler.Handle(
            new GetItemCardQuery(WarehouseId: whA.Id, ItemId: item.Id, MaterialId: Guid.NewGuid()),
            CancellationToken.None);
        await act.Should().ThrowAsync<DomainException>();
    }

    [Fact]
    public async Task ItemCard_ForAMaterialsWarehouse_ReadsTheMaterialLedgerInItsOwnUnit()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"material-card-{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        using var db = new ApplicationDbContext(options);

        var material = new Material("MAT-W3", "صبغة", MaterialUnit.KG, 10m, "seed");
        var wh = new Warehouse("WH-MAT", "المواد والكيماويات", WarehouseKind.Materials, "seed");
        var other = new Warehouse("WH-SUP", "مستلزمات التشغيل", WarehouseKind.OperatingSupplies, "seed");
        db.AddRange(material, wh, other);
        db.MaterialTransactions.AddRange(
            new MaterialTransaction(DocumentType.PurchaseReceipt, "PR-W3", Guid.NewGuid(), new DateTime(2026, 10, 1),
                material.Id, wh.Id, null, 100m, MaterialTransactionDirection.In, 10m, "clerk"),
            new MaterialTransaction(DocumentType.MaterialIssue, "MI-W3", Guid.NewGuid(), new DateTime(2026, 10, 3),
                material.Id, wh.Id, null, 30m, MaterialTransactionDirection.Out, null, "operator"),
            // The same material bought into the OTHER warehouse: it must never appear on this card.
            new MaterialTransaction(DocumentType.PurchaseReceipt, "PR-W3-SUP", Guid.NewGuid(), new DateTime(2026, 10, 2),
                material.Id, other.Id, null, 500m, MaterialTransactionDirection.In, 10m, "clerk"));
        await db.SaveChangesAsync();

        var card = await new GetItemCardQueryHandler(db).Handle(
            new GetItemCardQuery(WarehouseId: wh.Id, MaterialId: material.Id), CancellationToken.None);

        card.IsMaterials.Should().BeTrue();
        card.Scope.Should().Be(StockScopes.Materials);
        card.OpeningQuantity.Should().Be(0m);
        card.InQuantity.Should().Be(100m);
        card.OutQuantity.Should().Be(30m);
        card.ClosingQuantity.Should().Be(70m, "the 500 in the supplies warehouse is another balance");
        card.ClosingKg.Should().BeNull("materials are not fabric: no KG/Meter pair is invented for them");
        card.ClosingMeter.Should().BeNull();
        card.Movements.Should().HaveCount(2);
        card.Movements.Should().OnlyContain(m => m.WarehouseId == wh.Id);
        card.Movements.Single(m => m.Direction == "Out").BalanceAfter.Should().Be(70m);
    }

    // ================================================================== wiring + report totals

    [Fact]
    public async Task TheItemCardEndpoint_IsWiredToThePerWarehouseQuery()
    {
        var (db, whA, _, _, item) = await SeedRawMessageInTwoWarehousesAsync();
        using var _db = db;

        var handler = new GetItemCardQueryHandler(db);
        var sender = new Mock<ISender>(MockBehavior.Strict);
        sender.Setup(s => s.Send(It.IsAny<GetItemCardQuery>(), It.IsAny<CancellationToken>()))
            .Returns<GetItemCardQuery, CancellationToken>((q, ct) => handler.Handle(q, ct));

        var controller = new InventoryTraceabilityController(sender.Object);
        var result = await controller.GetItemCard(whA.Id, item.Id, null, null, null, null, null, null);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var card = ok.Value.Should().BeOfType<ItemCardDto>().Subject;
        card.WarehouseId.Should().Be(whA.Id);
        card.ClosingKg.Should().Be(60m);
        card.Movements.Should().OnlyContain(m => m.WarehouseId == whA.Id);
    }

    [Fact]
    public async Task WarehouseBalancesReport_ListsEachWarehouseWithItsOwnTotal_ThenSeparateGrandTotals()
    {
        var (db, whA, whB, message, _) = await SeedRawMessageInTwoWarehousesAsync();
        using var _db = db;

        var handler = new GetStockTraceabilityQueryHandler(db);
        var sender = new Mock<ISender>(MockBehavior.Strict);
        sender.Setup(s => s.Send(It.IsAny<GetStockTraceabilityQuery>(), It.IsAny<CancellationToken>()))
            .Returns<GetStockTraceabilityQuery, CancellationToken>((q, ct) => handler.Handle(q, ct));

        var export = new CapturingReportExportService();
        var controller = new ReportsController(sender.Object, export);
        var result = await controller.ExportWarehouseBalances(null, null, "pdf");
        result.Should().BeOfType<FileContentResult>();

        // One row per warehouse for the SAME receipt message - never a combined row.
        var dataRows = export.Rows.Where(r => (string?)r[1] != "إجمالي عام" && !((string?)r[1] ?? "").StartsWith("إجمالي"))
            .ToList();
        dataRows.Should().HaveCount(2);
        dataRows.Should().OnlyContain(r => (string?)r[2] == message.MessageNumber);
        dataRows.Select(r => (string?)r[0]).Should().BeEquivalentTo(new[] { whA.Name, whB.Name });
        dataRows.Single(r => (string?)r[0] == whA.Name)[6].Should().Be(60m);
        dataRows.Single(r => (string?)r[0] == whB.Name)[6].Should().Be(20m);

        // A total for EACH warehouse, so a warehouse total is a figure that exists in that warehouse.
        var warehouseTotals = export.Rows.Where(r => ((string?)r[1] ?? "").StartsWith("إجمالي — ")).ToList();
        warehouseTotals.Should().HaveCount(2);
        warehouseTotals.Single(r => (string?)r[0] == whA.Name)[6].Should().Be(60m);
        warehouseTotals.Single(r => (string?)r[0] == whB.Name)[6].Should().Be(20m);

        // And one grand total, stated separately: 80 KG / 40 M, not two ledgers merged into one figure.
        var grand = export.Rows.Where(r => (string?)r[1] == "إجمالي عام").ToList();
        grand.Should().ContainSingle();
        grand[0][6].Should().Be(80m);
        grand[0][7].Should().Be(40m);
    }
}
