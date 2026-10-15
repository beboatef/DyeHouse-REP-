using DyeHouseERP.Application.Common.Exceptions;
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
/// Finding 2 (sub-customer delivery permits and double stock deduction).
///
/// The chain under test: primary customer -> sub-customer (stored parent link) -> production order ->
/// PRIMARY permit (which owns the physical inventory movement) -> SUB-customer permit (a linked
/// document that owns no movement of its own).
///
/// The rule these tests pin, from spec section 31 and the screen's own footnote
/// ("الإذن الفرعي ... ولا يُنشئ أي حركة مخزنية"):
///   * the primary permit is the ONLY document that deducts ready stock;
///   * a sub-customer permit may reference the primary permit and the SAME production order the
///     primary permit delivered - the order belongs to the PRIMARY customer, so the ownership check
///     must accept that relationship (and still refuse an unrelated customer's order);
///   * issuing a sub-customer permit changes no ledger row and no balance;
///   * marking a sub-customer permit delivered, or correcting it, posts no stock - the lot must never
///     be deducted a second time;
///   * retries never duplicate the primary movement (idempotency token).
/// </summary>
public class SubCustomerPermitDeliveryTests
{
    private readonly DbContextOptions<Persistence.ApplicationDbContext> _options;

    public SubCustomerPermitDeliveryTests()
    {
        _options = new DbContextOptionsBuilder<Persistence.ApplicationDbContext>()
            .UseInMemoryDatabase($"subpermit-{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
    }

    private Persistence.ApplicationDbContext NewDb() => new(_options);

    private sealed record Scene(
        Guid PrimaryCustomerId, Guid SubCustomerId, Guid ThirdCustomerId, Guid ItemId, Guid OrderId,
        Guid ReadyWarehouseId, Guid PrimaryPermitId);

    /// <summary>
    /// Primary customer P with a Job Order, a sub-customer S (stored parent link) and a third,
    /// unrelated customer Z. 500 KG of ready goods sit on the order's lot, and the primary permit
    /// (issued to P, line = the same order) is PREPARED and ready to be delivered.
    /// </summary>
    private Scene Seed()
    {
        var primary = new Customer("C-P", "Primary Buyer", "seed");
        var sub = new Customer("C-S", "Sub Buyer", "seed");
        var third = new Customer("C-Z", "Unrelated Buyer", "seed");
        sub.SetParent(primary.Id, "seed");

        var item = new Item("IT-1", "Fabric", UnitOfMeasure.KG, "seed");
        var warehouse = new Warehouse("RDY", "Ready Goods", WarehouseKind.ReadyGoods, "seed");
        var order = new ProductionOrder("PRD-P-1", primary.Id, item.Id, DateTime.UtcNow, "seed", requestedQuantityKg: 600m);

        using var db = NewDb();
        db.Customers.AddRange(primary, sub, third);
        db.Items.Add(item);
        db.Warehouses.Add(warehouse);
        db.ProductionOrders.Add(order);

        // The ready-goods IN the primary permit will deduct from (the same ledger dimension
        // ReadyGoodsTransfer posts into: RawMessageId null + ProductionOrderId set).
        db.InventoryTransactions.Add(new InventoryTransaction(
            DocumentType.ReadyGoodsTransfer, "RGT-1", Guid.NewGuid(), DateTime.UtcNow,
            warehouse.Id, primary.Id, item.Id, rawMessageId: null, productionOrderId: order.Id,
            quantityKg: 500m, quantityMeter: null, direction: TransactionDirection.In, createdBy: "seed"));

        var primaryPermit = new Delivery("DLV-P-1", DateTime.UtcNow, primary.Id, "seed");
        primaryPermit.AddLine(order.Id, item.Id, "Red", 400m, null, null, null, null);
        primaryPermit.MarkPrepared();
        db.Deliveries.Add(primaryPermit);
        db.SaveChanges();

        return new Scene(primary.Id, sub.Id, third.Id, item.Id, order.Id, warehouse.Id, primaryPermit.Id);
    }

    private static MarkDeliveryDeliveredCommandHandler Deliverer(Persistence.ApplicationDbContext db) => new(
        db,
        new StubCurrentUser("operator"),
        new FixedClock(),
        new OpenPeriod(),
        new RecordingStockLock());

    private static CreateDeliveryCommandHandler Creator(Persistence.ApplicationDbContext db) =>
        Creator(db, new FixedNumberGenerator());

    private static CreateDeliveryCommandHandler Creator(Persistence.ApplicationDbContext db, IDocumentNumberGenerator numbers) => new(
        db, new StubCurrentUser("sales"), numbers);

    private static decimal ReadyBalance(Persistence.ApplicationDbContext db, Scene scene) =>
        db.InventoryTransactions
            .Where(t => t.RawMessageId == null && t.ProductionOrderId == scene.OrderId && t.ItemId == scene.ItemId)
            .Select(t => new { t.QuantityKg, t.Direction })
            .AsEnumerable()
            .Sum(t => (t.QuantityKg ?? 0m) * (int)t.Direction);

    /// <summary>The primary permit delivers 400 KG of the 500 KG lot: one physical delivery, one movement.</summary>
    private async Task DeliverPrimaryPermitAsync(Scene scene, Guid token)
    {
        using var db = NewDb();
        await Deliverer(db).Handle(new MarkDeliveryDeliveredCommand(scene.PrimaryPermitId, token), CancellationToken.None);
    }

    private Delivery NewSubCustomerPermit(Scene scene, decimal kg)
    {
        // Built directly as the database state would hold it: the permit the UI issued against the
        // primary one, carrying the same production order. Isolates the DELIVERY behaviour below.
        var permit = new Delivery($"DLV-S-{Guid.NewGuid():N}"[..16], DateTime.UtcNow, scene.SubCustomerId, "sales",
            primaryDeliveryId: scene.PrimaryPermitId, primaryCustomerId: scene.PrimaryCustomerId);
        permit.AddLine(scene.OrderId, scene.ItemId, "Red", kg, null, null, null, null);
        permit.MarkPrepared();

        using var db = NewDb();
        db.Deliveries.Add(permit);
        db.SaveChanges();
        return permit;
    }

    // ------------------------------------------------------------------ issuing the permit

    [Fact]
    public async Task SubCustomerPermit_OverThePrimaryCustomersOrder_IsAccepted()
    {
        var scene = Seed();
        using var db = NewDb();

        // The order belongs to the PRIMARY customer - exactly the job the primary permit delivered.
        var dto = await Creator(db).Handle(new CreateDeliveryCommand(
            scene.SubCustomerId, DateTime.UtcNow, null,
            new List<DeliveryLineInput>
            {
                new() { ProductionOrderId = scene.OrderId, ItemId = scene.ItemId, QuantityKg = 100m }
            },
            PrimaryDeliveryId: scene.PrimaryPermitId), CancellationToken.None);

        dto.IsSubCustomerPermit.Should().BeTrue();
        dto.PrimaryDeliveryNumber.Should().Be("DLV-P-1");
        dto.CustomerId.Should().Be(scene.SubCustomerId);
        dto.Lines.Should().ContainSingle(l => l.ProductionOrderId == scene.OrderId);
    }

    [Fact]
    public async Task SubCustomerPermit_OverAnUnrelatedCustomersOrder_IsStillRefused()
    {
        var scene = Seed();
        var thirdOrder = new ProductionOrder("PRD-Z-1", scene.ThirdCustomerId, scene.ItemId, DateTime.UtcNow, "seed",
            requestedQuantityKg: 10m);
        using (var seedDb = NewDb())
        {
            seedDb.ProductionOrders.Add(thirdOrder);
            seedDb.SaveChanges();
        }

        using var db = NewDb();
        var act = async () => await Creator(db).Handle(new CreateDeliveryCommand(
            scene.SubCustomerId, DateTime.UtcNow, null,
            new List<DeliveryLineInput>
            {
                new() { ProductionOrderId = thirdOrder.Id, ItemId = scene.ItemId, QuantityKg = 10m }
            },
            PrimaryDeliveryId: scene.PrimaryPermitId), CancellationToken.None);

        (await act.Should().ThrowAsync<DomainException>())
            .Which.Message.Should().Contain("does not belong to the delivery's customer");
    }

    [Fact]
    public async Task SubCustomerPermit_OverTheSubCustomersOwnOrder_IsStillAccepted()
    {
        var scene = Seed();
        var ownOrder = new ProductionOrder("PRD-S-1", scene.SubCustomerId, scene.ItemId, DateTime.UtcNow, "seed",
            requestedQuantityKg: 20m);
        using (var seedDb = NewDb())
        {
            seedDb.ProductionOrders.Add(ownOrder);
            seedDb.SaveChanges();
        }

        using var db = NewDb();
        var dto = await Creator(db).Handle(new CreateDeliveryCommand(
            scene.SubCustomerId, DateTime.UtcNow, null,
            new List<DeliveryLineInput>
            {
                new() { ProductionOrderId = ownOrder.Id, ItemId = scene.ItemId, QuantityKg = 10m }
            },
            PrimaryDeliveryId: scene.PrimaryPermitId), CancellationToken.None);

        dto.IsSubCustomerPermit.Should().BeTrue();
    }

    // ------------------------------------------------------------------ no movement of its own

    [Fact]
    public async Task IssuingASubCustomerPermit_LeavesTheLedgerAndTheReadyBalanceUntouched()
    {
        var scene = Seed();
        await DeliverPrimaryPermitAsync(scene, Guid.NewGuid()); // one real physical delivery, through the primary permit

        Guid[] before;
        decimal balanceBefore;
        using (var db = NewDb())
        {
            before = db.InventoryTransactions.Select(t => t.Id).ToArray();
            balanceBefore = ReadyBalance(db, scene);
            balanceBefore.Should().Be(100m, "500 KG in, 400 KG delivered out by the primary permit");
        }

        using (var db = NewDb())
        {
            await Creator(db).Handle(new CreateDeliveryCommand(
                scene.SubCustomerId, DateTime.UtcNow, null,
                new List<DeliveryLineInput>
                {
                    new() { ProductionOrderId = scene.OrderId, ItemId = scene.ItemId, QuantityKg = 100m }
                },
                PrimaryDeliveryId: scene.PrimaryPermitId), CancellationToken.None);
        }

        using (var verify = NewDb())
        {
            verify.InventoryTransactions.Select(t => t.Id).Should().BeEquivalentTo(before,
                "a sub-customer permit is a document - issuing it posts no ledger row");
            ReadyBalance(verify, scene).Should().Be(balanceBefore);
        }
    }

    [Fact]
    public async Task MarkingASubCustomerPermitDelivered_PostsNoSecondStockMovement()
    {
        var scene = Seed();
        await DeliverPrimaryPermitAsync(scene, Guid.NewGuid());

        var subPermit = NewSubCustomerPermit(scene, kg: 100m);

        decimal balanceBefore;
        int rowsBefore;
        using (var db = NewDb())
        {
            balanceBefore = ReadyBalance(db, scene);
            rowsBefore = db.InventoryTransactions.Count();
        }

        using (var db = NewDb())
        {
            var dto = await Deliverer(db).Handle(new MarkDeliveryDeliveredCommand(subPermit.Id, Guid.NewGuid()), CancellationToken.None);

            dto.Status.Should().Be(DeliveryStatus.Delivered, "the document records its own state");
        }

        using (var verify = NewDb())
        {
            verify.InventoryTransactions.Count().Should().Be(rowsBefore,
                "the physical movement belongs to the PRIMARY permit and must not be posted twice");
            verify.InventoryTransactions.Count(t => t.SourceDocumentId == subPermit.Id).Should().Be(0,
                "a sub-customer permit owns no inventory movement at all");
            ReadyBalance(verify, scene).Should().Be(balanceBefore);
            verify.Deliveries.First(d => d.Id == scene.PrimaryPermitId).Status.Should().Be(DeliveryStatus.Delivered);
        }
    }

    [Fact]
    public async Task CorrectingASubCustomerPermit_PostsNoCompensatingMovement()
    {
        var scene = Seed();
        await DeliverPrimaryPermitAsync(scene, Guid.NewGuid());
        var subPermit = NewSubCustomerPermit(scene, kg: 100m);

        using (var db = NewDb())
            await Deliverer(db).Handle(new MarkDeliveryDeliveredCommand(subPermit.Id, Guid.NewGuid()), CancellationToken.None);

        int rowsBefore;
        using (var db = NewDb()) rowsBefore = db.InventoryTransactions.Count();

        using (var db = NewDb())
        {
            var line = db.DeliveryLines.First(l => l.DeliveryId == subPermit.Id);
            await new EditDeliveredDeliveryCommandHandler(
                    db, new StubCurrentUser("operator"), new FixedClock(), new OpenPeriod(), new RecordingStockLock())
                .Handle(new EditDeliveredDeliveryCommand(subPermit.Id,
                    new List<DeliveryLineQuantity> { new(line.Id, 150m, null) }, "corrected upward"), CancellationToken.None);
        }

        using (var verify = NewDb())
        {
            verify.InventoryTransactions.Count().Should().Be(rowsBefore,
                "correcting a sub-customer permit must not draw stock out - its movement is the primary permit's");
            verify.DeliveryLines.First(l => l.DeliveryId == subPermit.Id).QuantityKg.Should().Be(150m,
                "the corrected figure is still recorded on the document");
        }
    }

    // ------------------------------------------------------------------ idempotency

    [Fact]
    public async Task RetryingThePrimaryDelivery_NeverDeductsTwice()
    {
        var scene = Seed();
        var token = Guid.NewGuid();

        await DeliverPrimaryPermitAsync(scene, token);

        int rowsAfterFirstPost;
        using (var db = NewDb())
        {
            rowsAfterFirstPost = db.InventoryTransactions.Count();
            db.InventoryTransactions.Count(t => t.SourceDocumentType == DocumentType.Delivery).Should().Be(1);
        }

        // The same confirmation sent again, with the same idempotency token.
        using (var db = NewDb())
        {
            var dto = await Deliverer(db).Handle(new MarkDeliveryDeliveredCommand(scene.PrimaryPermitId, token), CancellationToken.None);
            dto.Id.Should().Be(scene.PrimaryPermitId);
        }

        // A second confirmation WITHOUT the token hits the state guard instead of re-posting.
        using (var db = NewDb())
        {
            var act = async () => await Deliverer(db).Handle(
                new MarkDeliveryDeliveredCommand(scene.PrimaryPermitId, Guid.NewGuid()), CancellationToken.None);
            await act.Should().ThrowAsync<DomainException>();
        }

        using (var verify = NewDb())
        {
            verify.InventoryTransactions.Count().Should().Be(rowsAfterFirstPost,
                "a retry must never post the primary movement a second time");
            ReadyBalance(verify, scene).Should().Be(100m);
        }
    }

    [Fact]
    public async Task RetryingASubCustomerPermitDelivery_NeverPostsAMovement_Either()
    {
        var scene = Seed();
        await DeliverPrimaryPermitAsync(scene, Guid.NewGuid());
        var subPermit = NewSubCustomerPermit(scene, kg: 100m);

        int rowsBefore;
        using (var db = NewDb()) rowsBefore = db.InventoryTransactions.Count();

        var token = Guid.NewGuid();
        using (var db = NewDb())
            await Deliverer(db).Handle(new MarkDeliveryDeliveredCommand(subPermit.Id, token), CancellationToken.None);
        using (var db = NewDb())
            await Deliverer(db).Handle(new MarkDeliveryDeliveredCommand(subPermit.Id, token), CancellationToken.None);

        using (var verify = NewDb())
        {
            verify.InventoryTransactions.Count().Should().Be(rowsBefore);
            verify.InventoryTransactions.Count(t => t.SourceDocumentId == subPermit.Id).Should().Be(0);
        }
    }

    // ------------------------------------------------------------------ ordinary permits unchanged

    [Fact]
    public async Task DeliveringAnOrdinaryPermit_StillPostsExactlyOneOutRowPerLine()
    {
        var scene = Seed();

        await DeliverPrimaryPermitAsync(scene, Guid.NewGuid());

        using var verify = NewDb();
        var rows = verify.InventoryTransactions
            .Where(t => t.SourceDocumentType == DocumentType.Delivery && t.SourceDocumentId == scene.PrimaryPermitId)
            .ToList();

        rows.Should().ContainSingle();
        rows[0].Direction.Should().Be(TransactionDirection.Out);
        rows[0].QuantityKg.Should().Be(400m);
        ReadyBalance(verify, scene).Should().Be(100m);
    }

    // ------------------------------------------------------------------ completing on the primary alone

    [Fact]
    public async Task DeliveringWithThePrimaryPermitAlone_CompletesTheDeliveryWithoutAnySubPermit()
    {
        // The required base case: a user never has to create a sub-customer permit at all - the primary
        // permit alone delivers the goods and owns the (single) physical movement.
        var scene = Seed();
        await DeliverPrimaryPermitAsync(scene, Guid.NewGuid());

        using var verify = NewDb();
        verify.Deliveries.Count(d => d.PrimaryDeliveryId != null).Should().Be(0,
            "no sub-customer permit is needed to complete a delivery");
        verify.Deliveries.First(d => d.Id == scene.PrimaryPermitId).Status.Should().Be(DeliveryStatus.Delivered);
        ReadyBalance(verify, scene).Should().Be(100m);
        verify.InventoryTransactions.Count(t => t.SourceDocumentId == scene.PrimaryPermitId).Should().Be(1);
    }

    // ------------------------------------------------------------------ fully manual entry

    [Fact]
    public async Task SubCustomerPermit_CarriesTheFullyManuallyEnteredLineData()
    {
        // The sub-permit's QUANTITIES and its free-text details are typed by the user - never copied
        // from the primary permit - while its item and colour come from the primary permit it slices
        // (the colour is the one that permit recorded, see the colour tests below). Every entered figure
        // must survive the round trip on the document the user created.
        var scene = Seed();
        using var db = NewDb();

        var dto = await Creator(db).Handle(new CreateDeliveryCommand(
            scene.SubCustomerId, DateTime.UtcNow, "manually entered sub permit",
            new List<DeliveryLineInput>
            {
                new()
                {
                    ProductionOrderId = scene.OrderId, ItemId = scene.ItemId, Color = "Red",
                    QuantityKg = 120m, PieceCount = 7, RawOrigin = "Lot A",
                    OriginalRawQuantityKg = 90m
                }
            },
            PrimaryDeliveryId: scene.PrimaryPermitId), CancellationToken.None);

        var line = dto.Lines.Single();
        line.QuantityKg.Should().Be(120m);
        line.Color.Should().Be("Red", "the colour is the primary permit's own, and keeps its spelling");
        line.PieceCount.Should().Be(7);
        line.RawOrigin.Should().Be("Lot A");
        line.OriginalRawQuantityKg.Should().Be(90m);
    }

    // --------------------------------------------- item and colour come from the primary permit

    [Fact]
    public async Task SubCustomerPermit_ForAnItemThePrimaryPermitNeverCarried_IsRefused()
    {
        // The item of a sub-permit line is not an independent choice: it must be one of the items the
        // primary permit actually delivered for that Job Order. A sub-permit may therefore never
        // introduce an item (or any other master data) of its own.
        var scene = Seed();
        var otherItem = new Item("IT-2", "Another fabric", UnitOfMeasure.KG, "seed");
        using (var seedDb = NewDb())
        {
            seedDb.Items.Add(otherItem);
            seedDb.SaveChanges();
        }

        using var db = NewDb();
        var act = async () => await Creator(db).Handle(new CreateDeliveryCommand(
            scene.SubCustomerId, DateTime.UtcNow, null,
            new List<DeliveryLineInput>
            {
                new() { ProductionOrderId = scene.OrderId, ItemId = otherItem.Id, QuantityKg = 10m }
            },
            PrimaryDeliveryId: scene.PrimaryPermitId), CancellationToken.None);

        (await act.Should().ThrowAsync<DomainException>())
            .Which.Message.Should().Contain("not on the primary permit");

        using var verify = NewDb();
        verify.Deliveries.Count(d => d.PrimaryDeliveryId != null).Should().Be(0);
    }

    [Fact]
    public async Task SubCustomerPermit_WithAColourThePrimaryPermitNeverRecorded_IsRefused()
    {
        // The primary permit recorded this Job Order's item in colour "Red"; "Green" is a colour the
        // sub-permit is inventing, so it is refused rather than stored.
        var scene = Seed(); // primary permit line: 400 KG, colour "Red"
        using var db = NewDb();

        var act = async () => await Creator(db).Handle(new CreateDeliveryCommand(
            scene.SubCustomerId, DateTime.UtcNow, null,
            new List<DeliveryLineInput>
            {
                new()
                {
                    ProductionOrderId = scene.OrderId, ItemId = scene.ItemId,
                    Color = "Green", QuantityKg = 10m
                }
            },
            PrimaryDeliveryId: scene.PrimaryPermitId), CancellationToken.None);

        (await act.Should().ThrowAsync<DomainException>())
            .Which.Message.Should().Contain("is not on the primary permit")
            .And.Contain("Red", "the refusal names the colours the primary permit does carry");

        using var verify = NewDb();
        verify.Deliveries.Count(d => d.PrimaryDeliveryId != null).Should().Be(0);
    }

    [Fact]
    public async Task SubCustomerPermit_StoresThePrimaryPermitsOwnColourSpelling()
    {
        // "red" and "Red" are the same colour: a sub-permit must not turn one colour into two rows of
        // master data by re-typing it.
        var scene = Seed();
        using var db = NewDb();

        var dto = await Creator(db).Handle(new CreateDeliveryCommand(
            scene.SubCustomerId, DateTime.UtcNow, null,
            new List<DeliveryLineInput>
            {
                new()
                {
                    ProductionOrderId = scene.OrderId, ItemId = scene.ItemId,
                    Color = "red", QuantityKg = 10m
                }
            },
            PrimaryDeliveryId: scene.PrimaryPermitId), CancellationToken.None);

        dto.Lines.Single().Color.Should().Be("Red", "the primary permit's own spelling is what is stored");
    }

    [Fact]
    public async Task SubCustomerPermit_OnTheSubCustomersOwnOrder_WithSomeoneElsesItem_IsRefused()
    {
        // The sub-customer's OWN Job Order is allowed to appear on their permit, but its item still
        // travels with the Job Order: it is never typed in as an independent choice either.
        var scene = Seed();
        var ownOrder = new ProductionOrder("PRD-S-9", scene.SubCustomerId, scene.ItemId, DateTime.UtcNow, "seed",
            requestedQuantityKg: 20m);
        var otherItem = new Item("IT-3", "Unrelated item", UnitOfMeasure.KG, "seed");
        using (var seedDb = NewDb())
        {
            seedDb.ProductionOrders.Add(ownOrder);
            seedDb.Items.Add(otherItem);
            seedDb.SaveChanges();
        }

        using var db = NewDb();
        var act = async () => await Creator(db).Handle(new CreateDeliveryCommand(
            scene.SubCustomerId, DateTime.UtcNow, null,
            new List<DeliveryLineInput>
            {
                new() { ProductionOrderId = ownOrder.Id, ItemId = otherItem.Id, QuantityKg = 10m }
            },
            PrimaryDeliveryId: scene.PrimaryPermitId), CancellationToken.None);

        (await act.Should().ThrowAsync<DomainException>())
            .Which.Message.Should().Contain("does not belong to Job Order");
    }

    [Fact]
    public async Task SubCustomerPermit_MayCarryMetreAlone_EvenWhenTheItemRequiresBothUnits()
    {
        // "Require both units" is a POSTING rule. A sub-customer permit posts nothing of its own - it is
        // a slice of a permit that already recorded both units - so it must not be blocked from handing
        // over the single unit the sub-customer actually receives.
        var scene = SeedDualUnit(primaryKg: 1000m, primaryMeter: 1000m, requireBothUnits: true);

        using (var db = NewDb())
        {
            // One sub-permit holding a metre-only line AND a KG-only line: both units are optional for
            // the sub-customer, and neither figure is invented from the other.
            var dto = await Creator(db).Handle(new CreateDeliveryCommand(
                scene.SubCustomerId, DateTime.UtcNow, null,
                new List<DeliveryLineInput>
                {
                    new() { ProductionOrderId = scene.OrderId, ItemId = scene.ItemId, QuantityMeter = 400m },
                    new() { ProductionOrderId = scene.OrderId, ItemId = scene.ItemId, QuantityKg = 300m }
                },
                PrimaryDeliveryId: scene.PrimaryPermitId), CancellationToken.None);

            dto.Lines.Should().HaveCount(2);
            dto.Lines.Single(l => l.QuantityMeter != null).QuantityKg.Should().BeNull(
                "a KG figure is never invented to satisfy the item's setting");
            dto.Lines.Single(l => l.QuantityKg != null).QuantityMeter.Should().BeNull(
                "and a metre figure is never invented for the KG-only line");
        }

        // ...and the rule itself is untouched everywhere a real movement is recorded: the very same item
        // on an ORDINARY permit still requires both units - in BOTH directions. The metre-only direction
        // is the one that used to slip through (only the secondary unit was ever checked), so it is
        // asserted here on the real delivery handler, not just on the pure rule.
        foreach (var line in new[]
                 {
                     new DeliveryLineInput { ProductionOrderId = scene.OrderId, ItemId = scene.ItemId, QuantityKg = 500m },
                     new DeliveryLineInput { ProductionOrderId = scene.OrderId, ItemId = scene.ItemId, QuantityMeter = 600m }
                 })
        {
            using var db = NewDb();
            var act = async () => await Creator(db).Handle(new CreateDeliveryCommand(
                scene.PrimaryCustomerId, DateTime.UtcNow, null,
                new List<DeliveryLineInput> { line }), CancellationToken.None);

            (await act.Should().ThrowAsync<DomainException>())
                .Which.Message.Should().Contain("requires BOTH");
        }

        using (var verify = NewDb())
            (await verify.Deliveries.CountAsync()).Should().Be(2,
                "the seeded primary permit and the one sub-permit: the two refused ordinary permits were never created");
    }

    // ------------------------------------------------------------------ the chain is enforced

    [Fact]
    public async Task SubCustomerPermit_WithANonExistentPrimaryPermit_IsRefused()
    {
        // A "sub-permit" is defined only by the primary it is issued against, so a link to a permit that
        // does not exist can never produce one - this is the "no independent sub-permit" rule.
        var scene = Seed();
        using var db = NewDb();

        var act = async () => await Creator(db).Handle(new CreateDeliveryCommand(
            scene.SubCustomerId, DateTime.UtcNow, null,
            new List<DeliveryLineInput>
            {
                new() { ProductionOrderId = scene.OrderId, ItemId = scene.ItemId, QuantityKg = 10m }
            },
            PrimaryDeliveryId: Guid.NewGuid()), CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();

        using var verify = NewDb();
        verify.Deliveries.Count(d => d.PrimaryDeliveryId != null).Should().Be(0);
    }

    [Fact]
    public async Task SubCustomerPermit_ForACustomerThatIsNotASubCustomerOfThePrimary_IsRefused()
    {
        // Linking a wholly unrelated customer to a primary permit would make the word "sub-customer"
        // meaningless and the traceability chain fiction, so the stored parent relationship is required.
        var scene = Seed();
        using var db = NewDb();

        var act = async () => await Creator(db).Handle(new CreateDeliveryCommand(
            scene.ThirdCustomerId, DateTime.UtcNow, null,
            new List<DeliveryLineInput>
            {
                new() { ProductionOrderId = scene.OrderId, ItemId = scene.ItemId, QuantityKg = 10m }
            },
            PrimaryDeliveryId: scene.PrimaryPermitId), CancellationToken.None);

        (await act.Should().ThrowAsync<DomainException>())
            .Which.Message.Should().Contain("not a sub-customer");
    }

    [Fact]
    public async Task SubCustomerPermit_ExceedingThePrimaryPermitsQuantity_IsRefused()
    {
        // A sub-permit slices the primary delivery; it may never promise more than the primary permit
        // actually delivered for that Job Order (here the primary delivered 400 KG).
        var scene = Seed(); // seeded primary permit line = 400 KG
        using var db = NewDb();

        var act = async () => await Creator(db).Handle(new CreateDeliveryCommand(
            scene.SubCustomerId, DateTime.UtcNow, null,
            new List<DeliveryLineInput>
            {
                new() { ProductionOrderId = scene.OrderId, ItemId = scene.ItemId, QuantityKg = 500m }
            },
            PrimaryDeliveryId: scene.PrimaryPermitId), CancellationToken.None);

        (await act.Should().ThrowAsync<DomainException>())
            .Which.Message.Should().Contain("exceeds the primary permit");

        using var verify = NewDb();
        verify.Deliveries.Count(d => d.PrimaryDeliveryId != null).Should().Be(0);
    }

    // ------------------------------------------------------------------ original raw never double-counted

    [Fact]
    public async Task SubCustomerPermit_DoesNotSettleTheOrdersOriginalRaw_SoThePrimaryRemainsFullyAvailable()
    {
        // The defect this pins: a sub-customer permit line carrying an original-raw figure used to be
        // added to the order's "already settled" total even though the sub-permit moves no stock. That
        // let a document-only sub-permit consume the primary permit's real original-raw allowance.
        var scene = Seed(); // the order's fixed original raw = 600 KG

        // A real primary permit that settles 400 KG of the order's original raw, and delivers it.
        var primary = BuildPreparedPermit(scene.PrimaryCustomerId, scene, quantityKg: 400m, originalRawKg: 400m);
        await DeliverPermitAsync(primary.Id, Guid.NewGuid());

        (await RemainingOriginalRawKgAsync(scene, primary.Id)).Should().Be(200m,
            "600 KG started, 400 KG settled by the primary permit");

        // A sub-customer permit recording its own - informational - original-raw share.
        using (var db = NewDb())
            await Creator(db).Handle(new CreateDeliveryCommand(
                scene.SubCustomerId, DateTime.UtcNow, null,
                new List<DeliveryLineInput>
                {
                    new()
                    {
                        ProductionOrderId = scene.OrderId, ItemId = scene.ItemId,
                        QuantityKg = 100m, OriginalRawQuantityKg = 200m
                    }
                },
                PrimaryDeliveryId: primary.Id), CancellationToken.None);

        // Its figure is NOT settled: the order's remaining original raw is unchanged...
        (await RemainingOriginalRawKgAsync(scene, primary.Id)).Should().Be(200m,
            "a sub-customer permit owns no movement, so its original-raw figure may not settle the order");

        // ...so a second, REAL permit may still settle the remaining 200 KG (which the defect refused).
        using var db2 = NewDb();
        var realPermit = await Creator(db2).Handle(new CreateDeliveryCommand(
            scene.PrimaryCustomerId, DateTime.UtcNow, null,
            new List<DeliveryLineInput>
            {
                new()
                {
                    ProductionOrderId = scene.OrderId, ItemId = scene.ItemId,
                    QuantityKg = 200m, OriginalRawQuantityKg = 200m
                }
            }), CancellationToken.None);

        realPermit.IsSubCustomerPermit.Should().BeFalse();
    }

    [Fact]
    public async Task CorrectingASubCustomerPermit_CanChangeItsManualOriginalRaw_AndStillMovesNoStock()
    {
        var scene = Seed();
        await DeliverPrimaryPermitAsync(scene, Guid.NewGuid());
        var subPermit = NewSubCustomerPermit(scene, kg: 100m);

        using (var db = NewDb())
            await Deliverer(db).Handle(new MarkDeliveryDeliveredCommand(subPermit.Id, Guid.NewGuid()), CancellationToken.None);

        int rowsBefore;
        using (var db = NewDb()) rowsBefore = db.InventoryTransactions.Count();

        using (var db = NewDb())
        {
            var line = db.DeliveryLines.First(l => l.DeliveryId == subPermit.Id);
            await new EditDeliveredDeliveryCommandHandler(
                    db, new StubCurrentUser("operator"), new FixedClock(), new OpenPeriod(), new RecordingStockLock())
                .Handle(new EditDeliveredDeliveryCommand(subPermit.Id,
                    new List<DeliveryLineQuantity> { new(line.Id, 100m, null, 75m, null) }, "set the settled raw"),
                    CancellationToken.None);
        }

        using var verify = NewDb();
        verify.DeliveryLines.First(l => l.DeliveryId == subPermit.Id).OriginalRawQuantityKg.Should().Be(75m,
            "the manually entered original-raw figure is editable on the sub-permit");
        verify.InventoryTransactions.Count().Should().Be(rowsBefore,
            "correcting a sub-customer permit still posts no stock");
    }

    // ------------------------------------------------------------------ several sub-permits per primary

    /// <summary>A primary customer, its sub-customer, a dual-unit item (KG + Meter) and a prepared primary permit.</summary>
    private sealed record DualScene(
        Guid PrimaryCustomerId, Guid SubCustomerId, Guid ItemId, Guid OrderId, Guid PrimaryPermitId);

    /// <summary>
    /// Seeds a primary permit over a job whose item uses BOTH KG and Meter (but does not *require*
    /// both), so a sub-permit may legitimately be expressed in Meter alone.
    /// </summary>
    private DualScene SeedDualUnit(decimal primaryKg, decimal primaryMeter, bool requireBothUnits = false)
    {
        var primary = new Customer("C-P2", "Primary Buyer 2", "seed");
        var sub = new Customer("C-S2", "Sub Buyer 2", "seed");
        sub.SetParent(primary.Id, "seed");

        var item = new Item("IT-DUAL", "Dual Unit Fabric", UnitOfMeasure.KG, "seed",
            secondaryUnit: UnitOfMeasure.Meter, requireBothUnits: requireBothUnits);
        var order = new ProductionOrder("PRD-P-2", primary.Id, item.Id, DateTime.UtcNow, "seed",
            requestedQuantityKg: primaryKg, requestedQuantityMeter: primaryMeter);

        using var db = NewDb();
        db.Customers.AddRange(primary, sub);
        db.Items.Add(item);
        db.ProductionOrders.Add(order);

        var permit = new Delivery("DLV-P-2", DateTime.UtcNow, primary.Id, "seed");
        permit.AddLine(order.Id, item.Id, "Blue", primaryKg, primaryMeter, null, null, null);
        permit.MarkPrepared();
        db.Deliveries.Add(permit);
        db.SaveChanges();

        return new DualScene(primary.Id, sub.Id, item.Id, order.Id, permit.Id);
    }

    [Fact]
    public async Task OnePrimaryPermit_CanBeSlicedIntoSeveralSubPermits_UpToItsOwnQuantity()
    {
        // Spec section 31: the number of sub-permits against one primary is NOT fixed. Here the primary
        // delivered 1000 Metre and three metre-only sub-permits distribute exactly that (400 + 350 + 250);
        // the fourth is refused because nothing is left.
        var scene = SeedDualUnit(primaryKg: 1000m, primaryMeter: 1000m);

        var created = new List<Guid>();
        foreach (var meter in new[] { 400m, 350m, 250m })
        {
            using var db = NewDb();
            var dto = await Creator(db).Handle(new CreateDeliveryCommand(
                scene.SubCustomerId, DateTime.UtcNow, null,
                new List<DeliveryLineInput>
                {
                    new() { ProductionOrderId = scene.OrderId, ItemId = scene.ItemId, QuantityMeter = meter }
                },
                PrimaryDeliveryId: scene.PrimaryPermitId), CancellationToken.None);

            dto.IsSubCustomerPermit.Should().BeTrue();
            dto.PrimaryDeliveryNumber.Should().Be("DLV-P-2", "every sub-permit keeps its primary reference");
            dto.Lines.Single().QuantityMeter.Should().Be(meter);
            dto.Lines.Single().QuantityKg.Should().BeNull(
                "a metre-only sub-permit is never given an invented KG quantity");
            created.Add(dto.Id);
        }

        using (var verify = NewDb())
        {
            verify.Deliveries.Count(d => d.PrimaryDeliveryId == scene.PrimaryPermitId).Should().Be(3,
                "three independent sub-permits share one primary permit");
            (await verify.InventoryTransactions.CountAsync()).Should().Be(0,
                "issuing sub-permits moves no stock");
        }

        using (var db = NewDb())
        {
            var act = async () => await Creator(db).Handle(new CreateDeliveryCommand(
                scene.SubCustomerId, DateTime.UtcNow, null,
                new List<DeliveryLineInput>
                {
                    new() { ProductionOrderId = scene.OrderId, ItemId = scene.ItemId, QuantityMeter = 1m }
                },
                PrimaryDeliveryId: scene.PrimaryPermitId), CancellationToken.None);

            (await act.Should().ThrowAsync<DomainException>())
                .Which.Message.Should().Contain("exceeds the primary permit");
        }
    }

    [Fact]
    public async Task SubPermits_InDifferentUnits_AreBoundedIndependently()
    {
        // The KG allowance and the Metre allowance are separate: exhausting Metre through metre-only
        // sub-permits leaves the KG allowance untouched (and the reverse).
        var scene = SeedDualUnit(primaryKg: 500m, primaryMeter: 1000m);

        using (var db = NewDb())
            await Creator(db).Handle(new CreateDeliveryCommand(
                scene.SubCustomerId, DateTime.UtcNow, null,
                new List<DeliveryLineInput>
                {
                    new() { ProductionOrderId = scene.OrderId, ItemId = scene.ItemId, QuantityMeter = 1000m }
                },
                PrimaryDeliveryId: scene.PrimaryPermitId), CancellationToken.None);

        using (var db = NewDb())
        {
            var dto = await Creator(db).Handle(new CreateDeliveryCommand(
                scene.SubCustomerId, DateTime.UtcNow, null,
                new List<DeliveryLineInput>
                {
                    new() { ProductionOrderId = scene.OrderId, ItemId = scene.ItemId, QuantityKg = 500m }
                },
                PrimaryDeliveryId: scene.PrimaryPermitId), CancellationToken.None);

            dto.Lines.Single().QuantityKg.Should().Be(500m,
                "the full KG allowance is still available - Metre never stands in for KG and vice versa");
        }
    }

    [Fact]
    public async Task SubPermit_BeyondTheMetreAllowance_IsRefused_EvenWhenKgRemains()
    {
        var scene = SeedDualUnit(primaryKg: 1000m, primaryMeter: 1000m);

        using (var db = NewDb())
            await Creator(db).Handle(new CreateDeliveryCommand(
                scene.SubCustomerId, DateTime.UtcNow, null,
                new List<DeliveryLineInput>
                {
                    new() { ProductionOrderId = scene.OrderId, ItemId = scene.ItemId, QuantityMeter = 700m }
                },
                PrimaryDeliveryId: scene.PrimaryPermitId), CancellationToken.None);

        using (var db = NewDb())
        {
            var act = async () => await Creator(db).Handle(new CreateDeliveryCommand(
                scene.SubCustomerId, DateTime.UtcNow, null,
                new List<DeliveryLineInput>
                {
                    new() { ProductionOrderId = scene.OrderId, ItemId = scene.ItemId, QuantityMeter = 301m }
                },
                PrimaryDeliveryId: scene.PrimaryPermitId), CancellationToken.None);

            (await act.Should().ThrowAsync<DomainException>())
                .Which.Message.Should().Contain("exceeds the primary permit");
        }
    }

    // ------------------------------------------------------------------ independent numbering

    [Fact]
    public async Task SubCustomerPermits_NumberFromTheirOwnSequence_IndependentOfThePrimary()
    {
        var scene = Seed();
        var numbers = new RecordingNumberGenerator();

        using (var db = NewDb())
            await Creator(db, numbers).Handle(new CreateDeliveryCommand(
                scene.PrimaryCustomerId, DateTime.UtcNow, null,
                new List<DeliveryLineInput>
                {
                    new() { ProductionOrderId = scene.OrderId, ItemId = scene.ItemId, QuantityKg = 50m }
                }), CancellationToken.None);

        var subs = new List<DeliveryDto>();
        foreach (var kg in new[] { 10m, 20m })
        {
            using var db = NewDb();
            subs.Add(await Creator(db, numbers).Handle(new CreateDeliveryCommand(
                scene.SubCustomerId, DateTime.UtcNow, null,
                new List<DeliveryLineInput>
                {
                    new() { ProductionOrderId = scene.OrderId, ItemId = scene.ItemId, QuantityKg = kg }
                },
                PrimaryDeliveryId: scene.PrimaryPermitId), CancellationToken.None));
        }

        numbers.Requested.Should().Equal(
            new[] { DocumentType.Delivery, DocumentType.SubDelivery, DocumentType.SubDelivery },
            "the primary draws from the DEL sequence and every sub-permit from the independent SUB one");
        subs.Should().OnlyContain(s => s.IsSubCustomerPermit);
        subs[0].DeliveryNumber.Should().NotBe(subs[1].DeliveryNumber,
            "two sub-permits of one primary never share a number");
    }

    [Fact]
    public async Task DocumentSequences_SeedAnIndependentSubDeliverySequence()
    {
        using var db = NewDb();
        await DyeHouseERP.Persistence.Numbering.DocumentSequenceSeeder.SeedAsync(db);

        var definitions = await db.DocumentSequenceDefinitions.AsNoTracking().ToListAsync();
        var delivery = definitions.Single(d => d.DocumentType == DocumentType.Delivery);
        var sub = definitions.Single(d => d.DocumentType == DocumentType.SubDelivery);

        sub.Prefix.Should().Be("SUB");
        sub.Prefix.Should().NotBe(delivery.Prefix, "sub-permits must not share the primary prefix");
        delivery.Format(125, 2026).Should().Be("DEL-2026-000125");
        sub.Format(41, 2026).Should().Be("SUB-2026-000041",
            "the sub-permit counter is its own, so it starts at its own value");
    }

    /// <summary>
    /// The DATABASE half of the numbering and uniqueness story (spec sections 6 and 31) - the part a
    /// handler test can never prove, because every handler test injects a stub generator and an
    /// in-memory provider that ignores unique indexes and locks. Pinned straight from the EF model,
    /// with no SQL Server and no migration involved:
    ///
    ///   * a permit number is unique per row (primary or sub), so a clash is impossible in the
    ///     database rather than merely unlikely in code;
    ///   * the retry token is unique only WHERE PRESENT, so a replayed confirmation is idempotent
    ///     while every draft and historical row keeps its NULL;
    ///   * the primary-permit reference is real, indexed, optional and NON-cascading;
    ///   * the engine's counter identity is one row per (type, year, warehouse), so the DEL and SUB
    ///     sequences cannot share a counter - which is what stops two concurrent sub-permits from
    ///     taking the same value, in combination with the generator's sp_getapplock on that key;
    ///   * DocumentType is stored as TEXT in both sequence tables, which is exactly why adding the
    ///     SubDelivery sequence is a seeded DATA row and not a schema change (no migration ships).
    /// </summary>
    [Fact]
    public void Model_PinsPermitUniquenessAndTheIndependentSubSequence()
    {
        using var db = NewDb();
        var model = db.Model;

        var permit = model.FindEntityType(typeof(Delivery))!;

        var numberIndex = permit.GetIndexes().Single(i =>
            i.Properties.Select(p => p.Name).SequenceEqual(new[] { nameof(Delivery.DeliveryNumber) }));
        numberIndex.IsUnique.Should().BeTrue(
            "a duplicated permit number must be rejected by the database, for primary and sub-permits alike");

        var tokenIndex = permit.GetIndexes().Single(i =>
            i.Properties.Select(p => p.Name).SequenceEqual(new[] { nameof(Delivery.DeliverRequestId) }));
        tokenIndex.IsUnique.Should().BeTrue();
        tokenIndex.GetFilter().Should().NotBeNull(
            "the retry token is unique only among the rows that carry one");
        tokenIndex.GetFilter().Should().Contain(nameof(Delivery.DeliverRequestId));

        var primaryId = permit.FindProperty(nameof(Delivery.PrimaryDeliveryId))!;
        primaryId.IsNullable.Should().BeTrue("an ordinary permit references no primary permit at all");
        permit.GetIndexes().Should().Contain(i =>
            i.Properties.Any(p => p.Name == nameof(Delivery.PrimaryDeliveryId)));

        var link = permit.GetForeignKeys().Single(f =>
            f.Properties.Single().Name == nameof(Delivery.PrimaryDeliveryId));
        link.PrincipalEntityType.ClrType.Should().Be(typeof(Delivery),
            "the link must point at the permit aggregate itself, not at some other document");
        link.DeleteBehavior.Should().Be(DeleteBehavior.Restrict,
            "deleting a primary permit must never silently erase the sub-permits issued against it");

        var counter = model.FindEntityType(typeof(DocumentSequenceCounter))!;
        counter.GetIndexes().Single(i => i.Properties.Select(p => p.Name).SequenceEqual(new[]
            {
                nameof(DocumentSequenceCounter.DocumentType), nameof(DocumentSequenceCounter.Year),
                nameof(DocumentSequenceCounter.WarehouseId)
            }))
            .IsUnique.Should().BeTrue("one counter row per sequence key is what makes the sequence airtight");

        var definition = model.FindEntityType(typeof(DocumentSequenceDefinition))!;
        definition.GetIndexes().Single(i =>
                i.Properties.Select(p => p.Name).SequenceEqual(new[] { nameof(DocumentSequenceDefinition.DocumentType) }))
            .IsUnique.Should().BeTrue("one configuration row per document type");

        definition.FindProperty(nameof(DocumentSequenceDefinition.DocumentType))!.GetProviderClrType()
            .Should().Be(typeof(string), "a seeded SubDelivery definition must not need a schema change");
        counter.FindProperty(nameof(DocumentSequenceCounter.DocumentType))!.GetProviderClrType()
            .Should().Be(typeof(string));
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>A prepared permit built directly (as the database would hold it) for a chosen quantity.</summary>
    private Delivery BuildPreparedPermit(Guid customerId, Scene scene, decimal quantityKg, decimal? originalRawKg)
    {
        var permit = new Delivery($"DLV-P-{Guid.NewGuid():N}"[..16], DateTime.UtcNow, customerId, "sales");
        permit.AddLine(scene.OrderId, scene.ItemId, "Red", quantityKg, null, null, null, null, originalRawKg);
        permit.MarkPrepared();

        using var db = NewDb();
        db.Deliveries.Add(permit);
        db.SaveChanges();
        return permit;
    }

    private async Task DeliverPermitAsync(Guid permitId, Guid token)
    {
        using var db = NewDb();
        await Deliverer(db).Handle(new MarkDeliveryDeliveredCommand(permitId, token), CancellationToken.None);
    }

    /// <summary>
    /// The remaining original raw the PRODUCTION query reports for the order - the same figure the
    /// screen and the statement show, so the assertion exercises the real read path, not a re-implementation.
    /// </summary>
    private async Task<decimal> RemainingOriginalRawKgAsync(Scene scene, Guid anyPermitOnTheOrder)
    {
        using var db = NewDb();
        var dto = await new GetDeliveryByIdQueryHandler(db)
            .Handle(new GetDeliveryByIdQuery(anyPermitOnTheOrder), CancellationToken.None);
        return dto.Lines.First(l => l.ProductionOrderId == scene.OrderId).RemainingOriginalRawKg;
    }

    /// <summary>
    /// Records WHICH numbering sequence each created document asked for, so a test can prove the
    /// primary permit and the sub-customer permits draw from different sequences.
    /// </summary>
    private sealed class RecordingNumberGenerator : IDocumentNumberGenerator
    {
        public List<DocumentType> Requested { get; } = new();
        private int _n;

        public Task<string> NextAsync(
            DocumentType documentType, Guid? warehouseId = null, CancellationToken cancellationToken = default)
        {
            Requested.Add(documentType);
            _n++;
            return Task.FromResult($"{documentType}-{_n:D6}");
        }
    }
}
