using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.CostAccounting.Commands;
using DyeHouseERP.Application.CustomerTransfers.Commands;
using DyeHouseERP.Application.Invoices.Commands;
using DyeHouseERP.Application.Materials.Commands;
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
/// Spec section 43 - a closed accounting period must block the posting of any
/// ledger row dated inside it, and the date that is CHECKED must be the date the
/// row actually carries.
///
/// These tests deliberately use the REAL <see cref="PeriodCloseService"/> (not a
/// fake) against an in-memory context, so the actual period query - including the
/// reopened-period rule - is exercised, and they pin the two fixes:
///
///   * Finding A: creating a CostEntry is a dated financial posting, so its own
///     EntryDate must be checked before anything is persisted;
///   * Finding B: the invoice issue guard previously validated invoice.InvoiceDate
///     while the CustomerLedgerEntry was written with the actual posting date, so a
///     backdated invoice could post into a closed period. Both issue and cancel now
///     validate the exact date they write.
///
/// The clock is the shared <see cref="FixedClock"/>: "today" is 2026-03-01.
/// </summary>
public class AccountingPeriodClosureTests
{
    private static readonly DateTime Today = new FixedClock().UtcNow;

    private readonly DbContextOptions<ApplicationDbContext> _options;

    public AccountingPeriodClosureTests()
    {
        _options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"accounting-period-{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
    }

    private ApplicationDbContext NewDb() => new(_options);

    /// <summary>
    /// Real check logic over the in-memory provider. The lock partner is a noop
    /// because sp_getapplock requires a real SQL Server session - the lock
    /// behaviour itself is proven only by the SQL Server integration suite.
    /// </summary>
    private static PeriodCloseService RealPeriodClose(ApplicationDbContext db)
        => new(db, new NoopAllocationLock());

    private sealed class NoopAllocationLock : IAllocationLockService
    {
        public Task<IAsyncDisposable> AcquireAsync(StockLockKey key, CancellationToken cancellationToken = default)
            => Task.FromResult<IAsyncDisposable>(new NoopGuard());
        public Task<IAsyncDisposable> AcquireManyAsync(IEnumerable<StockLockKey> keys, CancellationToken cancellationToken = default)
            => Task.FromResult<IAsyncDisposable>(new NoopGuard());
        public Task<IAsyncDisposable> AcquireNamedAsync(string resource, CancellationToken cancellationToken = default)
            => Task.FromResult<IAsyncDisposable>(new NoopGuard());
        public Task<IAsyncDisposable> AcquireExclusiveAsync(string resource, CancellationToken cancellationToken = default)
            => Task.FromResult<IAsyncDisposable>(new NoopGuard());
        public Task<IAsyncDisposable> AcquireSharedAsync(string resource, CancellationToken cancellationToken = default)
            => Task.FromResult<IAsyncDisposable>(new NoopGuard());
        private sealed class NoopGuard : IAsyncDisposable
        {
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private static void Close(ApplicationDbContext db, DateTime start, DateTime end)
    {
        db.PeriodCloses.Add(new PeriodClose(start, end, "controller"));
        db.SaveChanges();
    }

    // ------------------------------------------------------------------
    // Finding A - CostEntry period guard
    // ------------------------------------------------------------------

    [Fact]
    public async Task CostEntry_InsideAClosedPeriod_IsRejectedBeforeAnythingIsPersisted()
    {
        using var db = NewDb();
        Close(db, new DateTime(2026, 2, 1), new DateTime(2026, 3, 31));

        var handler = new CreateCostEntryCommandHandler(db, new StubCurrentUser("accountant"), RealPeriodClose(db));

        var act = () => handler.Handle(
            new CreateCostEntryCommand(Guid.NewGuid(), CostCategory.Electricity, 4_500m, Today, "March power"), CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>().WithMessage("*closed*");
        (await db.CostEntries.CountAsync()).Should().Be(0, "a rejected cost entry must not be persisted");
    }

    [Fact]
    public async Task CostEntry_InsideAnOpenPeriod_IsPersisted()
    {
        using var db = NewDb();
        Close(db, new DateTime(2026, 1, 1), new DateTime(2026, 1, 31)); // a different period

        var orderId = Guid.NewGuid();
        var handler = new CreateCostEntryCommandHandler(db, new StubCurrentUser("accountant"), RealPeriodClose(db));

        var dto = await handler.Handle(
            new CreateCostEntryCommand(orderId, CostCategory.Electricity, 4_500m, Today, "March power"), CancellationToken.None);

        var stored = await db.CostEntries.SingleAsync();
        stored.Id.Should().Be(dto.Id);
        stored.ProductionOrderId.Should().Be(orderId);
        stored.Category.Should().Be(CostCategory.Electricity);
        stored.Amount.Should().Be(4_500m);
        stored.EntryDate.Should().Be(Today, "the entry keeps the caller-supplied accounting date");
    }

    [Fact]
    public async Task CostEntry_InsideAReopenedPeriod_IsPersisted()
    {
        using var db = NewDb();
        var period = new PeriodClose(new DateTime(2026, 2, 1), new DateTime(2026, 3, 31), "controller");
        db.PeriodCloses.Add(period);
        db.SaveChanges();

        period.Reopen("late supplier invoice", "controller");
        db.SaveChanges();

        var handler = new CreateCostEntryCommandHandler(db, new StubCurrentUser("accountant"), RealPeriodClose(db));

        var dto = await handler.Handle(
            new CreateCostEntryCommand(Guid.NewGuid(), CostCategory.Maintenance, 900m, Today, "reopened period"), CancellationToken.None);

        (await db.CostEntries.CountAsync()).Should().Be(1);
        dto.EntryDate.Should().Be(Today);
    }

    [Fact]
    public void CostEntry_ExistingValidationRulesAreUnchanged()
    {
        var validator = new CreateCostEntryCommandValidator();

        validator.Validate(new CreateCostEntryCommand(Guid.Empty, CostCategory.Labor, 100m, Today, null))
            .IsValid.Should().BeFalse("a cost entry still requires a production order");

        validator.Validate(new CreateCostEntryCommand(Guid.NewGuid(), CostCategory.Labor, -1m, Today, null))
            .IsValid.Should().BeFalse("a negative amount is still rejected");

        validator.Validate(new CreateCostEntryCommand(Guid.NewGuid(), CostCategory.Labor, 100m, Today, "ok"))
            .IsValid.Should().BeTrue();
    }

    // ------------------------------------------------------------------
    // Finding B - invoice issue: guard must match the row it writes
    // ------------------------------------------------------------------

    /// <summary>A draft invoice dated 2026-02-15 (open), issued while "today" (2026-03-01) is closed.</summary>
    [Fact]
    public async Task IssueInvoice_WhenThePostingDateIsClosed_IsRejectedEvenThoughTheInvoiceDateIsOpen()
    {
        using var db = NewDb();
        var invoice = DraftInvoice(db, new DateTime(2026, 2, 15));
        Close(db, new DateTime(2026, 3, 1), new DateTime(2026, 3, 31));

        var handler = new IssueInvoiceCommandHandler(db, new StubCurrentUser("accountant"), new FixedClock(), RealPeriodClose(db));

        // Before the fix this passed (the guard checked 2026-02-15) and the receivable was written
        // with today's date, i.e. inside the closed March period.
        var act = () => handler.Handle(new IssueInvoiceCommand(invoice.Id), CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>().WithMessage("*closed*");
        (await db.CustomerLedgerEntries.CountAsync()).Should().Be(0, "a refused issue posts nothing");
        (await db.Invoices.SingleAsync()).Status.Should().Be(InvoiceStatus.Draft, "the invoice is untouched");
    }

    [Fact]
    public async Task IssueInvoice_PostsTheReceivableOnTheActualPostingDate()
    {
        using var db = NewDb();
        var invoice = DraftInvoice(db, new DateTime(2026, 2, 15)); // backdated document, no closed period

        var handler = new IssueInvoiceCommandHandler(db, new StubCurrentUser("accountant"), new FixedClock(), RealPeriodClose(db));
        await handler.Handle(new IssueInvoiceCommand(invoice.Id), CancellationToken.None);

        var row = await db.CustomerLedgerEntries.SingleAsync();
        row.EntryDate.Should().Be(Today, "the ledger row keeps its existing (issue-time) dating");
        row.Debit.Should().Be(970m);
        row.Credit.Should().Be(0m);
        (await db.Invoices.SingleAsync()).Status.Should().Be(InvoiceStatus.Issued);
    }

    /// <summary>
    /// Documents the chosen semantics: the debit is posted on the issue date, so a backdated
    /// invoice whose OWN date sits in a closed period is still postable - nothing lands in the
    /// closed period. (Rejecting it instead would require dating the row with InvoiceDate, which
    /// would move the receivable on the customer statement.)
    /// </summary>
    [Fact]
    public async Task IssueInvoice_WhenOnlyTheBackdatedInvoiceDateIsClosed_PostsOnTheOpenPostingDate()
    {
        using var db = NewDb();
        var invoice = DraftInvoice(db, new DateTime(2026, 2, 15));
        Close(db, new DateTime(2026, 2, 1), new DateTime(2026, 2, 28));

        var handler = new IssueInvoiceCommandHandler(db, new StubCurrentUser("accountant"), new FixedClock(), RealPeriodClose(db));
        await handler.Handle(new IssueInvoiceCommand(invoice.Id), CancellationToken.None);

        var row = await db.CustomerLedgerEntries.SingleAsync();
        row.EntryDate.Should().Be(Today);
        (await db.Invoices.SingleAsync()).Status.Should().Be(InvoiceStatus.Issued);
    }

    // ------------------------------------------------------------------
    // Finding B - cancellation path is verified separately
    // ------------------------------------------------------------------

    [Fact]
    public async Task CancelInvoice_WhenThePostingDateIsClosed_IsRejectedAndPostsNoReversal()
    {
        using var db = NewDb();
        var invoice = IssuedInvoice(db);
        var numberOfRows = await db.CustomerLedgerEntries.CountAsync();
        Close(db, new DateTime(2026, 3, 1), new DateTime(2026, 3, 31));

        var handler = new CancelInvoiceCommandHandler(db, new StubCurrentUser("accountant"), new FixedClock(), RealPeriodClose(db));

        var act = () => handler.Handle(new CancelInvoiceCommand(invoice.Id, "returned goods"), CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>().WithMessage("*closed*");
        (await db.CustomerLedgerEntries.CountAsync()).Should().Be(numberOfRows, "no reversal row may be written");
        (await db.Invoices.SingleAsync()).Status.Should().Be(InvoiceStatus.Issued, "the invoice is untouched");
    }

    [Fact]
    public async Task CancelInvoice_PostsTheReversalOnThePostingDate()
    {
        using var db = NewDb();
        var invoice = IssuedInvoice(db);

        var handler = new CancelInvoiceCommandHandler(db, new StubCurrentUser("accountant"), new FixedClock(), RealPeriodClose(db));
        await handler.Handle(new CancelInvoiceCommand(invoice.Id, "returned goods"), CancellationToken.None);

        var reversal = await db.CustomerLedgerEntries.SingleAsync(e => e.Credit > 0);
        reversal.EntryDate.Should().Be(Today);
        reversal.Debit.Should().Be(0m);
        reversal.Credit.Should().Be(970m);
        (await db.Invoices.SingleAsync()).Status.Should().Be(InvoiceStatus.Cancelled);
    }

    // ------------------------------------------------------------------

    /// <summary>Draft invoice for 10 units at 100, discount 50, tax 20 = 970.</summary>
    private static Invoice DraftInvoice(ApplicationDbContext db, DateTime invoiceDate)
    {
        var invoice = new Invoice("INV-2026-0001", invoiceDate, Guid.NewGuid(), "accountant", null, 50m, 20m, null);
        invoice.AddLine(null, Guid.NewGuid(), "Navy", 10m, 100m, null);
        db.Invoices.Add(invoice);
        db.SaveChanges();
        return invoice;
    }

    private static Invoice IssuedInvoice(ApplicationDbContext db)
    {
        var invoice = DraftInvoice(db, Today);
        invoice.Issue();
        db.SaveChanges();
        return invoice;
    }

    // ------------------------------------------------------------------
    // Period close-vs-post protocol (Batch A).
    // The lock MECHANISM itself is proven only on SQL Server (recorded there as
    // an environment blocker). These unit tests pin the PROTOCOL: the posting
    // guard is taken on the single global resource, the closure/reopen hold their
    // EXCLUSIVE form of the SAME resource, and the shared-guard re-check refuses
    // a date inside a closed period even while the guard is held.
    // ------------------------------------------------------------------

    [Fact]
    public async Task Protocol_PostingGuard_IsTheSameResourceTheClosureExclusiveLocks()
    {
        using var db = NewDb();
        var recording = new RecordingLockPartner();
        var service = new PeriodCloseService(db, recording);

        await using (await service.BeginPostingAsync(Today, CancellationToken.None))
        {
            recording.SharedResources.Should().ContainSingle("one Shared acquisition per posting")
                .Which.Should().Be(IPeriodCloseService.PeriodLockResource,
                    "postings and the closure MUST use one global resource - per-period keys leave gaps");
        }
    }

    [Fact]
    public async Task Protocol_AClosingARangeWhileAPostingGuardRefsInnerDate_IsRefusedInsideTheSharedGuard()
    {
        using var db = NewDb();
        var period = new PeriodClose(new DateTime(2026, 2, 1), new DateTime(2026, 3, 31), "controller");
        db.PeriodCloses.Add(period);
        db.SaveChanges();

        var service = new PeriodCloseService(db, new RecordingLockPartner());

        var act = () => service.BeginPostingAsync(Today, CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>().WithMessage("*closed*");
    }

    [Fact]
    public void Protocol_TheExclusiveLockResourceMatchesTheSharedOne()
    {
        // The whole S/X protocol rests on the two sides naming the SAME resource.
        IPeriodCloseService.PeriodLockResource.Should().Be("Shared:Period");
    }

    // ------------------------------------------------------------------
    // Batch B - clock unification: the guard date and the ledger row date must
    // come from ONE read. With the shared FixedClock (2026-03-01T10:00) these
    // tests prove that the row a customer transfer writes carries exactly the
    // date the posting guard validated - the midnight-drift shape is the same
    // one that previously let the invoice issue guard check a different date
    // than the row it wrote.
    // ------------------------------------------------------------------

    [Fact]
    public async Task CustomerTransfer_LedgerLegsCarryTheExactPostingDateTheGuardChecked()
    {
        using var db = NewDb();
        var customer = new Customer("C-1", "Customer", "seed");
        var toCustomer = new Customer("C-2", "Receiver", "seed");
        var item = new Item("IT-1", "Item", UnitOfMeasure.KG, "seed");
        var warehouse = new Warehouse("W-1", "Main", WarehouseKind.RawMaterial, "seed");
        var message = new RawMessage("RMSG-1", new FixedClock().UtcNow, customer.Id, warehouse.Id, "seed", "seed");
        message.AddLine(item.Id, 500m, null, null, null);

        _ = toCustomer;

        db.Customers.AddRange(customer, toCustomer);
        db.Items.Add(item);
        db.Warehouses.Add(warehouse);
        db.RawMessages.Add(message);

        // The customer's own receipt: 500 KG available.
        db.InventoryTransactions.Add(new InventoryTransaction(
            DocumentType.RawReceiptMessage, message.MessageNumber, message.Id, new FixedClock().UtcNow,
            warehouse.Id, customer.Id, item.Id, rawMessageId: message.Id, productionOrderId: null,
            quantityKg: 500m, quantityMeter: null,
            direction: TransactionDirection.In, createdBy: "seed"));
        db.SaveChanges();

        var handler = new CreateCustomerTransferCommandHandler(db, new StubCurrentUser("user"),
            new FixedNumberGenerator(), new InventoryLedgerService(db), new FixedClock(), new OpenPeriod(), new RecordingStockLock());

        await handler.Handle(new CreateCustomerTransferCommand(
            customer.Id, toCustomer.Id, message.Id, item.Id, 200m, null, "shift owner", null), CancellationToken.None);

        var legs = await db.InventoryTransactions
            .Where(t => t.SourceDocumentType == DocumentType.CustomerTransfer)
            .ToListAsync();
        legs.Should().HaveCount(2, "one Out and one In leg on the same message");
        legs.Select(l => l.TransactionDate).Should().OnlyContain(d => d == new FixedClock().UtcNow,
            "Both legs MUST carry the single posting date (one read) - a second clock read could " +
            "land one leg after midnight and split the transfer across two periods.");
    }

    [Fact]
    public async Task MaterialIssue_TheLedgerRowCarriesThePostingDateTheGuardChecked()
    {
        using var db = NewDb();
        var material = new Material("MAT-1", "Chemical", MaterialUnit.KG, 2.5m, "seed", MaterialKind.Chemical);
        var warehouse = new Warehouse("W-1", "Chemical Store", WarehouseKind.Materials, "seed");
        var customer = new Customer("C-1", "Owner", "seed");
        var item = new Item("IT-1", "Item", UnitOfMeasure.KG, "seed");
        var order = new ProductionOrder("PRO-1", customer.Id, item.Id, new FixedClock().UtcNow, "seed");
        db.Materials.Add(material);
        db.Warehouses.Add(warehouse);
        db.Customers.Add(customer);
        db.Items.Add(item);
        db.ProductionOrders.Add(order);
        db.MaterialTransactions.Add(new MaterialTransaction(
            DocumentType.PurchaseReceipt, "PR-1", Guid.NewGuid(), new FixedClock().UtcNow,
            material.Id, warehouse.Id, productionOrderId: null,
            quantity: 100m, direction: MaterialTransactionDirection.In, unitCost: 2.5m, createdBy: "seed"));
        db.SaveChanges();

        var handler = new CreateMaterialIssueCommandHandler(db, new StubCurrentUser("user"),
            new FixedNumberGenerator(), new MaterialLedgerService(db), new FixedClock(),
            new OpenPeriod(), new RecordingStockLock());

        await handler.Handle(new CreateMaterialIssueCommand(
            material.Id, warehouse.Id, order.Id, 25m, null, null), CancellationToken.None);

        var row = await db.MaterialTransactions.SingleAsync(t =>
            t.SourceDocumentType == DocumentType.MaterialIssue && t.Direction == MaterialTransactionDirection.Out);
        row.TransactionDate.Should().Be(new FixedClock().UtcNow);
    }
}

/// <summary>Recording lock partner for the protocol tests (no database locks taken).</summary>
internal sealed class RecordingLockPartner : IAllocationLockService
{
    public List<string> SharedResources { get; } = new();
    public List<string> ExclusiveResources { get; } = new();

    public Task<IAsyncDisposable> AcquireAsync(StockLockKey key, CancellationToken cancellationToken = default)
        => Task.FromResult<IAsyncDisposable>(new RecordingGuard());
    public Task<IAsyncDisposable> AcquireManyAsync(IEnumerable<StockLockKey> keys, CancellationToken cancellationToken = default)
        => Task.FromResult<IAsyncDisposable>(new RecordingGuard());
    public Task<IAsyncDisposable> AcquireNamedAsync(string resource, CancellationToken cancellationToken = default)
    {
        ExclusiveResources.Add(resource);
        return Task.FromResult<IAsyncDisposable>(new RecordingGuard());
    }
    public Task<IAsyncDisposable> AcquireExclusiveAsync(string resource, CancellationToken cancellationToken = default)
    {
        ExclusiveResources.Add(resource);
        return Task.FromResult<IAsyncDisposable>(new RecordingGuard());
    }
    public Task<IAsyncDisposable> AcquireSharedAsync(string resource, CancellationToken cancellationToken = default)
    {
        SharedResources.Add(resource);
        return Task.FromResult<IAsyncDisposable>(new RecordingGuard());
    }
    private sealed class RecordingGuard : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
