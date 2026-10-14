using System.Reflection;
using DyeHouseERP.API.Authorization;
using DyeHouseERP.API.Controllers;
using DyeHouseERP.Application.Checks.Commands;
using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.CostAccounting.Commands;
using DyeHouseERP.Application.CustomerReturns.Commands;
using DyeHouseERP.Application.Invoices.Commands;
using DyeHouseERP.Application.Invoices.DTOs;
using DyeHouseERP.Application.Invoices.Queries;
using DyeHouseERP.Application.PeriodClosing.Commands;
using DyeHouseERP.Application.Purchases.Commands;
using DyeHouseERP.Domain.Common;
using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Enums;
using DyeHouseERP.Domain.Exceptions;
using DyeHouseERP.Infrastructure.Services;
using DyeHouseERP.Persistence;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using Xunit;

namespace DyeHouseERP.UnitTests;

/// <summary>
/// Phase 6 - finance, invoicing, treasury and cheques.
///
/// Everything here is a unit-level proof over the real handlers with the in-memory provider: the
/// SQL-only halves (sp_getapplock serialization, filtered unique index enforcement) are backed by
/// the schema and by construction, and are recorded as Not run in the phase report because no SQL
/// Server is available in this environment.
/// </summary>
public abstract class Phase6TestBase
{
    private readonly DbContextOptions<ApplicationDbContext> _options;

    protected Phase6TestBase()
    {
        _options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"phase6-{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
    }

    protected ApplicationDbContext NewDb() => new(_options);

    protected static readonly DateTime Today = new FixedClock().UtcNow;
}

// =====================================================================================
// 1. Service billing & invoices - the billing unit follows the service price, and the
//    quantity billed is the APPROVED quantity IN THAT UNIT. Never a conversion.
// =====================================================================================
public class Phase6InvoiceBillingUnitTests : Phase6TestBase
{
    private sealed record Scene(
        Customer Customer, Item Item, ProductionOrder Order, Delivery Delivery, DeliveryLine Line);

    /// <summary>
    /// A job priced per KG and per Meter, whose approved delivery recorded 1000 KG and 1800 Meter -
    /// the ordinary dyehouse case where the floor measures KG but the service is priced per Meter.
    /// </summary>
    private static Scene Seed(
        ApplicationDbContext db, string meterPrice = "2.0", string kgPrice = "1.0",
        decimal? deliveredKg = 1000m, decimal? deliveredMeter = 1800m)
    {
        var customer = new Customer("C-1", "Customer", "seed");
        var item = new Item("FAB", "Fabric", UnitOfMeasure.KG, "seed", secondaryUnit: UnitOfMeasure.Meter);
        var order = new ProductionOrder("PRD-1", customer.Id, item.Id, Today, "seed");

        db.AddRange(customer, item, order);

        foreach (var (unit, price) in new[]
                 {
                     (UnitOfMeasure.KG, decimal.Parse(kgPrice)), (UnitOfMeasure.Meter, decimal.Parse(meterPrice))
                 })
        {
            db.ProductionOrderServicePrices.Add(new ProductionOrderServicePrice(
                order.Id, Guid.NewGuid(), unit, price, sourceCustomerId: null, isOverride: false,
                overrideReason: null, pricedBy: "seed"));
        }

        var delivery = new Delivery("DEL-1", Today, customer.Id, "seed");
        var line = delivery.AddLine(order.Id, item.Id, "Navy", deliveredKg, deliveredMeter, null, null, null);
        delivery.MarkPrepared(); // Prepared = the delivery is approved (a Draft permit is not billable)
        db.Deliveries.Add(delivery);

        db.SaveChanges();
        return new Scene(customer, item, order, delivery, line);
    }

    private static CreateInvoiceCommandHandler Handler(ApplicationDbContext db) =>
        new(db, new StubCurrentUser("accountant"), new FixedNumberGenerator());

    private static InvoiceLineInput Line(Scene scene, UnitOfMeasure unit, decimal quantity, decimal price,
        Guid? costEntryId = null) => new()
    {
        ProductionOrderId = scene.Order.Id, ItemId = scene.Item.Id, Color = "Navy",
        Unit = unit, Quantity = quantity, ProcessingPrice = price, CostEntryId = costEntryId
    };

    [Fact]
    public async Task Billing_ByMeter_UsesTheApprovedMeterQuantity_AndNeverConvertsKgIntoMeter()
    {
        using var db = NewDb();
        var scene = Seed(db);

        var dto = await Handler(db).Handle(
            new CreateInvoiceCommand(scene.Customer.Id, Today, null, 0m, 0m, null,
                new List<InvoiceLineInput> { Line(scene, UnitOfMeasure.Meter, 1800m, 2.0m) }),
            CancellationToken.None);

        var line = dto.Lines.Single();
        line.Unit.Should().Be(UnitOfMeasure.Meter, "the billing unit follows the configured service price");
        line.Quantity.Should().Be(1800m, "the APPROVED quantity in that unit - the recorded delivery figure");
        line.Value.Should().Be(3600m, "1800 Meter x 2.0 - never 1000 KG converted at some invented rate");

        var stored = await db.InvoiceLines.SingleAsync();
        stored.Unit.Should().Be(UnitOfMeasure.Meter);
        stored.Quantity.Should().Be(1800m);
    }

    [Fact]
    public async Task Billing_ByMeter_IsRefused_WhenNoMeterQuantityWasEverApproved()
    {
        using var db = NewDb();
        // Priced per Meter, but the delivery recorded KG only: there is no approved Meter quantity.
        var scene = Seed(db, deliveredKg: 1000m, deliveredMeter: null);

        var act = () => Handler(db).Handle(
            new CreateInvoiceCommand(scene.Customer.Id, Today, null, 0m, 0m, null,
                new List<InvoiceLineInput> { Line(scene, UnitOfMeasure.Meter, 1000m, 2.0m) }),
            CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>().WithMessage("*never converts KG into Meter*");
        (await db.Invoices.CountAsync()).Should().Be(0, "a refused line leaves nothing persisted");
    }

    [Fact]
    public async Task Billing_IsRefused_InAUnitTheOrderHasNoServicePriceFor()
    {
        using var db = NewDb();
        var scene = Seed(db, deliveredKg: 1000m, deliveredMeter: 1800m);
        // Remove the Meter price: only KG is priced, so the job cannot be billed per Meter at all.
        var meterPrice = await db.ProductionOrderServicePrices.SingleAsync(p => p.Unit == UnitOfMeasure.Meter);
        db.ProductionOrderServicePrices.Remove(meterPrice);
        await db.SaveChangesAsync();

        var act = () => Handler(db).Handle(
            new CreateInvoiceCommand(scene.Customer.Id, Today, null, 0m, 0m, null,
                new List<InvoiceLineInput> { Line(scene, UnitOfMeasure.Meter, 1800m, 2.0m) }),
            CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>().WithMessage("*no configured service price in Meter*");
    }

    [Fact]
    public async Task TheApprovedQuantity_CannotBeBilledTwice_AndATypoCanStillBeCorrected()
    {
        using var db = NewDb();
        var scene = Seed(db, deliveredKg: 1000m, deliveredMeter: null);

        await Handler(db).Handle(
            new CreateInvoiceCommand(scene.Customer.Id, Today, null, 0m, 0m, null,
                new List<InvoiceLineInput> { Line(scene, UnitOfMeasure.KG, 600m, 1.0m) }),
            CancellationToken.None);

        // A second line for the same order/unit that would push the total past the approved 1000 KG.
        var overBill = () => Handler(db).Handle(
            new CreateInvoiceCommand(scene.Customer.Id, Today, null, 0m, 0m, null,
                new List<InvoiceLineInput> { Line(scene, UnitOfMeasure.KG, 600m, 1.0m) }),
            CancellationToken.None);
        await overBill.Should().ThrowAsync<DomainException>().WithMessage("*exceed its approved KG quantity*");

        var invoice = await db.Invoices.Include(i => i.Lines).SingleAsync();
        var invoiceLine = invoice.Lines.Single();

        // Correcting the SAME line up to the approved quantity is allowed...
        var update = new UpdateInvoiceLineCommandHandler(db, new StubCurrentUser("accountant"));
        await update.Handle(new UpdateInvoiceLineCommand(
            invoice.Id, invoiceLine.Id, 1000m, 1.0m, UnitOfMeasure.KG, "Navy", null), CancellationToken.None);

        // ... and past it is still refused (the line's own previous figure is excluded, not the rule).
        var act = () => update.Handle(new UpdateInvoiceLineCommand(
            invoice.Id, invoiceLine.Id, 1100m, 1.0m, UnitOfMeasure.KG, "Navy", null), CancellationToken.None);
        await act.Should().ThrowAsync<DomainException>().WithMessage("*exceed its approved KG quantity*");
    }

    [Fact]
    public void TheBillingUnit_MustBeARealUnit()
    {
        new CreateInvoiceCommandValidator()
            .Validate(new CreateInvoiceCommand(Guid.NewGuid(), Today, null, 0m, 0m, null,
                new List<InvoiceLineInput> { new() { ItemId = Guid.NewGuid(), Quantity = 1m, Unit = (UnitOfMeasure)999 } }))
            .IsValid.Should().BeFalse("the billing unit is never a made-up value");
    }
}

// =====================================================================================
// 2. Reprocessing / separate costs: company-borne unless explicitly flagged chargeable,
//    and a flagged cost is billed exactly once.
// =====================================================================================
public class Phase6ChargeableCostTests : Phase6TestBase
{
    private static CreateCostEntryCommandHandler CostHandler(ApplicationDbContext db) =>
        new(db, new StubCurrentUser("accountant"), new OpenPeriod());

    private static CreateInvoiceCommandHandler InvoiceHandler(ApplicationDbContext db) =>
        new(db, new StubCurrentUser("accountant"), new FixedNumberGenerator());

    private static CreateInvoiceCommand InvoiceWithCost(Guid customerId, Guid itemId, Guid costEntryId) =>
        new(customerId, Today, null, 0m, 0m, null, new List<InvoiceLineInput>
        {
            new()
            {
                ProductionOrderId = null, ItemId = itemId, Unit = UnitOfMeasure.KG, Quantity = 1m,
                ProcessingPrice = 250m, CostEntryId = costEntryId
            }
        });

    [Fact]
    public async Task ACompanyBorneCost_IsNeverAutoAdded_AndCannotBeBilled()
    {
        using var db = NewDb();
        var orderId = Guid.NewGuid();
        var itemId = Guid.NewGuid();

        var cost = await CostHandler(db).Handle(
            new CreateCostEntryCommand(orderId, CostCategory.Maintenance, 250m, Today, "separate reprocessing charge"),
            CancellationToken.None);

        cost.IsChargeableToCustomer.Should().BeFalse("a cost is the factory's own unless somebody flags it");

        var act = () => InvoiceHandler(db).Handle(InvoiceWithCost(Guid.NewGuid(), itemId, cost.Id), CancellationToken.None);
        await act.Should().ThrowAsync<DomainException>().WithMessage("*not flagged chargeable*");
    }

    [Fact]
    public async Task AChargeableCost_IsBilled_AndNeverCountedTwice()
    {
        using var db = NewDb();
        var itemId = Guid.NewGuid();

        var cost = await CostHandler(db).Handle(
            new CreateCostEntryCommand(Guid.NewGuid(), CostCategory.Maintenance, 250m, Today, "customer-caused rework",
                IsChargeableToCustomer: true),
            CancellationToken.None);
        cost.IsChargeableToCustomer.Should().BeTrue();

        var first = await InvoiceHandler(db).Handle(
            InvoiceWithCost(Guid.NewGuid(), itemId, cost.Id), CancellationToken.None);
        first.Lines.Single().CostEntryId.Should().Be(cost.Id, "the line links the cost it bills");

        var second = () => InvoiceHandler(db).Handle(
            InvoiceWithCost(Guid.NewGuid(), itemId, cost.Id), CancellationToken.None);
        await second.Should().ThrowAsync<DomainException>().WithMessage("*already been billed*");

        (await db.InvoiceLines.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task AReprocessingOrSeparateCost_StaysOutOfInvoicesEntirely_UnlessFlagged()
    {
        using var db = NewDb();
        var orderId = Guid.NewGuid();
        var itemId = Guid.NewGuid();

        // The ordinary case: several costs booked to the order, none of them customer-chargeable.
        foreach (var category in new[] { CostCategory.Maintenance, CostCategory.Electricity, CostCategory.Fuel })
        {
            await CostHandler(db).Handle(
                new CreateCostEntryCommand(orderId, category, 100m, Today, $"{category} cost"), CancellationToken.None);
        }

        (await db.CostEntries.CountAsync(c => c.IsChargeableToCustomer)).Should().Be(0);
        (await db.InvoiceLines.CountAsync()).Should().Be(0, "no code path auto-adds a company-borne cost to an invoice");

        // Only the one an authorized user deliberately flags becomes billable.
        var flagged = await CostHandler(db).Handle(
            new CreateCostEntryCommand(orderId, CostCategory.Maintenance, 100m, Today, "agreed with the customer",
                IsChargeableToCustomer: true), CancellationToken.None);

        var dto = await InvoiceHandler(db).Handle(InvoiceWithCost(Guid.NewGuid(), itemId, flagged.Id), CancellationToken.None);
        dto.Lines.Should().ContainSingle(l => l.CostEntryId == flagged.Id);
    }
}

// =====================================================================================
// 3. A customer return is a stock document: it never touches an invoice and never
//    creates a credit note.
// =====================================================================================
public class Phase6CustomerReturnInvoiceIsolationTests : Phase6TestBase
{
    [Fact]
    public async Task RegisteringAReturn_LeavesTheInvoiceAndTheCustomerStatementUntouched()
    {
        using var db = NewDb();

        var customer = new Customer("C-1", "Customer", "seed");
        var item = new Item("IT-1", "Item", UnitOfMeasure.KG, "seed");
        var raw = new Warehouse("RAW", "Raw Material", WarehouseKind.RawMaterial, "seed");

        var lot = new RawMessage("RMSG-1", Today, customer.Id, raw.Id, "seed", "seed");
        lot.AddLine(item.Id, 100m, null, null, null);

        // An issued invoice (Debit on the customer) - the financial record the return must not disturb.
        var invoice = new Invoice("INV-2026-000001", Today, customer.Id, "accountant");
        invoice.AddLine(null, item.Id, "Navy", 10m, 100m, UnitOfMeasure.KG, null);
        invoice.Issue();

        db.AddRange(customer, item, raw, lot, invoice);
        db.InventoryTransactions.Add(new InventoryTransaction(
            DocumentType.RawReceiptMessage, lot.MessageNumber, lot.Id, Today, raw.Id, customer.Id, item.Id,
            rawMessageId: lot.Id, productionOrderId: null, quantityKg: 100m, quantityMeter: null,
            direction: TransactionDirection.In, createdBy: "seed"));
        db.CustomerLedgerEntries.Add(new CustomerLedgerEntry(
            customer.Id, Today, DocumentType.Invoice, invoice.InvoiceNumber, invoice.Id,
            debit: invoice.Total, credit: 0m, description: "Issued invoice", createdBy: "accountant"));
        db.SaveChanges();

        var ledgerRowsBefore = await db.CustomerLedgerEntries.CountAsync();
        var totalBefore = invoice.Total;

        var handler = new CreateCustomerReturnCommandHandler(
            db, new StubCurrentUser("operator"), new FixedNumberGenerator(), new FixedClock(),
            new OpenPeriod(), new RecordingStockLock());

        await handler.Handle(new CreateCustomerReturnCommand(
            customer.Id, Today, "customer sent it back", null,
            new List<CustomerReturnLineInput> { new(lot.Id, item.Id, 40m, null) },
            ReferenceDocument: invoice.InvoiceNumber), CancellationToken.None);

        var stored = await db.Invoices.Include(i => i.Lines).SingleAsync();
        stored.Status.Should().Be(InvoiceStatus.Issued, "a return never changes the invoice's state");
        stored.Total.Should().Be(totalBefore);
        stored.Discount.Should().Be(0m, "a return never manufactures a credit note or a discount");
        stored.Lines.Should().HaveCount(1, "a return never adds a negative line to an invoice");
        (await db.CustomerLedgerEntries.CountAsync()).Should().Be(ledgerRowsBefore,
            "returning goods posts stock, not money: any financial adjustment needs its own authorized workflow");

        // The return itself did happen (it posted its IN row), so the assertion above is not vacuous.
        (await db.InventoryTransactions.CountAsync(t => t.SourceDocumentType == DocumentType.CustomerReturn))
            .Should().Be(1);
    }
}

// =====================================================================================
// 4. An endorsed customer cheque settling a supplier payable: no cash, both balances
//    move, the documents link, and the same cheque can never settle twice.
// =====================================================================================
public class Phase6EndorsedCheckSettlementTests : Phase6TestBase
{
    private sealed record Scene(Customer Customer, Supplier Supplier, Check Check);

    private static Scene Seed(ApplicationDbContext db, decimal payable = 5000m, decimal receivable = 5000m,
        decimal checkAmount = 3000m)
    {
        var customer = new Customer("C-1", "Customer", "seed");
        var supplier = new Supplier("S-1", "Chemical Supplier", "seed");

        // The customer's account already carries what they owe us (their issued invoice).
        db.Customers.Add(customer);
        db.Suppliers.Add(supplier);
        db.CustomerLedgerEntries.Add(new CustomerLedgerEntry(
            customer.Id, Today, DocumentType.Invoice, "INV-2026-000001", Guid.NewGuid(),
            debit: receivable, credit: 0m, description: "Issued invoice", createdBy: "accountant"));

        // And the supplier's account carries what we owe them (their posted invoice).
        db.SupplierLedgerEntries.Add(new SupplierLedgerEntry(
            supplier.Id, Today, DocumentType.SupplierInvoice, "SINV-1", Guid.NewGuid(),
            debit: payable, credit: 0m, description: "Supplier invoice", createdBy: "accountant"));

        var check = new Check("CHK-1", CheckDirection.CustomerCheck, "NBE", checkAmount, Today, Today.AddDays(30),
            "Customer", customer.Name, CheckHolderType.Customer, "seed", customerId: customer.Id);
        check.ConfirmReceipt(Today, "DyeHouse", "seed"); // physically in our hands
        db.Checks.Add(check);
        db.SaveChanges();

        return new Scene(customer, supplier, check);
    }

    private static SettleSupplierPayableWithEndorsedCheckCommandHandler Handler(ApplicationDbContext db) =>
        new(db, new StubCurrentUser("accountant"), new FixedNumberGenerator(), new OpenPeriod(), new RecordingStockLock());

    private static decimal SupplierBalance(ApplicationDbContext db, Guid supplierId)
        => db.SupplierLedgerEntries.Where(e => e.SupplierId == supplierId).ToList().Sum(e => e.Debit - e.Credit);

    private static decimal CustomerBalance(ApplicationDbContext db, Guid customerId)
        => db.CustomerLedgerEntries.Where(e => e.CustomerId == customerId).ToList().Sum(e => e.Debit - e.Credit);

    [Fact]
    public async Task EndorsingACustomerCheque_PostsNoCash_AndMovesBothBalances()
    {
        using var db = NewDb();
        var scene = Seed(db);

        var payment = await Handler(db).Handle(
            new SettleSupplierPayableWithEndorsedCheckCommand(
                scene.Check.Id, scene.Supplier.Id, Today, Reason: "settle their chemical invoice"),
            CancellationToken.None);

        // 1. NO cash, in either direction: the instrument went to the supplier and never
        //    passed through one of our accounts.
        (await db.TreasuryTransactions.CountAsync()).Should().Be(0,
            "an endorsed cheque is not a cash inflow nor a cash outflow");

        // 2. The supplier's payable went down...
        SupplierBalance(db, scene.Supplier.Id).Should().Be(2000m, "5000 owed - 3000 settled by the cheque");

        // 3. ... and so did the customer's account: their cheque is what settled it.
        CustomerBalance(db, scene.Customer.Id).Should().Be(2000m, "5000 owed - 3000 paid with their own cheque");

        // 4. The documents link: cheque -> endorsement movement -> payment -> ledger rows.
        var stored = await db.SupplierPayments.SingleAsync();
        stored.CheckId.Should().Be(scene.Check.Id);
        stored.CheckNumber.Should().Be("CHK-1");
        stored.Amount.Should().Be(3000m);
        stored.TreasuryAccountId.Should().BeNull("a cheque settlement has no account because no money left one");

        var check = await db.Checks.Include(c => c.Movements).SingleAsync();
        check.Status.Should().Be(CheckStatus.Endorsed);
        check.CurrentHolderType.Should().Be(CheckHolderType.Supplier);
        check.Movements.Select(m => m.MovementType).Should().Contain(CheckMovementType.Endorsed);

        (await db.SupplierLedgerEntries.SingleAsync(e => e.SourceDocumentType == DocumentType.SupplierPayment))
            .SourceDocumentId.Should().Be(stored.Id, "the payable reduction points at the payment document");
        (await db.CustomerLedgerEntries.SingleAsync(e => e.SourceDocumentType == DocumentType.CheckRegister))
            .SourceDocumentId.Should().Be(scene.Check.Id, "and the customer's credit points at the instrument");
    }

    [Fact]
    public async Task TheSameCheque_CanNeverSettleASecondTime()
    {
        using var db = NewDb();
        var scene = Seed(db);

        var command = new SettleSupplierPayableWithEndorsedCheckCommand(scene.Check.Id, scene.Supplier.Id, Today);
        await Handler(db).Handle(command, CancellationToken.None);

        var act = () => Handler(db).Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>().WithMessage("*already settled supplier payment*");
        (await db.SupplierPayments.CountAsync()).Should().Be(1);
        SupplierBalance(db, scene.Supplier.Id).Should().Be(2000m, "the duplicate attempt moved nothing");
        (await db.TreasuryTransactions.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ClearingAnEndorsedCustomerCheque_PostsNoCashEither()
    {
        using var db = NewDb();
        var scene = Seed(db);
        await Handler(db).Handle(
            new SettleSupplierPayableWithEndorsedCheckCommand(scene.Check.Id, scene.Supplier.Id, Today),
            CancellationToken.None);

        var clear = new ClearCheckCommandHandler(db, new StubCurrentUser("accountant"), new OpenPeriod());

        // Clearing it "into" one of our accounts is refused outright rather than quietly ignored:
        // there is no account whose money this instrument could ever be.
        var account = new TreasuryAccount("BANK", "Bank", TreasuryAccountKind.Bank, "seed");
        db.TreasuryAccounts.Add(account);
        await db.SaveChangesAsync();

        var intoAccount = () => clear.Handle(
            new ClearCheckCommand(scene.Check.Id, Today, "cleared into the bank", account.Id), CancellationToken.None);
        await intoAccount.Should().ThrowAsync<DomainException>()
            .WithMessage("*never passes through one of our cash/bank accounts*");
        (await db.Checks.SingleAsync()).Status.Should().Be(CheckStatus.Endorsed, "a refused clear changes nothing");

        var dto = await clear.Handle(new ClearCheckCommand(scene.Check.Id), CancellationToken.None);
        dto.Status.Should().Be(CheckStatus.Cleared);
        (await db.TreasuryTransactions.CountAsync()).Should().Be(0,
            "the customer's bank paid the supplier directly - money in our account never happened");
    }

    [Fact]
    public async Task CancellingTheSettlement_RestoresBothBalances_AndStillPostsNoCash()
    {
        using var db = NewDb();
        var scene = Seed(db);

        var payment = await Handler(db).Handle(
            new SettleSupplierPayableWithEndorsedCheckCommand(scene.Check.Id, scene.Supplier.Id, Today),
            CancellationToken.None);

        var cancel = new CancelSupplierPaymentCommandHandler(
            db, new StubCurrentUser("supervisor"), new FixedClock(), new OpenPeriod());
        await cancel.Handle(new CancelSupplierPaymentCommand(payment.Id, "wrong supplier"), CancellationToken.None);

        SupplierBalance(db, scene.Supplier.Id).Should().Be(5000m, "the payable is restored in full");
        CustomerBalance(db, scene.Customer.Id).Should().Be(5000m,
            "the customer's balance must come back too - the cheque no longer settles anything");
        (await db.TreasuryTransactions.CountAsync()).Should().Be(0,
            "cancelling a cheque settlement reverses ledger rows, never cash");
        (await db.SupplierPayments.SingleAsync()).Status.Should().Be(TreasuryDocumentStatus.Cancelled);
    }

    [Fact]
    public async Task OnlyAnIncomingCustomerCheck_CanSettleASupplierPayable()
    {
        using var db = NewDb();
        var supplier = new Supplier("S-1", "Supplier", "seed");
        db.Suppliers.Add(supplier);
        var ourOwnCheck = new Check("CHK-9", CheckDirection.SupplierCheck, "NBE", 1000m, Today, Today,
            "DyeHouse", "DyeHouse", CheckHolderType.Company, "seed", supplierId: supplier.Id);
        db.Checks.Add(ourOwnCheck);
        await db.SaveChangesAsync();

        var act = () => Handler(db).Handle(
            new SettleSupplierPayableWithEndorsedCheckCommand(ourOwnCheck.Id, supplier.Id, Today),
            CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>().WithMessage("*Only an incoming customer check*");
    }

    [Fact]
    public async Task PayingASupplierWithACustomerCheck_IsRefused_BecauseTheEndorsementWorkflowOwnsIt()
    {
        using var db = NewDb();
        var scene = Seed(db);
        var account = new TreasuryAccount("BANK", "Bank", TreasuryAccountKind.Bank, "seed");
        db.TreasuryAccounts.Add(account);
        await db.SaveChangesAsync();

        var pay = new PaySupplierCommandHandler(
            db, new StubCurrentUser("accountant"), new FixedNumberGenerator(), new OpenPeriod());

        var act = () => pay.Handle(new PaySupplierCommand(
            Today, scene.Supplier.Id, account.Id, 3000m, "cheque", CheckId: scene.Check.Id), CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>().WithMessage("*endorsement workflow*");
        (await db.TreasuryTransactions.CountAsync()).Should().Be(0);
        (await db.SupplierPayments.CountAsync()).Should().Be(0);
    }
}

// =====================================================================================
// 5. Treasury: a payment settled by a cheque does not post the cash twice (once at
//    hand-over and again when the cheque clears).
// =====================================================================================
public class Phase6TreasuryChequeCashTests : Phase6TestBase
{
    [Fact]
    public async Task APaymentMadeWithOurOwnCheque_PostsNoCashUntilThatChequeClears()
    {
        using var db = NewDb();
        var supplier = new Supplier("S-1", "Supplier", "seed");
        var account = new TreasuryAccount("BANK", "Bank", TreasuryAccountKind.Bank, "seed");
        db.AddRange(supplier, account);
        await db.SaveChangesAsync();

        var check = new Check("CHK-1", CheckDirection.SupplierCheck, "NBE", 1200m, Today, Today.AddDays(10),
            "DyeHouse", "DyeHouse", CheckHolderType.Company, "seed", supplierId: supplier.Id);
        check.SetDrawnOnAccount(account.Id);
        check.EndorseToSupplier(supplier.Id, supplier.Name, Today, "seed", "handed over");
        db.Checks.Add(check);
        await db.SaveChangesAsync();

        var pay = new PaySupplierCommandHandler(
            db, new StubCurrentUser("accountant"), new FixedNumberGenerator(), new OpenPeriod());
        await pay.Handle(new PaySupplierCommand(
            Today, supplier.Id, account.Id, 1200m, "cheque", CheckId: check.Id, Description: "settle the invoice"),
            CancellationToken.None);

        (await db.TreasuryTransactions.CountAsync()).Should().Be(0,
            "handing over a cheque is not cash leaving the bank - the cheque's own clearance is");

        // Exactly one OUT when the instrument is presented and clears.
        var clear = new ClearCheckCommandHandler(db, new StubCurrentUser("accountant"), new OpenPeriod());
        await clear.Handle(new ClearCheckCommand(check.Id, Today, "cleared"), CancellationToken.None);

        var rows = await db.TreasuryTransactions.ToListAsync();
        rows.Should().ContainSingle();
        rows[0].Direction.Should().Be(TreasuryDirection.Out);
        rows[0].Amount.Should().Be(1200m);
    }
}

// =====================================================================================
// 6. Period closing: validated before it happens, and only ONE close of a range can
//    commit (the exclusive period lock serializes concurrent closes; the second sees
//    the first's committed row and is refused).
// =====================================================================================
public class Phase6PeriodClosingTests : Phase6TestBase
{
    private static ClosePeriodCommandHandler Handler(ApplicationDbContext db) =>
        new(db, new StubCurrentUser("controller"), new RecordingStockLock());

    [Fact]
    public async Task ClosingTheSameRangeTwice_LeavesExactlyOneClosedPeriod()
    {
        using var db = NewDb();

        var first = await Handler(db).Handle(
            new ClosePeriodCommand(new DateTime(2026, 2, 1), new DateTime(2026, 2, 28), "February"), CancellationToken.None);
        first.PeriodStart.Should().Be(new DateTime(2026, 2, 1));

        var second = () => Handler(db).Handle(
            new ClosePeriodCommand(new DateTime(2026, 2, 1), new DateTime(2026, 2, 28), "again"), CancellationToken.None);

        await second.Should().ThrowAsync<DomainException>().WithMessage("*already closed*");
        (await db.PeriodCloses.CountAsync()).Should().Be(1, "exactly one close succeeded");
    }

    [Fact]
    public async Task ClosingAnOverlappingRange_IsRefused_SoNoDateSitsInsideTwoClosures()
    {
        using var db = NewDb();
        await Handler(db).Handle(
            new ClosePeriodCommand(new DateTime(2026, 2, 1), new DateTime(2026, 2, 28), null), CancellationToken.None);

        var act = () => Handler(db).Handle(
            new ClosePeriodCommand(new DateTime(2026, 2, 15), new DateTime(2026, 3, 15), null), CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>().WithMessage("*already closed*");
        (await db.PeriodCloses.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task ClosingTakesTheExclusiveGlobalPeriodLock_AndStaysPossibleAfterAReopen()
    {
        using var db = NewDb();
        var lockPartner = new RecordingStockLock();
        var handler = new ClosePeriodCommandHandler(db, new StubCurrentUser("controller"), lockPartner);

        var closed = await handler.Handle(
            new ClosePeriodCommand(new DateTime(2026, 2, 1), new DateTime(2026, 2, 28), null), CancellationToken.None);

        lockPartner.NamedResources.Should().Contain(IPeriodCloseService.PeriodLockResource,
            "a close must serialize against every posting through the same global period resource");

        // A reopened period is no longer a closure, so the same range can be closed again.
        var period = await db.PeriodCloses.SingleAsync();
        period.Reopen("late supplier invoice", "controller");
        await db.SaveChangesAsync();

        var reopen = () => handler.Handle(
            new ClosePeriodCommand(new DateTime(2026, 2, 1), new DateTime(2026, 2, 28), null), CancellationToken.None);
        await reopen.Should().NotThrowAsync();
        _ = closed;
    }
}

// =====================================================================================
// 7. Invoice PDF: the factory identity comes from settings (name + logo), and a missing
//    logo still generates.
// =====================================================================================
public class Phase6InvoicePdfTests : Phase6TestBase
{
    private static InvoiceDto InvoiceDto() => new()
    {
        Id = Guid.NewGuid(),
        InvoiceNumber = "INV-2026-000042",
        InvoiceDate = Today,
        CustomerId = Guid.NewGuid(),
        CustomerCode = "C-1",
        CustomerName = "Customer",
        Status = InvoiceStatus.Issued,
        SubTotal = 3600m,
        Total = 3600m,
        Lines = new List<InvoiceLineDto>
        {
            new()
            {
                Id = Guid.NewGuid(), ItemId = Guid.NewGuid(), ItemCode = "FAB", Color = "Navy",
                Unit = UnitOfMeasure.Meter, Quantity = 1800m, ProcessingPrice = 2m, Value = 3600m
            }
        }
    };

    private static async Task<FileContentResult> RenderAsync(string nameAr, string nameEn, string? logo)
    {
        var dto = InvoiceDto();
        var sender = new Mock<ISender>();
        sender.Setup(s => s.Send(It.IsAny<GetInvoiceByIdQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(dto);

        var controller = new InvoicesController(sender.Object);
        var exporter = new ReportExportService(new TestCompanyIdentityProvider(nameAr, nameEn, logo));

        var result = await controller.GetInvoicePdf(dto.Id, exporter);
        return result.Should().BeOfType<FileContentResult>().Subject;
    }

    [Fact]
    public async Task TheInvoicePdf_UsesTheConfiguredFactoryName_AndStillGeneratesWithoutALogo()
    {
        var withoutLogo = await RenderAsync("شركة الأصباغ الحديثة", "Modern Dye House", null);

        withoutLogo.ContentType.Should().Be("application/pdf");
        withoutLogo.FileDownloadName.Should().Contain("INV-2026-000042");
        System.Text.Encoding.ASCII.GetString(withoutLogo.FileContents, 0, 5).Should().Be("%PDF-");
        withoutLogo.FileContents.Length.Should().BeGreaterThan(500);

        // The document is built from the CONFIGURED identity: renaming the factory changes the file
        // (and the previously hard-coded identity is gone).
        var renamed = await RenderAsync("شركة الأصباغ الحديثة", "Renamed Dye House", null);
        renamed.FileContents.Should().NotEqual(withoutLogo.FileContents);
    }

    [Fact]
    public async Task TheInvoicePdf_StillGenerates_WithALogo()
    {
        var withLogo = await RenderAsync("شركة", "Logo Co", TestCompanyIdentityProvider.OnePixelPngDataUrl);
        System.Text.Encoding.ASCII.GetString(withLogo.FileContents, 0, 5).Should().Be("%PDF-");
    }
}

// =====================================================================================
// 8. Permissions: every treasury / cheque / invoice endpoint names its own granular,
//    configurable permission (never a role), and the sensitive ones are distinct rights.
// =====================================================================================
public class Phase6PermissionTests
{
    public static IEnumerable<object[]> GuardedEndpoints => new List<object[]>
    {
        new object[] { typeof(InvoicesController), nameof(InvoicesController.Get), Permissions.InvoicesView },
        new object[] { typeof(InvoicesController), nameof(InvoicesController.Create), Permissions.InvoicesCreate },
        new object[] { typeof(InvoicesController), nameof(InvoicesController.AddLine), Permissions.InvoicesEdit },
        new object[] { typeof(InvoicesController), nameof(InvoicesController.UpdateLine), Permissions.InvoicesEdit },
        new object[] { typeof(InvoicesController), nameof(InvoicesController.RemoveLine), Permissions.InvoicesEdit },
        new object[] { typeof(InvoicesController), nameof(InvoicesController.Issue), Permissions.InvoicesIssue },
        new object[] { typeof(InvoicesController), nameof(InvoicesController.Cancel), Permissions.InvoicesCancel },
        new object[] { typeof(InvoicesController), nameof(InvoicesController.GetInvoicePdf), Permissions.ReportsExport },
        new object[] { typeof(CustomerStatementController), nameof(CustomerStatementController.Get), Permissions.InvoicesView },
        new object[] { typeof(ReceiptsController), nameof(ReceiptsController.Create), Permissions.TreasuryCreate },
        new object[] { typeof(ReceiptsController), nameof(ReceiptsController.Cancel), Permissions.TreasuryCancel },
        new object[] { typeof(PaymentsController), nameof(PaymentsController.Create), Permissions.TreasuryCreate },
        new object[] { typeof(PaymentsController), nameof(PaymentsController.Cancel), Permissions.TreasuryCancel },
        new object[] { typeof(TreasuryAccountsController), nameof(TreasuryAccountsController.Get), Permissions.TreasuryView },
        new object[] { typeof(TreasuryTransfersController), nameof(TreasuryTransfersController.Create), Permissions.TreasuryCreate },
        new object[] { typeof(TreasuryTransfersController), nameof(TreasuryTransfersController.Cancel), Permissions.TreasuryCancel },
        new object[] { typeof(ChecksController), nameof(ChecksController.Endorse), Permissions.ChecksEndorse },
        new object[] { typeof(ChecksController), nameof(ChecksController.SettleSupplier), Permissions.ChecksEndorse },
        new object[] { typeof(ChecksController), nameof(ChecksController.Deposit), Permissions.ChecksDeposit },
        new object[] { typeof(ChecksController), nameof(ChecksController.Clear), Permissions.ChecksClear },
        new object[] { typeof(ChecksController), nameof(ChecksController.Bounce), Permissions.ChecksBounce },
        new object[] { typeof(ChecksController), nameof(ChecksController.Cancel), Permissions.ChecksCancel },
        new object[] { typeof(PeriodClosingController), nameof(PeriodClosingController.Close), Permissions.SettingsManage }
    };

    [Theory]
    [MemberData(nameof(GuardedEndpoints))]
    public void EveryFinanceEndpoint_CarriesItsGranularPermissionPolicy(Type controller, string action, string permission)
    {
        var method = controller.GetMethod(action)
            ?? throw new InvalidOperationException($"{controller.Name}.{action} not found");

        var policies = method.GetCustomAttributes<AuthorizeAttribute>(inherit: true)
            .Select(a => a.Policy).Where(p => p is not null).ToList();

        policies.Should().Contain(PermissionPolicy.Prefix + permission,
            because: $"{controller.Name}.{action} must be controlled by the configurable '{permission}' right");

        method.GetCustomAttributes<AuthorizeAttribute>(inherit: true)
            .Should().NotContain(a => a.Roles != null,
                because: "a role gate would make the granular permission unusable for a non-admin user");
    }

    [Fact]
    public void TheFinanceRights_AreSeparatePermissions()
    {
        Permissions.InvoicesCreate.Should().NotBe(Permissions.InvoicesEdit);
        Permissions.InvoicesEdit.Should().NotBe(Permissions.InvoicesIssue);
        Permissions.InvoicesIssue.Should().NotBe(Permissions.InvoicesCancel);
        Permissions.TreasuryCreate.Should().NotBe(Permissions.TreasuryCancel);
        Permissions.ChecksEndorse.Should().NotBe(Permissions.ChecksClear);

        foreach (var permission in new[]
                 {
                     Permissions.InvoicesView, Permissions.InvoicesCreate, Permissions.InvoicesEdit,
                     Permissions.InvoicesIssue, Permissions.InvoicesCancel, Permissions.TreasuryView,
                     Permissions.TreasuryCreate, Permissions.TreasuryCancel, Permissions.ChecksEndorse,
                     Permissions.ChecksDeposit, Permissions.ChecksClear, Permissions.ChecksBounce
                 })
        {
            Permissions.All.Should().Contain(permission);
        }
    }
}
