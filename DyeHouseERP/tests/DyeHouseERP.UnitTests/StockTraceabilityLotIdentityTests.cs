using DyeHouseERP.API.Controllers;
using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.Inventory.DTOs;
using DyeHouseERP.Application.Inventory.Queries;
using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Enums;
using DyeHouseERP.Infrastructure.Services;
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
/// WHICH LOT IS WHICH - the identity rules behind every traceability screen, excel export and printed
/// lot card, exercised over a real (in-memory) ledger so the assertions are about figures the query
/// actually summed, not about a hand-written DTO.
///
/// The bug these pin shut: one raw receipt message with TWO item lines is TWO independent balances, and
/// a card/report that named the first line's item while reporting the combined stock of both, or that
/// drew movement rows without saying which item each belonged to, is a document that misstates stock.
///
/// Nothing here needs SQL Server: the ledger rules are the application's own, and the SQL-only halves
/// (sp_getapplock, rowversion, real row locks) stay in the integration suite, which cannot run without a
/// real database.
/// </summary>
public class StockTraceabilityLotIdentityTests
{
    private static ApplicationDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"lot-identity-{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new ApplicationDbContext(options);
    }

    /// <summary>
    /// ONE receipt message, TWO items: A (KG + Meter) received 1000/500, issued 400/200 to a job order and
    /// took 50/25 back as a customer return; B (KG only, no meter configured at all) received 700. Each
    /// item is its own lot, and each has its own in / out / return history.
    /// </summary>
    private static async Task<(ApplicationDbContext Db, RawMessage Message, Item ItemA, Item ItemB,
        Warehouse Warehouse, Customer Customer)> SeedMessageWithTwoItemsAsync()
    {
        var db = CreateDb();
        var customer = new Customer("CUST-L1", "عميل تتبع", "seed");
        var itemA = new Item("IT-A", "قطن", UnitOfMeasure.KG, "seed", secondaryUnit: UnitOfMeasure.Meter);
        var itemB = new Item("IT-B", "بوليستر", UnitOfMeasure.KG, "seed");
        var warehouse = new Warehouse("WH-RAW", "مخزن الخام", WarehouseKind.RawMaterial, "seed");
        var message = new RawMessage("MSG-L1", new DateTime(2026, 10, 1), customer.Id, warehouse.Id, "seed", "seed");
        var orderA = new ProductionOrder("PRD-L1", customer.Id, itemA.Id, new DateTime(2026, 10, 2), "seed",
            requestedQuantityKg: 400m, requestedQuantityMeter: 200m);

        db.AddRange(customer, itemA, itemB, warehouse, message, orderA);
        db.InventoryTransactions.AddRange(
            // Item A: received 1000 KG / 500 M, then 400 KG / 200 M issued to the job order.
            new InventoryTransaction(
                DocumentType.RawReceiptMessage, message.MessageNumber, message.Id, new DateTime(2026, 10, 1),
                warehouse.Id, customer.Id, itemA.Id, rawMessageId: message.Id, productionOrderId: null,
                quantityKg: 1000m, quantityMeter: 500m, direction: TransactionDirection.In, createdBy: "clerk"),
            new InventoryTransaction(
                DocumentType.ProductionOrder, orderA.OrderNumber, orderA.Id, new DateTime(2026, 10, 3),
                warehouse.Id, customer.Id, itemA.Id, rawMessageId: message.Id, productionOrderId: orderA.Id,
                quantityKg: 400m, quantityMeter: 200m, direction: TransactionDirection.Out, createdBy: "operator"),
            // Item A again: 50 KG / 25 M came back as a customer return, so its balance goes UP.
            new InventoryTransaction(
                DocumentType.CustomerReturn, "RET-L1", Guid.NewGuid(), new DateTime(2026, 10, 4),
                warehouse.Id, customer.Id, itemA.Id, rawMessageId: message.Id, productionOrderId: null,
                quantityKg: 50m, quantityMeter: 25m, direction: TransactionDirection.In, createdBy: "clerk"),
            // Item B, same message: received 700 KG and NO meter at all.
            new InventoryTransaction(
                DocumentType.RawReceiptMessage, message.MessageNumber, message.Id, new DateTime(2026, 10, 1),
                warehouse.Id, customer.Id, itemB.Id, rawMessageId: message.Id, productionOrderId: null,
                quantityKg: 700m, quantityMeter: null, direction: TransactionDirection.In, createdBy: "clerk"));

        await db.SaveChangesAsync();
        return (db, message, itemA, itemB, warehouse, customer);
    }

    // =================================================================== the query

    [Fact]
    public async Task OneMessageWithTwoItems_IsTwoIndependentLots_AndEachMovementCarriesItsOwnLotsKey()
    {
        var (db, message, itemA, itemB, _, _) = await SeedMessageWithTwoItemsAsync();
        using var _db = db;

        var res = await new GetStockTraceabilityQueryHandler(db)
            .Handle(new GetStockTraceabilityQuery(Scope: StockScopes.Raw), CancellationToken.None);

        // Two rows for ONE message: the message is not the lot, the (message, item) pair is.
        res.Rows.Should().HaveCount(2);
        res.Rows.Select(r => r.LotKey).Should().OnlyHaveUniqueItems("a lot key identifies exactly one balance");

        var rowA = res.Rows.Single(r => r.ItemId == itemA.Id);
        var rowB = res.Rows.Single(r => r.ItemId == itemB.Id);
        rowA.RawMessageId.Should().Be(message.Id);
        rowB.RawMessageId.Should().Be(message.Id);

        // Each balance is its own: A keeps 1000 - 400 + 50 = 650 (and 500 - 200 + 25 = 325), while B keeps
        // all 700 it received.
        rowA.RemainingKg.Should().Be(650m);
        rowA.RemainingMeter.Should().Be(325m);
        rowA.ReturnedKg.Should().Be(50m, "a return adds to its own lot only");
        rowA.ReturnedMeter.Should().Be(25m);
        rowB.RemainingKg.Should().Be(700m);
        rowB.ReturnedKg.Should().BeNull("item B had nothing returned - no movement of its own recorded one");
        rowB.RemainingMeter.Should().BeNull("item B never had a meter configured, so it has no meter balance");

        // The message-wide total exists and is the sum of both, which is exactly why no card may print it
        // beside one item's name.
        res.TotalRemainingKg.Should().Be(1350m);

        // Every movement names the balance row it belongs to, and its running balance is that lot's own.
        res.Movements.Should().HaveCount(4);
        res.Movements.Should().OnlyContain(m => res.Rows.Any(r => r.LotKey == m.LotKey));
        var aMovements = res.Movements.Where(m => m.LotKey == rowA.LotKey).ToList();
        var bMovements = res.Movements.Where(m => m.LotKey == rowB.LotKey).ToList();
        aMovements.Should().HaveCount(3, "item A's in, out and return movements");
        bMovements.Should().HaveCount(1);
        aMovements.Should().OnlyContain(m => m.ItemId == itemA.Id);
        bMovements.Should().OnlyContain(m => m.ItemId == itemB.Id);
        bMovements.Should().NotContain(m => m.Kind == "Returned", "the return belongs to item A, not to its sibling");

        var aChronological = aMovements.OrderBy(m => m.Date).ToList();
        aChronological[0].BalanceAfterKg.Should().Be(1000m);
        aChronological[1].BalanceAfterKg.Should().Be(600m, "the running balance belongs to item A's lot only");
        aChronological[2].Kind.Should().Be("Returned");
        aChronological[2].BalanceAfterKg.Should().Be(650m, "the return lands in its own lot's running balance");
        aChronological[2].BalanceAfterMeter.Should().Be(325m);
        bMovements.Single().BalanceAfterKg.Should().Be(700m, "item B's running balance is never item A's");

        // A unit the item does not have is missing, never zero.
        bMovements.Single().BalanceAfterMeter.Should().BeNull();
    }

    [Fact]
    public async Task ReadingOneItemOfAReceiptMessage_ReturnsOnlyThatItemsRowAndMovements()
    {
        var (db, message, itemA, itemB, _, _) = await SeedMessageWithTwoItemsAsync();
        using var _db = db;

        var res = await new GetStockTraceabilityQueryHandler(db).Handle(
            new GetStockTraceabilityQuery(Scope: StockScopes.Raw, RawMessageId: message.Id, ItemId: itemB.Id),
            CancellationToken.None);

        var row = res.Rows.Should().ContainSingle().Subject;
        row.ItemId.Should().Be(itemB.Id);
        row.RemainingKg.Should().Be(700m);
        row.ReturnedKg.Should().BeNull();
        res.TotalRemainingKg.Should().Be(700m, "the total of one lot is that lot's own stock");
        res.Movements.Should().ContainSingle().Which.ItemId.Should().Be(itemB.Id);
        res.Movements.Should().NotContain(m => m.ItemId == itemA.Id, "item A is a different lot of the same message");

        // And item A's own lot keeps all three of its movements, its return among them.
        var resA = await new GetStockTraceabilityQueryHandler(db).Handle(
            new GetStockTraceabilityQuery(Scope: StockScopes.Raw, RawMessageId: message.Id, ItemId: itemA.Id),
            CancellationToken.None);
        resA.Rows.Should().ContainSingle();
        resA.Movements.Should().HaveCount(3);
        resA.Movements.Should().OnlyContain(m => m.ItemId == itemA.Id);
        resA.TotalRemainingKg.Should().Be(650m);
    }

    [Fact]
    public async Task OneMaterialInTwoWarehouses_IsTwoLotsWithDistinctKeys()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"material-identity-{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        using var db = new ApplicationDbContext(options);

        var material = new Material("MAT-L1", "صبغة", MaterialUnit.KG, 10m, "seed");
        var wh1 = new Warehouse("WH-M1", "مخزن كيماويات 1", WarehouseKind.Materials, "seed");
        var wh2 = new Warehouse("WH-M2", "مخزن كيماويات 2", WarehouseKind.Materials, "seed");
        db.AddRange(material, wh1, wh2);
        db.MaterialTransactions.AddRange(
            new MaterialTransaction(DocumentType.PurchaseReceipt, "PR-1", Guid.NewGuid(), new DateTime(2026, 10, 1),
                material.Id, wh1.Id, null, 100m, MaterialTransactionDirection.In, 10m, "clerk"),
            new MaterialTransaction(DocumentType.PurchaseReceipt, "PR-2", Guid.NewGuid(), new DateTime(2026, 10, 2),
                material.Id, wh2.Id, null, 40m, MaterialTransactionDirection.In, 10m, "clerk"));
        await db.SaveChangesAsync();

        var handler = new GetStockTraceabilityQueryHandler(db);

        var both = await handler.Handle(new GetStockTraceabilityQuery(Scope: StockScopes.Materials), CancellationToken.None);
        both.Rows.Should().HaveCount(2, "the same material in two warehouses is two independent balances");
        both.Rows.Select(r => r.LotKey).Should().OnlyHaveUniqueItems();
        both.Rows.Select(r => r.RemainingQuantity).Should().BeEquivalentTo(new decimal?[] { 100m, 40m });

        var one = await handler.Handle(
            new GetStockTraceabilityQuery(Scope: StockScopes.Materials, MaterialId: material.Id, WarehouseId: wh1.Id),
            CancellationToken.None);
        var row = one.Rows.Should().ContainSingle().Subject;
        row.WarehouseId.Should().Be(wh1.Id);
        row.RemainingQuantity.Should().Be(100m, "the warehouse is part of the identity, not a decoration");
        one.Movements.Should().ContainSingle().Which.WarehouseId.Should().Be(wh1.Id);
        one.Movements.Single().LotKey.Should().Be(row.LotKey);
    }

    [Fact]
    public async Task MaterialsAndOperatingSuppliesScopes_KeepTheirLedgerRowsSeparate()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"material-supplies-scope-{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        using var db = new ApplicationDbContext(options);

        var chemical = new Material("MAT-C1", "صبغة", MaterialUnit.KG, 10m, "seed");
        var supply = new Material("SUP-S1", "قطع غيار", MaterialUnit.KG, 10m, "seed");
        supply.SetKind(MaterialKind.OperatingSupply, "seed");
        var chemicalsWarehouse = new Warehouse("WH-MAT", "مخزن المواد والكيماويات", WarehouseKind.Materials, "seed");
        var suppliesWarehouse = new Warehouse("WH-SUP", "مخزن مستلزمات التشغيل", WarehouseKind.OperatingSupplies, "seed");
        db.AddRange(chemical, supply, chemicalsWarehouse, suppliesWarehouse);
        db.MaterialTransactions.AddRange(
            new MaterialTransaction(DocumentType.PurchaseReceipt, "PR-C1", Guid.NewGuid(), new DateTime(2026, 10, 1),
                chemical.Id, chemicalsWarehouse.Id, null, 100m, MaterialTransactionDirection.In, 10m, "clerk"),
            new MaterialTransaction(DocumentType.PurchaseReceipt, "PR-S1", Guid.NewGuid(), new DateTime(2026, 10, 2),
                supply.Id, suppliesWarehouse.Id, null, 25m, MaterialTransactionDirection.In, 10m, "clerk"));
        await db.SaveChangesAsync();

        var handler = new GetStockTraceabilityQueryHandler(db);
        var chemicals = await handler.Handle(
            new GetStockTraceabilityQuery(Scope: StockScopes.Materials), CancellationToken.None);
        var supplies = await handler.Handle(
            new GetStockTraceabilityQuery(Scope: StockScopes.Supplies), CancellationToken.None);

        chemicals.Rows.Should().ContainSingle().Which.MaterialId.Should().Be(chemical.Id);
        chemicals.TotalRemainingQuantity.Should().Be(100m);
        supplies.Rows.Should().ContainSingle().Which.MaterialId.Should().Be(supply.Id);
        supplies.TotalRemainingQuantity.Should().Be(25m);
        supplies.Scope.Should().Be(StockScopes.Supplies);
    }

    // =================================================================== the card, end to end

    /// <summary>
    /// The controller over the REAL query handler and the REAL renderer: no stubbed figures anywhere, so
    /// what comes out is what the ledger says about the lot that was actually named.
    /// </summary>
    [Fact]
    public async Task TheCardForOneItemOfAReceiptMessage_CarriesThatItemsStockAndNoneOfItsSiblings()
    {
        var (db, message, itemA, itemB, _, _) = await SeedMessageWithTwoItemsAsync();
        using var _db = db;

        var handler = new GetStockTraceabilityQueryHandler(db);
        var sender = new Mock<ISender>(MockBehavior.Strict);
        sender.Setup(s => s.Send(It.IsAny<GetStockTraceabilityQuery>(), It.IsAny<CancellationToken>()))
            .Returns<GetStockTraceabilityQuery, CancellationToken>((q, ct) => handler.Handle(q, ct));
        var controller = new InventoryTraceabilityController(sender.Object);

        // ONE lot per card, both cards off the SAME message: each carries its own item, its own movements
        // and its own closing balance, and neither carries a trace of the other's stock.
        var cardB = new CapturingReportExportService();
        var resultB = await controller.ExportLotCard(StockScopes.Raw, message.Id, null, null, itemB.Id, null, null, cardB);
        resultB.Should().BeOfType<FileContentResult>();
        cardB.Subtitle.Should().Contain("IT-B");
        cardB.Subtitle.Should().Contain("700", "item B's own closing balance");
        cardB.Subtitle.Should().NotContain("IT-A", "the sibling item must not appear on item B's card");
        cardB.Rows.Should().ContainSingle();
        cardB.Rows.Single()[2].Should().Be("IT-B - بوليستر");
        cardB.Rows.Single()[6].Should().Be("700", "item B's running balance is never item A's 600");

        var cardA = new CapturingReportExportService();
        await controller.ExportLotCard(StockScopes.Raw, message.Id, null, null, itemA.Id, null, null, cardA);
        cardA.Subtitle.Should().Contain("IT-A");
        cardA.Subtitle.Should().NotContain("IT-B");
        cardA.Subtitle.Should().NotContain("1350", "the message's combined balance is not one item's stock");
        cardA.Subtitle.Should().Contain("650 / 325");
        cardA.Rows.Should().HaveCount(3, "item A's receipt, issue and return");
        cardA.Rows.Should().OnlyContain(row => (string)row[2]! == "IT-A - قطن");
        cardA.Rows.Select(r => (string)r[1]!).Should().ContainInOrder("استلام", "صرف للتشغيل", "مرتجع");

        // The named lot's card is a REAL file from the production renderer, not just a captured shape.
        var fileB = (FileContentResult)await controller.ExportLotCard(
            StockScopes.Raw, message.Id, null, null, itemB.Id, null, null,
            new ReportExportService(new TestCompanyIdentityProvider("شركة تجريبية", "Test Co")));
        System.Text.Encoding.ASCII.GetString(fileB.FileContents, 0, 5).Should().Be("%PDF-");
        fileB.FileContents.Length.Should().BeGreaterThan(1000);
        fileB.FileDownloadName.Should().Be("lot-card-MSG-L1-IT-B.pdf",
            "the name carries the item, so the sibling lot cannot overwrite this file");

        // The other item of the SAME message is a different lot, so it is a different file with a
        // different name - naming both after the message alone is how one overwrote the other.
        var fileA = (FileContentResult)await controller.ExportLotCard(
            StockScopes.Raw, message.Id, null, null, itemA.Id, null, null,
            new ReportExportService(new TestCompanyIdentityProvider("شركة تجريبية", "Test Co")));
        fileA.FileDownloadName.Should().Be("lot-card-MSG-L1-IT-A.pdf");
        fileA.FileDownloadName.Should().NotBe(fileB.FileDownloadName,
            "two items of one receipt message must never download under one name");
    }
}
