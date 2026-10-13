using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.RawReceipts.Commands;
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
/// D5 - correcting a raw receipt that was entered in error (wrong customer, item or warehouse).
///
/// A raw receipt is a posted stock document with no draft state, so the correction is a
/// cancellation: the document and its original IN ledger row are preserved and the stock is
/// reversed with an equal-and-opposite OUT row that references the row it reverses. The handler
/// refuses - before touching anything - while material is still held by a Job Order or has
/// already left the lot, never edits a balance and never leaves the receipt cancellable twice.
/// </summary>
public class RawReceiptCancellationTests
{
    private readonly DbContextOptions<ApplicationDbContext> _options;

    public RawReceiptCancellationTests()
    {
        _options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"raw-cancel-{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
    }

    private ApplicationDbContext NewDb() => new(_options);

    private static CancelRawMessageCommandHandler Handler(ApplicationDbContext db) =>
        new(db, new StubCurrentUser("operator"), new FixedClock(), new OpenPeriod(), new RecordingStockLock());

    private sealed record Scene(Customer Customer, Item Item, Warehouse Raw, RawMessage Message);

    /// <summary>A receipt of 100 KG that nothing has touched yet - the only unambiguously reversible case.</summary>
    private static Scene SeedReceipt(ApplicationDbContext db, decimal receivedKg = 100m)
    {
        var customer = new Customer("C-1", "Customer", "seed");
        var item = new Item("IT-1", "Item", UnitOfMeasure.KG, "seed");
        var raw = new Warehouse("RAW", "Raw Material", WarehouseKind.RawMaterial, "seed");

        var message = new RawMessage("RMSG-1", DateTime.UtcNow, customer.Id, raw.Id, "seed", "seed");
        message.AddLine(item.Id, receivedKg, null, null, null);

        db.AddRange(customer, item, raw, message);
        db.InventoryTransactions.Add(new InventoryTransaction(
            DocumentType.RawReceiptMessage, message.MessageNumber, message.Id, DateTime.UtcNow,
            raw.Id, customer.Id, item.Id, rawMessageId: message.Id, productionOrderId: null,
            quantityKg: receivedKg, quantityMeter: null, direction: TransactionDirection.In, createdBy: "seed"));
        db.SaveChanges();

        return new Scene(customer, item, raw, message);
    }

    private static async Task<decimal> LotBalanceKg(ApplicationDbContext db, Guid messageId) =>
        (await db.InventoryTransactions.AsNoTracking().Where(t => t.RawMessageId == messageId).ToListAsync())
        .Sum(t => (t.QuantityKg ?? 0m) * (int)t.Direction);

    // =====================================================================
    // The safe path: an untouched receipt
    // =====================================================================

    [Fact]
    public async Task An_Untouched_Receipt_Is_Cancelled_By_Reversing_Its_Stock_And_Keeping_Its_History()
    {
        using var db = NewDb();
        var scene = SeedReceipt(db);
        var originalRowId = (await db.InventoryTransactions.AsNoTracking().SingleAsync()).Id;

        await Handler(db).Handle(new CancelRawMessageCommand(scene.Message.Id, "wrong customer"), CancellationToken.None);

        var message = await db.RawMessages.AsNoTracking().SingleAsync(m => m.Id == scene.Message.Id);
        message.Status.Should().Be(RawMessageStatus.Cancelled);
        message.CancellationReason.Should().Be("wrong customer");
        message.CancelledBy.Should().Be("operator");
        message.CancelledAtUtc.Should().NotBeNull();
        message.IsAvailableForAllocation.Should().BeFalse("reversed stock must never be allocatable again");

        var rows = await db.InventoryTransactions.AsNoTracking().Where(t => t.RawMessageId == scene.Message.Id).ToListAsync();
        rows.Should().HaveCount(2, "the original IN row is preserved and one reversing OUT row is appended");
        rows.Should().ContainSingle(r => r.Direction == TransactionDirection.In, "history is never rewritten");
        var reversal = rows.Single(r => r.Direction == TransactionDirection.Out);
        reversal.QuantityKg.Should().Be(100m);
        reversal.ReversesTransactionId.Should().Be(originalRowId, "the reversal names the movement it reverses");
        reversal.SourceDocumentId.Should().Be(scene.Message.Id);

        (await LotBalanceKg(db, scene.Message.Id)).Should().Be(0m, "the lot nets to zero without any balance being edited");
    }

    [Fact]
    public async Task Only_The_Accepted_Quantity_Is_Reversed_When_Part_Of_The_Receipt_Was_Rejected()
    {
        using var db = NewDb();
        var scene = SeedReceipt(db, receivedKg: 100m);

        var message = await db.RawMessages.Include(m => m.Lines).SingleAsync(m => m.Id == scene.Message.Id);
        var rejection = message.RecordLineRejection(message.Lines.Single().Id, 40m, null);
        db.InventoryTransactions.Add(new InventoryTransaction(
            DocumentType.RawReceiptMessage, message.MessageNumber, message.Id, DateTime.UtcNow,
            scene.Raw.Id, scene.Customer.Id, scene.Item.Id, rawMessageId: message.Id, productionOrderId: null,
            quantityKg: rejection.Kg, quantityMeter: rejection.Meter,
            direction: TransactionDirection.Out, createdBy: "seed"));
        await db.SaveChangesAsync();

        await Handler(db).Handle(new CancelRawMessageCommand(scene.Message.Id, "wrong item"), CancellationToken.None);

        var reversal = await db.InventoryTransactions.AsNoTracking()
            .SingleAsync(t => t.RawMessageId == scene.Message.Id && t.Direction == TransactionDirection.Out &&
                t.SourceDocumentType == DocumentType.RawReceiptMessage && t.ReversesTransactionId != null);
        reversal.QuantityKg.Should().Be(60m, "only the accepted 60 KG was still standing on the lot");

        (await LotBalanceKg(db, scene.Message.Id)).Should().Be(0m);
    }

    // =====================================================================
    // The refusals: nothing is changed when the reversal is not unambiguous
    // =====================================================================

    [Fact]
    public async Task A_Receipt_Held_By_A_Job_Order_Is_Refused_And_Changes_Nothing()
    {
        using var db = NewDb();
        var scene = SeedReceipt(db);

        var order = new ProductionOrder("PRD-2026-000001", scene.Customer.Id, scene.Item.Id, DateTime.UtcNow, "seed",
            requestedQuantityKg: 100m, requestedQuantityMeter: null);
        db.ProductionOrders.Add(order);
        db.RawAllocations.Add(order.AllocateRaw(scene.Message.Id, scene.Item.Id, 100m, null, "seed"));
        await db.SaveChangesAsync();

        var act = () => Handler(db).Handle(
            new CancelRawMessageCommand(scene.Message.Id, "wrong customer"), CancellationToken.None);

        (await act.Should().ThrowAsync<DomainException>())
            .WithMessage("*already allocated to 1 Job Order*");

        var message = await db.RawMessages.AsNoTracking().SingleAsync(m => m.Id == scene.Message.Id);
        message.Status.Should().Be(RawMessageStatus.Open);
        message.CancellationReason.Should().BeNull();
        (await LotBalanceKg(db, scene.Message.Id)).Should().Be(100m, "a refused cancellation posts no reversal");
    }

    [Fact]
    public async Task A_Receipt_Whose_Material_Already_Left_The_Lot_Is_Refused_And_Changes_Nothing()
    {
        using var db = NewDb();
        var scene = SeedReceipt(db);

        // Someone else consumed 30 KG against a different document (e.g. an external release).
        db.InventoryTransactions.Add(new InventoryTransaction(
            DocumentType.RawIssueExternalRelease, "EXT-1", Guid.NewGuid(), DateTime.UtcNow,
            scene.Raw.Id, scene.Customer.Id, scene.Item.Id, rawMessageId: scene.Message.Id, productionOrderId: null,
            quantityKg: 30m, quantityMeter: null, direction: TransactionDirection.Out, createdBy: "other"));
        await db.SaveChangesAsync();

        var act = () => Handler(db).Handle(
            new CancelRawMessageCommand(scene.Message.Id, "wrong warehouse"), CancellationToken.None);

        (await act.Should().ThrowAsync<DomainException>())
            .WithMessage("*cannot be cancelled*")
            .WithMessage("*70 KG*");

        var message = await db.RawMessages.AsNoTracking().SingleAsync(m => m.Id == scene.Message.Id);
        message.Status.Should().Be(RawMessageStatus.Open);
        (await db.InventoryTransactions.CountAsync(t => t.RawMessageId == scene.Message.Id))
            .Should().Be(2, "only the original IN row and the other document's OUT row exist");
    }

    [Fact]
    public async Task Cancelling_Twice_Is_Refused()
    {
        using var db = NewDb();
        var scene = SeedReceipt(db);

        await Handler(db).Handle(new CancelRawMessageCommand(scene.Message.Id, "wrong customer"), CancellationToken.None);

        var act = () => Handler(db).Handle(
            new CancelRawMessageCommand(scene.Message.Id, "again"), CancellationToken.None);

        (await act.Should().ThrowAsync<DomainException>()).WithMessage("*already cancelled*");
        (await db.InventoryTransactions.CountAsync(t => t.RawMessageId == scene.Message.Id))
            .Should().Be(2, "a second cancellation posts no second reversal");
    }

    [Fact]
    public async Task Cancelling_A_Receipt_In_A_Retired_Warehouse_Is_Refused()
    {
        using var db = NewDb();
        var scene = SeedReceipt(db);

        var warehouse = await db.Warehouses.SingleAsync(w => w.Id == scene.Raw.Id);
        warehouse.Deactivate("seed");
        await db.SaveChangesAsync();

        var act = () => Handler(db).Handle(
            new CancelRawMessageCommand(scene.Message.Id, "wrong customer"), CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>("a retired warehouse never receives a new stock row");
        (await db.RawMessages.AsNoTracking().SingleAsync(m => m.Id == scene.Message.Id))
            .Status.Should().Be(RawMessageStatus.Open);
    }

    // =====================================================================
    // Domain-level rules
    // =====================================================================

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_Cancellation_Reason_Is_Mandatory(string reason)
    {
        var message = new RawMessage("RMSG-1", DateTime.UtcNow, Guid.NewGuid(), Guid.NewGuid(), "seed", "seed");
        var act = () => message.Cancel(reason, "operator");
        act.Should().Throw<DomainException>().WithMessage("*reason is required*");
        message.Status.Should().Be(RawMessageStatus.Open);
    }

    [Fact]
    public void The_Validator_Rejects_A_Missing_Reason_And_A_Missing_Id()
    {
        var validator = new CancelRawMessageCommandValidator();
        validator.Validate(new CancelRawMessageCommand(Guid.Empty, "reason")).IsValid.Should().BeFalse();
        validator.Validate(new CancelRawMessageCommand(Guid.NewGuid(), "  ")).IsValid.Should().BeFalse();
        validator.Validate(new CancelRawMessageCommand(Guid.NewGuid(), "wrong customer")).IsValid.Should().BeTrue();
    }

    [Fact]
    public void A_Cancelled_Receipt_Is_Never_Brought_Back_By_A_Later_Return()
    {
        var message = new RawMessage("RMSG-1", DateTime.UtcNow, Guid.NewGuid(), Guid.NewGuid(), "seed", "seed");
        message.AddLine(Guid.NewGuid(), 100m, null, null, null);
        message.Cancel("wrong customer", "operator");

        // A return of stock to this lot recomputes statuses; a cancelled receipt must stay cancelled.
        message.RecalculateStatus(hasRemainingBalance: true, hasAnyUsage: false);

        message.Status.Should().Be(RawMessageStatus.Cancelled);
        message.IsAvailableForAllocation.Should().BeFalse();
    }
}
