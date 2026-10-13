using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.CustomerReturns.Commands;
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
/// Correcting a customer return entered by mistake (wrong lot, quantity or customer).
///
/// A return posts real IN ledger rows the moment it is created, so - like every other posted
/// stock document here - it is corrected by CANCELLING it: the document and its original rows
/// stay, and the stock it restored is taken back out with an equal-and-opposite OUT row. The
/// cancellation is refused, before anything is written, whenever the returned material has
/// already been used, so it can never invent negative stock or silently borrow another job's
/// material.
/// </summary>
public class CustomerReturnCancellationTests
{
    private readonly DbContextOptions<ApplicationDbContext> _options;

    public CustomerReturnCancellationTests()
    {
        _options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"return-cancel-{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
    }

    private ApplicationDbContext NewDb() => new(_options);

    private static CancelCustomerReturnCommandHandler Handler(ApplicationDbContext db) =>
        new(db, new StubCurrentUser("operator"), new FixedClock(), new OpenPeriod(), new RecordingStockLock());

    private sealed record Scene(Customer Customer, Item Item, Warehouse Raw, RawMessage Lot, CustomerReturn Return);

    /// <summary>A lot holding 100 KG, and a 40 KG return posted into it.</summary>
    private static Scene SeedPostedReturn(ApplicationDbContext db, decimal returnedKg = 40m)
    {
        var customer = new Customer("C-1", "Customer", "seed");
        var item = new Item("IT-1", "Item", UnitOfMeasure.KG, "seed");
        var raw = new Warehouse("RAW", "Raw Material", WarehouseKind.RawMaterial, "seed");

        var lot = new RawMessage("RMSG-1", DateTime.UtcNow, customer.Id, raw.Id, "seed", "seed");
        lot.AddLine(item.Id, 100m, null, null, null);

        var ret = new CustomerReturn("CRT-2026-000001", DateTime.UtcNow, customer.Id, "seed", "customer sent it back");
        ret.AddLine(lot.Id, item.Id, returnedKg, null, null, null, null);

        db.AddRange(customer, item, raw, lot, ret);
        db.InventoryTransactions.Add(new InventoryTransaction(
            DocumentType.RawReceiptMessage, lot.MessageNumber, lot.Id, DateTime.UtcNow,
            raw.Id, customer.Id, item.Id, rawMessageId: lot.Id, productionOrderId: null,
            quantityKg: 100m, quantityMeter: null, direction: TransactionDirection.In, createdBy: "seed"));
        db.InventoryTransactions.Add(new InventoryTransaction(
            DocumentType.CustomerReturn, ret.ReturnNumber, ret.Id, DateTime.UtcNow,
            raw.Id, customer.Id, item.Id, rawMessageId: lot.Id, productionOrderId: null,
            quantityKg: returnedKg, quantityMeter: null, direction: TransactionDirection.In, createdBy: "seed"));
        ret.Post();
        db.SaveChanges();

        return new Scene(customer, item, raw, lot, ret);
    }

    private static async Task<decimal> LotBalanceKg(ApplicationDbContext db, Guid lotId) =>
        (await db.InventoryTransactions.AsNoTracking().Where(t => t.RawMessageId == lotId).ToListAsync())
        .Sum(t => (t.QuantityKg ?? 0m) * (int)t.Direction);

    // =====================================================================
    // The safe path
    // =====================================================================

    [Fact]
    public async Task An_Untouched_Return_Is_Cancelled_By_Reversing_Its_Stock_And_Keeping_Its_History()
    {
        using var db = NewDb();
        var scene = SeedPostedReturn(db);
        var returnInRowId = (await db.InventoryTransactions.AsNoTracking()
            .SingleAsync(t => t.SourceDocumentId == scene.Return.Id)).Id;

        var dto = await Handler(db).Handle(
            new CancelCustomerReturnCommand(scene.Return.Id, "returned against the wrong lot"), CancellationToken.None);

        dto.IsCancelled.Should().BeTrue();
        dto.CancellationReason.Should().Be("returned against the wrong lot");
        dto.CancelledBy.Should().Be("operator");

        var rows = await db.InventoryTransactions.AsNoTracking()
            .Where(t => t.RawMessageId == scene.Lot.Id).ToListAsync();
        rows.Should().HaveCount(3, "the two original IN rows stay and one reversing OUT row is appended");
        rows.Count(r => r.Direction == TransactionDirection.In).Should().Be(2, "history is never rewritten");

        var reversal = rows.Single(r => r.Direction == TransactionDirection.Out);
        reversal.QuantityKg.Should().Be(40m);
        reversal.SourceDocumentId.Should().Be(scene.Return.Id);
        reversal.ReversesTransactionId.Should().Be(returnInRowId, "the reversal names the movement it reverses");

        (await LotBalanceKg(db, scene.Lot.Id)).Should().Be(100m,
            "the lot drops back to exactly what the receipt brought in - no balance was edited");
    }

    // =====================================================================
    // The refusals: nothing is written when the reversal is not unambiguous
    // =====================================================================

    [Fact]
    public async Task A_Return_Whose_Material_Was_Already_Used_Is_Refused_And_Changes_Nothing()
    {
        using var db = NewDb();
        var scene = SeedPostedReturn(db);

        // 100 received + 40 returned = 140 on the lot; 90 consumed leaves 50, less than the 40 being
        // reversed is fine - so consume 110 to leave 30, which the reversal would drive negative.
        db.InventoryTransactions.Add(new InventoryTransaction(
            DocumentType.RawIssueExternalRelease, "EXT-1", Guid.NewGuid(), DateTime.UtcNow,
            scene.Raw.Id, scene.Customer.Id, scene.Item.Id, rawMessageId: scene.Lot.Id, productionOrderId: null,
            quantityKg: 110m, quantityMeter: null, direction: TransactionDirection.Out, createdBy: "other"));
        await db.SaveChangesAsync();

        var act = () => Handler(db).Handle(
            new CancelCustomerReturnCommand(scene.Return.Id, "wrong quantity"), CancellationToken.None);

        (await act.Should().ThrowAsync<DomainException>())
            .WithMessage("*cannot be cancelled*")
            .WithMessage("*30 KG*");

        var ret = await db.CustomerReturns.AsNoTracking().SingleAsync(r => r.Id == scene.Return.Id);
        ret.IsCancelled.Should().BeFalse();
        ret.CancellationReason.Should().BeNull();
        (await db.InventoryTransactions.CountAsync(t => t.SourceDocumentId == scene.Return.Id &&
            t.Direction == TransactionDirection.Out))
            .Should().Be(0, "a refused cancellation posts no reversal row");
    }

    [Fact]
    public async Task Cancelling_Twice_Is_Refused()
    {
        using var db = NewDb();
        var scene = SeedPostedReturn(db);

        await Handler(db).Handle(new CancelCustomerReturnCommand(scene.Return.Id, "wrong lot"), CancellationToken.None);

        var act = () => Handler(db).Handle(
            new CancelCustomerReturnCommand(scene.Return.Id, "again"), CancellationToken.None);

        (await act.Should().ThrowAsync<DomainException>()).WithMessage("*already cancelled*");
        (await db.InventoryTransactions.CountAsync(t => t.SourceDocumentId == scene.Return.Id &&
            t.Direction == TransactionDirection.Out))
            .Should().Be(1, "a second cancellation posts no second reversal");
    }

    [Fact]
    public async Task Cancelling_A_Return_In_A_Retired_Warehouse_Is_Refused()
    {
        using var db = NewDb();
        var scene = SeedPostedReturn(db);

        var warehouse = await db.Warehouses.SingleAsync(w => w.Id == scene.Raw.Id);
        warehouse.Deactivate("seed");
        await db.SaveChangesAsync();

        var act = () => Handler(db).Handle(
            new CancelCustomerReturnCommand(scene.Return.Id, "wrong lot"), CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>("a retired warehouse never receives a new stock row");
        (await db.CustomerReturns.AsNoTracking().SingleAsync(r => r.Id == scene.Return.Id))
            .IsCancelled.Should().BeFalse();
    }

    // =====================================================================
    // Domain-level rules
    // =====================================================================

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_Cancellation_Reason_Is_Mandatory(string reason)
    {
        var ret = new CustomerReturn("CRT-1", DateTime.UtcNow, Guid.NewGuid(), "seed");
        var act = () => ret.Cancel(reason, "operator");
        act.Should().Throw<DomainException>().WithMessage("*reason is required*");
        ret.IsCancelled.Should().BeFalse();
    }

    [Fact]
    public void The_Validator_Rejects_A_Missing_Reason_And_A_Missing_Id()
    {
        var validator = new CancelCustomerReturnCommandValidator();
        validator.Validate(new CancelCustomerReturnCommand(Guid.Empty, "wrong lot")).IsValid.Should().BeFalse();
        validator.Validate(new CancelCustomerReturnCommand(Guid.NewGuid(), "  ")).IsValid.Should().BeFalse();
        validator.Validate(new CancelCustomerReturnCommand(Guid.NewGuid(), "wrong lot")).IsValid.Should().BeTrue();
    }

    [Fact]
    public void A_Posted_Return_Is_Locked_So_Only_The_Cancellation_Workflow_Can_Correct_It()
    {
        var ret = new CustomerReturn("CRT-1", DateTime.UtcNow, Guid.NewGuid(), "seed");
        ret.AddLine(Guid.NewGuid(), Guid.NewGuid(), 10m, null);
        ret.Post();

        var addAfterPost = () => ret.AddLine(Guid.NewGuid(), Guid.NewGuid(), 5m, null);
        addAfterPost.Should().Throw<DocumentLockedException>("the posted rows are append-only");
        ret.IsCancelled.Should().BeFalse("and the correction path is the audited cancellation");
    }
}
