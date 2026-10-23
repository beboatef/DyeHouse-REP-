using DyeHouseERP.Application.Common.Exceptions;
using DyeHouseERP.Application.CustomerReturns.Commands;
using DyeHouseERP.Application.Deliveries.Commands;
using DyeHouseERP.Application.Deliveries.DTOs;
using DyeHouseERP.Application.Deliveries.Queries;
using DyeHouseERP.Application.ReadyGoods.Commands;
using DyeHouseERP.Application.ReadyGoods.Queries;
using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Enums;
using DyeHouseERP.Domain.Exceptions;
using DyeHouseERP.Persistence;
using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using Xunit;

namespace DyeHouseERP.UnitTests;

/// <summary>
/// Spec sections 29-33: the Phase 5 surface - Ready Goods warehouse movements, PARTIAL delivery and
/// its statement (بيان تسليم), the sub-customer permit, and customer returns (including a new Job
/// Order raised from one).
///
/// These tests pin the rules that make those documents trustworthy, so the exact things a wrong
/// implementation would silently get wrong are asserted explicitly:
///   * a ready-goods-to-raw move is the ONLY way stock leaves Ready Goods, posts two ledger rows, and
///     can never overdraw the ready lot;
///   * the statement never converts KG into Meter, reports null (never a fabricated zero) for a unit
///     that was not recorded, and prints no negative "waste";
///   * a partial delivery can only settle what the order's FIXED original raw quantity still has
///     outstanding;
///   * a sub-customer permit links to a real primary permit AND a real stored parent relationship;
///   * a return lands only in raw material stock, records who owns the material, and never touches an
///     invoice; a Job Order raised from it must state who carries the cost.
/// </summary>
public abstract class Phase5TestBase
{
    private readonly DbContextOptions<ApplicationDbContext> _options;

    protected Phase5TestBase()
    {
        _options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"phase5-{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
    }

    protected ApplicationDbContext NewDb() => new(_options);

    // ------------------------------------------------------------- shared fixture

    protected sealed class Scene
    {
        public required Customer Customer { get; init; }
        public required Item Item { get; init; }
        public required Warehouse Raw { get; init; }
        public required Warehouse Ready { get; init; }
        public required ProductionOrder Order { get; init; }
        public required RawMessage Lot { get; init; }
    }

    /// <summary>
    /// One customer with an order and a raw lot, plus both a raw-material and a ready-goods
    /// warehouse - the minimum world every Phase 5 document needs to exist in.
    /// </summary>
    protected Scene Seed(
        string orderNumber = "PRD-2026-0001", string lotNumber = "RM-2026-0001",
        decimal requestedKg = 600m, decimal? requestedMeter = 100m,
        // When set, the order's formation stage is closed with these figures in the SAME context the
        // order is created in - the shape a floor operation leaves behind for the statement to read.
        decimal? stageOutputKg = null, decimal? stageOutputMeter = null,
        decimal? separatesKg = null, decimal? separatesMeter = null,
        decimal? secondGradeKg = null, decimal? secondGradeMeter = null)
    {
        using var db = NewDb();

        var customer = new Customer("C-1", "Customer One", "seed");
        // KG primary + Meter secondary, but NOT RequireBothUnits: a movement may record either unit
        // on this item, which is what lets us prove the two units are handled independently.
        var item = new Item("IT-1", "Fabric", UnitOfMeasure.KG, "seed", secondaryUnit: UnitOfMeasure.Meter);
        var raw = new Warehouse("RAW", "Raw Material", WarehouseKind.RawMaterial, "seed");
        var ready = new Warehouse("RDY", "Ready Goods", WarehouseKind.ReadyGoods, "seed");

        db.Customers.Add(customer);
        db.Items.Add(item);
        db.Warehouses.AddRange(raw, ready);

        var order = new ProductionOrder(
            orderNumber, customer.Id, item.Id, new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc), "seed",
            color: "Blue", requestedQuantityKg: requestedKg, requestedQuantityMeter: requestedMeter);
        db.ProductionOrders.Add(order);

        var lot = new RawMessage(
            lotNumber, new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc), customer.Id, raw.Id, "receiver", "seed");
        lot.AddLine(item.Id, requestedKg, requestedMeter, null, null);
        db.RawMessages.Add(lot);

        if (stageOutputKg.HasValue || stageOutputMeter.HasValue)
        {
            var formation = new ProductionStageDefinition("PS002", "التشكيل", 2, "seed", isFormationStage: true);
            db.ProductionStageDefinitions.Add(formation);
            order.BuildStageRoute(new[] { formation }, "seed");
            order.StageExecutions.Single().CloseWithOutput(
                stageOutputKg, stageOutputMeter, separatesKg, separatesMeter, "recorded", "operator",
                secondGradeKg: secondGradeKg, secondGradeMeter: secondGradeMeter);
        }

        db.SaveChanges();

        return new Scene { Customer = customer, Item = item, Raw = raw, Ready = ready, Order = order, Lot = lot };
    }

    /// <summary>Seeds an IN ledger row on the ready lot so the ready balance is what the test expects.</summary>
    protected static void SeedReadyBalance(
        ApplicationDbContext db, Scene scene, decimal kg, decimal? meter, string documentNumber = "RGT-SEED-1")
    {
        db.InventoryTransactions.Add(new InventoryTransaction(
            DocumentType.ReadyGoodsTransfer, documentNumber, Guid.NewGuid(),
            new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc), scene.Ready.Id, scene.Customer.Id, scene.Item.Id,
            rawMessageId: null, productionOrderId: scene.Order.Id,
            quantityKg: kg, quantityMeter: meter, direction: TransactionDirection.In, createdBy: "seed"));
        db.SaveChanges();
    }
}

// ============================================================================================
//  Ready Goods -> raw material (spec section 29): the only authorized way finished stock
//  becomes raw stock again - authorized, reasoned, traceable and audited.
// ============================================================================================

public class Phase5ReadyGoodsRawReturnTests : Phase5TestBase
{
    private static ReturnReadyGoodsToRawStockCommandHandler Handler(ApplicationDbContext db, string user = "supervisor")
        => new(db, new StubCurrentUser(user), new FixedNumberGenerator(), new FixedClock(), new OpenPeriod(),
            new RecordingStockLock());

    [Fact]
    public void TheValidator_RefusesAMovementWithoutAReason()
    {
        var validator = new ReturnReadyGoodsToRawStockCommandValidator();

        validator.Validate(new ReturnReadyGoodsToRawStockCommand(
                Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 10m, null, Reason: ""))
            .IsValid.Should().BeFalse("moving posted stock back to raw is never silent");

        validator.Validate(new ReturnReadyGoodsToRawStockCommand(
                Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 10m, null, Reason: "reclassify for rework"))
            .IsValid.Should().BeTrue();
    }

    [Fact]
    public void TheValidator_RefusesAQuantityThatIsNeitherKgNorMeter()
    {
        new ReturnReadyGoodsToRawStockCommandValidator()
            .Validate(new ReturnReadyGoodsToRawStockCommand(
                Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, null, "reason"))
            .IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task TheMovement_PostsOneOutOfReadyGoods_AndOneIntoTheRawLot()
    {
        var scene = Seed();
        using var db = NewDb();
        SeedReadyBalance(db, scene, kg: 500m, meter: 80m);

        var dto = await Handler(db).Handle(new ReturnReadyGoodsToRawStockCommand(
            scene.Order.Id, scene.Item.Id, scene.Lot.Id, 120m, 30m,
            "return to raw for rework"), CancellationToken.None);

        dto.MovementNumber.Should().Be("ReadyGoodsRawReturn-TEST-1");
        dto.DestinationRawMessageId.Should().Be(scene.Lot.Id);
        dto.SourceWarehouseId.Should().Be(scene.Ready.Id);

        var movement = await db.ReadyGoodsRawReturns.SingleAsync();
        movement.Reason.Should().Be("return to raw for rework");
        movement.IsLocked.Should().BeTrue("a posted correction document is finalized immediately");

        var ledger = await db.InventoryTransactions
            .Where(t => t.SourceDocumentType == DocumentType.ReadyGoodsRawReturn).ToListAsync();

        ledger.Should().HaveCount(2, "the stock leaves one warehouse and arrives in the other in one transaction");

        var outRow = ledger.Single(t => t.Direction == TransactionDirection.Out);
        outRow.RawMessageId.Should().BeNull("ready stock is the (order, item) dimension, not a raw lot");
        outRow.ProductionOrderId.Should().Be(scene.Order.Id);
        outRow.WarehouseId.Should().Be(scene.Ready.Id);
        outRow.QuantityKg.Should().Be(120m);

        var inRow = ledger.Single(t => t.Direction == TransactionDirection.In);
        inRow.RawMessageId.Should().Be(scene.Lot.Id);
        inRow.WarehouseId.Should().Be(scene.Raw.Id);
        inRow.CustomerId.Should().Be(scene.Customer.Id);
        inRow.QuantityMeter.Should().Be(30m);

        (await db.AuditLogEntries.CountAsync(a => a.Action == "ReturnReadyGoodsToRaw")).Should().Be(1);
    }

    [Fact]
    public async Task TheMovement_CanNeverOverdrawTheReadyLot()
    {
        var scene = Seed();
        using var db = NewDb();
        SeedReadyBalance(db, scene, kg: 100m, meter: null);

        var act = () => Handler(db).Handle(new ReturnReadyGoodsToRawStockCommand(
            scene.Order.Id, scene.Item.Id, scene.Lot.Id, 150m, null, "too much"), CancellationToken.None);

        await act.Should().ThrowAsync<NegativeStockException>();

        (await db.ReadyGoodsRawReturns.CountAsync()).Should().Be(0);
        (await db.InventoryTransactions.CountAsync(t =>
            t.SourceDocumentType == DocumentType.ReadyGoodsRawReturn)).Should().Be(0);
    }

    [Fact]
    public async Task TheMovement_RefusesToCreditAnotherCustomersLot()
    {
        var scene = Seed();
        using var db = NewDb();
        SeedReadyBalance(db, scene, kg: 500m, meter: null);

        var other = new Customer("C-2", "Someone Else", "seed");
        db.Customers.Add(other);
        var foreignLot = new RawMessage("RM-2026-0099", DateTime.UtcNow, other.Id, scene.Raw.Id, "receiver", "seed");
        foreignLot.AddLine(scene.Item.Id, 100m, null, null, null);
        db.RawMessages.Add(foreignLot);
        await db.SaveChangesAsync();

        var act = () => Handler(db).Handle(new ReturnReadyGoodsToRawStockCommand(
            scene.Order.Id, scene.Item.Id, foreignLot.Id, 10m, null, "wrong lot"), CancellationToken.None);

        (await act.Should().ThrowAsync<DomainException>())
            .WithMessage("*belongs to a different customer*");

        (await db.ReadyGoodsRawReturns.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task TheMovement_RefusesARequestedItemThatIsNotOnTheDestinationLot()
    {
        var scene = Seed();
        using var db = NewDb();

        var otherItem = new Item("IT-2", "Different fabric", UnitOfMeasure.KG, "seed");
        var otherOrder = new ProductionOrder(
            "PRD-2026-0002", scene.Customer.Id, otherItem.Id,
            new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc), "seed",
            color: "Red", requestedQuantityKg: 100m);
        db.Items.Add(otherItem);
        db.ProductionOrders.Add(otherOrder);
        await db.SaveChangesAsync();

        var act = () => Handler(db).Handle(new ReturnReadyGoodsToRawStockCommand(
            otherOrder.Id, otherItem.Id, scene.Lot.Id, 10m, null, "item not on lot"), CancellationToken.None);

        (await act.Should().ThrowAsync<DomainException>())
            .WithMessage("*does not contain the item being returned*");

        (await db.ReadyGoodsRawReturns.CountAsync()).Should().Be(0);
        (await db.InventoryTransactions.CountAsync(t =>
            t.SourceDocumentType == DocumentType.ReadyGoodsRawReturn)).Should().Be(0);
    }

    [Fact]
    public async Task TheMovement_RefusesALotThatIsNotInRawMaterialStock()
    {
        var scene = Seed();
        using var db = NewDb();
        SeedReadyBalance(db, scene, kg: 500m, meter: null);

        // A lot parked in a MATERIALS warehouse - the destination has to be raw material, because the
        // whole point of the document is that the stock changes sides.
        var materials = new Warehouse("MAT", "Materials", WarehouseKind.Materials, "seed");
        db.Warehouses.Add(materials);
        var wrongLot = new RawMessage("RM-2026-0100", DateTime.UtcNow, scene.Customer.Id, materials.Id, "receiver", "seed");
        wrongLot.AddLine(scene.Item.Id, 100m, null, null, null);
        db.RawMessages.Add(wrongLot);
        await db.SaveChangesAsync();

        var act = () => Handler(db).Handle(new ReturnReadyGoodsToRawStockCommand(
            scene.Order.Id, scene.Item.Id, wrongLot.Id, 10m, null, "wrong warehouse"), CancellationToken.None);

        (await act.Should().ThrowAsync<DomainException>())
            .WithMessage("*not in a raw material warehouse*");
    }

    [Fact]
    public async Task TheRegister_ReadsBackEveryPostedMovement()
    {
        var scene = Seed();
        using var db = NewDb();
        SeedReadyBalance(db, scene, kg: 500m, meter: 90m);

        await Handler(db).Handle(new ReturnReadyGoodsToRawStockCommand(
            scene.Order.Id, scene.Item.Id, scene.Lot.Id, 40m, 10m, "reclassify"), CancellationToken.None);

        var rows = await new GetReadyGoodsRawReturnsQueryHandler(db)
            .Handle(new GetReadyGoodsRawReturnsQuery(), CancellationToken.None);

        var row = rows.Should().ContainSingle().Subject;
        row.ProductionOrderNumber.Should().Be("PRD-2026-0001");
        row.CustomerCode.Should().Be("C-1");
        row.DestinationMessageNumber.Should().Be("RM-2026-0001");
        row.SourceWarehouseName.Should().Be("Ready Goods");
        row.DestinationWarehouseName.Should().Be("Raw Material");
        row.Reason.Should().Be("reclassify");
    }
}

// ============================================================================================
//  Delivery: partial delivery + the delivery statement (بيان تسليم) + the sub-customer permit.
// ============================================================================================

public class Phase5DeliveryStatementTests : Phase5TestBase
{
    private static GetDeliveryStatementQueryHandler StatementHandler(ApplicationDbContext db) => new(db);

    private static async Task<Guid> SeedDeliveryAsync(
        ApplicationDbContext db, Scene scene,
        decimal readyKg, decimal? readyMeter,
        decimal? originalRawKg, decimal? originalRawMeter)
    {
        var delivery = new Delivery(
            "DLV-2026-0001", new DateTime(2026, 3, 5, 0, 0, 0, DateTimeKind.Utc), scene.Customer.Id, "seed");
        delivery.AddLine(scene.Order.Id, scene.Item.Id, "Blue", readyKg, readyMeter, 12, "RM-2026-0001", null,
            originalRawQuantityKg: originalRawKg, originalRawQuantityMeter: originalRawMeter);

        db.Deliveries.Add(delivery);
        await db.SaveChangesAsync();
        return delivery.Id;
    }

    [Fact]
    public async Task TheStatement_ReportsTheOrdersOriginalQuantity_WhatThisDeliverySettles_AndWhatRemains()
    {
        var scene = Seed(requestedKg: 600m, requestedMeter: 100m);
        using var db = NewDb();
        var deliveryId = await SeedDeliveryAsync(db, scene, readyKg: 500m, readyMeter: 80m,
            originalRawKg: 550m, originalRawMeter: 90m);

        var statement = await StatementHandler(db).Handle(new GetDeliveryStatementQuery(deliveryId), CancellationToken.None);

        var row = statement.Rows.Should().ContainSingle().Subject;
        row.ProductionOrderNumber.Should().Be("PRD-2026-0001");
        row.OriginalRawQuantityKg.Should().Be(600m, "the order's original quantity is fixed and read live");
        row.OriginalRawQuantityMeter.Should().Be(100m);
        row.DeliveredOriginalRawQuantityKg.Should().Be(550m);
        row.RemainingOriginalRawKg.Should().Be(50m, "600 started, 550 settled by this delivery");
        row.RemainingOriginalRawMeter.Should().Be(10m);
        row.ReadyGoodsQuantityKg.Should().Be(500m);
        row.BaleCount.Should().Be(12);
    }

    [Fact]
    public async Task TheStatement_NeverInventsAFigureForAUnitThatWasNotRecorded()
    {
        var scene = Seed(requestedKg: 600m, requestedMeter: null);
        using var db = NewDb();
        // A KG-only permit: nothing was ever measured in Meter for the order.
        var deliveryId = await SeedDeliveryAsync(db, scene, readyKg: 500m, readyMeter: null,
            originalRawKg: 550m, originalRawMeter: null);

        var row = (await StatementHandler(db).Handle(new GetDeliveryStatementQuery(deliveryId), CancellationToken.None))
            .Rows.Single();

        row.OriginalRawQuantityMeter.Should().BeNull();
        row.RemainingOriginalRawMeter.Should().Be(0m);
        row.ScrapsMeter.Should().BeNull("no operation recorded Meter separates");
        row.SecondGradeMeter.Should().BeNull();
        // Ready goods (the produced figure) are unknown here, so the waste cannot be computed - null,
        // not a zero that would read as a real measurement.
        row.CalculatedWasteKg.Should().BeNull();
    }

    [Fact]
    public async Task TheStatement_CalculatesWastePerUnit_FromTheOperationsAndNeverAcrossUnits()
    {
        var scene = Seed(requestedKg: 600m, requestedMeter: 100m,
            stageOutputKg: 520m, stageOutputMeter: 85m,
            separatesKg: 20m, separatesMeter: 5m, secondGradeKg: 10m, secondGradeMeter: 2m);

        using var db = NewDb();
        var deliveryId = await SeedDeliveryAsync(db, scene, readyKg: 500m, readyMeter: 80m,
            originalRawKg: 550m, originalRawMeter: 90m);

        var row = (await StatementHandler(db).Handle(new GetDeliveryStatementQuery(deliveryId), CancellationToken.None))
            .Rows.Single();

        row.ScrapsKg.Should().Be(20m);
        row.ScrapsMeter.Should().Be(5m);
        row.SecondGradeKg.Should().Be(10m);
        row.SecondGradeMeter.Should().Be(2m);

        // KG: 600 original - 520 produced - 20 scraps - 10 second grade = 50 (waste is calculated in KG only).
        row.CalculatedWasteKg.Should().Be(50m);
    }

    [Fact]
    public async Task TheStatement_CarriesEveryPrintRunsDrawingNumber_WithTheOperationsRecordedSeparatesAndSecondGrade()
    {
        // Phase 5 reads the drawing numbers, the separates/scraps and the second grade from the Job
        // Order's OPERATIONS - the exact fields Phase 4's floor capture records. This pins that the two
        // phases agree on the FINAL field set: each print run is reported with its own drawing number,
        // and the scraps/second grade the statement shows are the recorded operation figures, summed
        // per unit (never converted).
        var scene = Seed(requestedKg: 600m, requestedMeter: null,
            stageOutputKg: 520m, separatesKg: 20m, secondGradeKg: 10m);
        using var db = NewDb();

        // A second, PRINTING operation: it requires a drawing number, ran with its own ("DR-B"), and
        // recorded its own separates and second grade - the same shape a real floor transfer leaves.
        var printing = new ProductionStageDefinition("PS010", "الطباعة", 10, "seed", requiresDrawingNumber: true);
        db.ProductionStageDefinitions.Add(printing);
        var printRun = scene.Order.ActivateNextStage(printing, baselineKg: 520m, baselineMeter: null,
            startedBy: "seed", drawingNumber: "DR-B");
        db.ProductionOrderStageExecutions.Add(printRun);
        printRun.CloseWithOutput(510m, null, 5m, null, "second print run", "operator", secondGradeKg: 4m);
        await db.SaveChangesAsync();

        var deliveryId = await SeedDeliveryAsync(db, scene, readyKg: 500m, readyMeter: null,
            originalRawKg: 550m, originalRawMeter: null);

        var row = (await StatementHandler(db).Handle(new GetDeliveryStatementQuery(deliveryId), CancellationToken.None))
            .Rows.Single();

        row.DrawingNumbers.Should().BeEquivalentTo(new[] { "DR-B" },
            "only the printing operation requires a drawing number, and its own run's number is reported");
        row.ScrapsKg.Should().Be(25m, "20 recorded by the formation operation + 5 by the print run");
        row.SecondGradeKg.Should().Be(14m, "10 recorded by the formation operation + 4 by the print run");
        row.ScrapsMeter.Should().BeNull("a unit no operation ever recorded still reports null, never a zero");
    }

    [Fact]
    public async Task PrintingTheStatement_IsReadOnly_AndPostsNoStock()
    {
        var scene = Seed();
        using var db = NewDb();
        var deliveryId = await SeedDeliveryAsync(db, scene, 500m, 80m, 550m, 90m);
        var before = await db.InventoryTransactions.CountAsync();

        await StatementHandler(db).Handle(new GetDeliveryStatementQuery(deliveryId), CancellationToken.None);
        await StatementHandler(db).Handle(new GetDeliveryStatementQuery(deliveryId), CancellationToken.None);

        (await db.InventoryTransactions.CountAsync()).Should().Be(before, "reprinting a statement never moves stock");
    }

    // ------------------------------------------------------------ partial delivery guard

    [Fact]
    public void TheOrder_RefusesADeliveryThatSettlesMoreOriginalRawThanItStartedFrom()
    {
        var order = new ProductionOrder("PRD-1", Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow, "seed",
            requestedQuantityKg: 600m, requestedQuantityMeter: 100m);

        Action exact = () => order.EnsureOriginalRawWithinLimit(0m, 0m, 600m, null);
        exact.Should().NotThrow("settling exactly the original quantity is fine");

        Action over = () => order.EnsureOriginalRawWithinLimit(0m, 0m, 601m, null);
        over.Should().Throw<DomainException>().WithMessage("*exceeds the original raw quantity*");

        // Units are independent: settling all the KG says nothing about the Meter balance.
        Action meterStillOpen = () => order.EnsureOriginalRawWithinLimit(600m, 0m, null, 100m);
        meterStillOpen.Should().NotThrow();
    }

    [Fact]
    public async Task ASecondPartialDelivery_MayOnlySettleTheOutstandingOriginalRaw()
    {
        var scene = Seed(requestedKg: 600m, requestedMeter: null);
        using var db = NewDb();

        // A first delivery already settled 600 KG of the order's original raw.
        await SeedDeliveryAsync(db, scene, readyKg: 500m, readyMeter: null, originalRawKg: 600m, originalRawMeter: null);

        var handler = new CreateDeliveryCommandHandler(db, new StubCurrentUser("sales"), new FixedNumberGenerator());

        var act = () => handler.Handle(new CreateDeliveryCommand(
            scene.Customer.Id, new DateTime(2026, 3, 6, 0, 0, 0, DateTimeKind.Utc), null,
            new List<DeliveryLineInput>
            {
                new()
                {
                    ProductionOrderId = scene.Order.Id, ItemId = scene.Item.Id,
                    QuantityKg = 10m, OriginalRawQuantityKg = 50m
                }
            }), CancellationToken.None);

        (await act.Should().ThrowAsync<DomainException>())
            .WithMessage("*exceeds the original raw quantity*");
    }

    // ------------------------------------------------------------ sub-customer permit

    private static async Task<(Customer Primary, Customer Sub, Delivery PrimaryPermit)> SeedParentAndPermitAsync(
        ApplicationDbContext db)
    {
        var primary = new Customer("C-P", "Primary Buyer", "seed");
        var sub = new Customer("C-S", "Sub Buyer", "seed");
        sub.SetParent(primary.Id, "seed");
        db.Customers.AddRange(primary, sub);

        var item = new Item("IT-9", "Fabric", UnitOfMeasure.KG, "seed");
        db.Items.Add(item);

        var order = new ProductionOrder("PRD-P-1", primary.Id, item.Id, DateTime.UtcNow, "seed", requestedQuantityKg: 100m);
        db.ProductionOrders.Add(order);

        var primaryPermit = new Delivery("DLV-P-1", DateTime.UtcNow, primary.Id, "seed");
        primaryPermit.AddLine(order.Id, item.Id, "Red", 10m, null, null, null, null);
        db.Deliveries.Add(primaryPermit);

        await db.SaveChangesAsync();
        return (primary, sub, primaryPermit);
    }

    [Fact]
    public async Task ASubCustomerPermit_NamesThePrimaryPermit_AndNeverCarriesItsCustomerRecord()
    {
        using var db = NewDb();
        var (primary, sub, primaryPermit) = await SeedParentAndPermitAsync(db);

        var subOrder = new ProductionOrder("PRD-S-1", sub.Id, await db.Items.Select(i => i.Id).FirstAsync(),
            DateTime.UtcNow, "seed", requestedQuantityKg: 50m);
        db.ProductionOrders.Add(subOrder);
        await db.SaveChangesAsync();

        var handler = new CreateDeliveryCommandHandler(db, new StubCurrentUser("sales"), new FixedNumberGenerator());

        var dto = await handler.Handle(new CreateDeliveryCommand(
            sub.Id, DateTime.UtcNow, null,
            new List<DeliveryLineInput>
            {
                new() { ProductionOrderId = subOrder.Id, ItemId = subOrder.ItemId, QuantityKg = 5m }
            },
            PrimaryDeliveryId: primaryPermit.Id), CancellationToken.None);

        dto.IsSubCustomerPermit.Should().BeTrue();
        dto.PrimaryDeliveryNumber.Should().Be("DLV-P-1");
        dto.PrimaryCustomerCode.Should().Be(primary.Code);
        dto.PrimaryCustomerName.Should().Be(primary.Name);
    }

    [Fact]
    public async Task ASubCustomerPermit_IsRefusedWhenTheCustomerIsNotActuallyASubCustomer()
    {
        using var db = NewDb();
        var (_, _, primaryPermit) = await SeedParentAndPermitAsync(db);

        var unrelated = new Customer("C-X", "Unrelated", "seed");
        db.Customers.Add(unrelated);
        var item = await db.Items.FirstAsync();
        var order = new ProductionOrder("PRD-X-1", unrelated.Id, item.Id, DateTime.UtcNow, "seed", requestedQuantityKg: 10m);
        db.ProductionOrders.Add(order);
        await db.SaveChangesAsync();

        var handler = new CreateDeliveryCommandHandler(db, new StubCurrentUser("sales"), new FixedNumberGenerator());

        var act = () => handler.Handle(new CreateDeliveryCommand(
            unrelated.Id, DateTime.UtcNow, null,
            new List<DeliveryLineInput> { new() { ProductionOrderId = order.Id, ItemId = order.ItemId, QuantityKg = 5m } },
            PrimaryDeliveryId: primaryPermit.Id), CancellationToken.None);

        (await act.Should().ThrowAsync<DomainException>())
            .WithMessage("*is not a sub-customer*");
    }

    [Fact]
    public async Task ASubCustomerPermit_IsRefusedAgainstACancelledPrimaryPermit()
    {
        using var db = NewDb();
        var (_, sub, primaryPermit) = await SeedParentAndPermitAsync(db);

        var tracked = await db.Deliveries.SingleAsync(d => d.Id == primaryPermit.Id);
        tracked.Cancel("issued in error", "sales");
        await db.SaveChangesAsync();

        var item = await db.Items.FirstAsync();
        var order = new ProductionOrder("PRD-S-2", sub.Id, item.Id, DateTime.UtcNow, "seed", requestedQuantityKg: 10m);
        db.ProductionOrders.Add(order);
        await db.SaveChangesAsync();

        var handler = new CreateDeliveryCommandHandler(db, new StubCurrentUser("sales"), new FixedNumberGenerator());

        var act = () => handler.Handle(new CreateDeliveryCommand(
            sub.Id, DateTime.UtcNow, null,
            new List<DeliveryLineInput> { new() { ProductionOrderId = order.Id, ItemId = order.ItemId, QuantityKg = 5m } },
            PrimaryDeliveryId: primaryPermit.Id), CancellationToken.None);

        (await act.Should().ThrowAsync<DomainException>())
            .WithMessage("*is cancelled*");
    }
}

// ============================================================================================
//  Customer returns (spec sections 32-33) - raw stock back in, ownership recorded, no invoice.
// ============================================================================================

public class Phase5CustomerReturnTests : Phase5TestBase
{
    private static CreateCustomerReturnCommandHandler ReturnHandler(ApplicationDbContext db)
        => new(db, new StubCurrentUser("gate"), new FixedNumberGenerator(), new FixedClock(), new OpenPeriod(),
            new RecordingStockLock());

    private static CreateCustomerReturnCommand ReturnCommand(
        Scene scene, ReturnOwnership ownership = ReturnOwnership.CustomerOwned, string? referenceDocument = null)
        => new(scene.Customer.Id, new DateTime(2026, 3, 7, 0, 0, 0, DateTimeKind.Utc),
            "customer rejected the batch", null,
            new List<CustomerReturnLineInput>
            {
                new(scene.Lot.Id, scene.Item.Id, 40m, null)
            },
            referenceDocument, ownership);

    [Fact]
    public async Task AReturn_PostsAnInRowIntoRawStock_AndRecordsOwnershipAndTheReferenceDocument()
    {
        var scene = Seed();
        using var db = NewDb();

        var dto = await ReturnHandler(db).Handle(
            ReturnCommand(scene, ReturnOwnership.CompanyOwned, referenceDocument: "INV-2026-77"), CancellationToken.None);

        dto.ReturnNumber.Should().Be("CustomerReturn-TEST-1");
        dto.Ownership.Should().Be(ReturnOwnership.CompanyOwned);
        dto.ReferenceDocument.Should().Be("INV-2026-77");

        var ledger = await db.InventoryTransactions
            .Where(t => t.SourceDocumentType == DocumentType.CustomerReturn).ToListAsync();

        ledger.Should().ContainSingle();
        ledger[0].Direction.Should().Be(TransactionDirection.In);
        ledger[0].RawMessageId.Should().Be(scene.Lot.Id);
        ledger[0].WarehouseId.Should().Be(scene.Raw.Id, "returns land in raw material stock, never in Ready Goods");
        ledger[0].QuantityKg.Should().Be(40m);

        // No invoice, no credit note, no delivery is touched by a return.
        (await db.Invoices.CountAsync()).Should().Be(0);
        (await db.Deliveries.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task AReturn_IsRefusedWhenTheLotIsNotInRawMaterialStock()
    {
        var scene = Seed();
        using var db = NewDb();

        var materials = new Warehouse("MAT", "Materials", WarehouseKind.Materials, "seed");
        db.Warehouses.Add(materials);
        var wrongLot = new RawMessage("RM-2026-0101", DateTime.UtcNow, scene.Customer.Id, materials.Id, "receiver", "seed");
        wrongLot.AddLine(scene.Item.Id, 100m, null, null, null);
        db.RawMessages.Add(wrongLot);
        await db.SaveChangesAsync();

        var command = new CreateCustomerReturnCommand(
            scene.Customer.Id, DateTime.UtcNow, null, null,
            new List<CustomerReturnLineInput> { new(wrongLot.Id, scene.Item.Id, 10m, null) });

        var act = () => ReturnHandler(db).Handle(command, CancellationToken.None);

        (await act.Should().ThrowAsync<DomainException>())
            .WithMessage("*not in a raw material warehouse*");
    }

    [Fact]
    public async Task AReturn_IsRefusedAgainstAnotherCustomersLot()
    {
        var scene = Seed();
        using var db = NewDb();

        var other = new Customer("C-2", "Someone Else", "seed");
        db.Customers.Add(other);
        await db.SaveChangesAsync();

        var command = new CreateCustomerReturnCommand(
            other.Id, DateTime.UtcNow, null, null,
            new List<CustomerReturnLineInput> { new(scene.Lot.Id, scene.Item.Id, 10m, null) });

        var act = () => ReturnHandler(db).Handle(command, CancellationToken.None);

        (await act.Should().ThrowAsync<DomainException>())
            .WithMessage("*another customer*");
    }

    [Fact]
    public void ACancelledReturn_RequiresAReason_AndCannotBeCancelledTwice()
    {
        var ret = new CustomerReturn("CRT-2026-000001", DateTime.UtcNow, Guid.NewGuid(), "gate");

        Action noReason = () => ret.Cancel("  ", "gate");
        noReason.Should().Throw<DomainException>();

        ret.Cancel("wrong lot", "gate");
        ret.IsCancelled.Should().BeTrue();
        ret.CancellationReason.Should().Be("wrong lot");
        ret.CancelledBy.Should().Be("gate");

        Action twice = () => ret.Cancel("again", "gate");
        twice.Should().Throw<DomainException>();
    }

    // ------------------------------------------------- Job Order raised FROM a return

    [Fact]
    public void AJobOrderFromAReturn_MustStateWhoCarriesTheCost()
    {
        var validator = new CreateProductionOrderFromReturnCommandValidator();

        validator.Validate(new CreateProductionOrderFromReturnCommand(
                Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow, 50m, null, null))
            .IsValid.Should().BeFalse("there is no default: the same rework can be billed or absorbed");

        validator.Validate(new CreateProductionOrderFromReturnCommand(
                Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow, 50m, null, CostBearer.Company))
            .IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task AJobOrderFromAReturn_IsRefusedForACancelledReturn()
    {
        var scene = Seed();
        using var db = NewDb();

        var ret = new CustomerReturn("CRT-2026-000002", DateTime.UtcNow, scene.Customer.Id, "gate");
        ret.Cancel("entered in error", "gate");
        db.CustomerReturns.Add(ret);
        await db.SaveChangesAsync();

        // The handler must reject before it ever forwards to the issue workflow.
        var mediator = new Mock<ISender>(MockBehavior.Strict);
        var handler = new CreateProductionOrderFromReturnCommandHandler(db, mediator.Object);

        var act = () => handler.Handle(new CreateProductionOrderFromReturnCommand(
            ret.Id, scene.Item.Id, scene.Lot.Id, DateTime.UtcNow, 50m, null, CostBearer.Customer),
            CancellationToken.None);

        (await act.Should().ThrowAsync<DomainException>())
            .WithMessage("*is cancelled*");

        mediator.VerifyNoOtherCalls();
    }
}
