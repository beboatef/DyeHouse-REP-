using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Enums;
using DyeHouseERP.Domain.Exceptions;
using FluentAssertions;
using Xunit;

namespace DyeHouseERP.UnitTests;

/// <summary>
/// Spec section 35: a purchase order has no stock or accounting effect by itself,
/// receiving is what moves stock, and an order can never be over-received.
/// </summary>
public class PurchaseOrderTests
{
    private static PurchaseOrder NewOrder() => new(
        "PO-2026-000001", DateTime.UtcNow, Guid.NewGuid(), Guid.NewGuid(), "tester");

    private static PurchaseOrder ApprovedOrder(out PurchaseOrderLine line)
    {
        var order = NewOrder();
        line = order.AddLine(Guid.NewGuid(), 100m, MaterialUnit.KG, 12.5m, null, "tester");
        order.Submit("tester");
        order.Approve("manager");
        return order;
    }

    [Fact]
    public void Submit_WithoutLines_Throws()
    {
        var order = NewOrder();

        var act = () => order.Submit("tester");

        act.Should().Throw<DomainException>("an empty purchase order has nothing to approve");
    }

    [Fact]
    public void Receive_BeforeApproval_Throws()
    {
        var order = NewOrder();
        var line = order.AddLine(Guid.NewGuid(), 100m, MaterialUnit.KG, 10m, null, "tester");

        var act = () => order.RecordReceivedQuantity(line.Id, 10m, MaterialUnit.KG, "warehouse");

        act.Should().Throw<DomainException>("stock can only arrive against an approved order");
    }

    [Fact]
    public void Receive_MoreThanOrdered_Throws()
    {
        var order = ApprovedOrder(out var line);

        var act = () => order.RecordReceivedQuantity(line.Id, 150m, MaterialUnit.KG, "warehouse");

        act.Should().Throw<DomainException>("received quantity can never exceed the ordered quantity (spec section 55)");
    }

    [Fact]
    public void Receive_InADifferentUnit_Throws()
    {
        var order = ApprovedOrder(out var line);

        var act = () => order.RecordReceivedQuantity(line.Id, 10m, MaterialUnit.Liter, "warehouse");

        act.Should().Throw<DomainException>("units are never auto-converted (spec section 6)");
    }

    [Fact]
    public void PartialThenFullReceipt_DrivesTheOrderStatus()
    {
        var order = ApprovedOrder(out var line);

        order.RecordReceivedQuantity(line.Id, 40m, MaterialUnit.KG, "warehouse");
        order.Status.Should().Be(PurchaseOrderStatus.PartiallyReceived);
        line.OutstandingQuantity.Should().Be(60m);

        order.RecordReceivedQuantity(line.Id, 60m, MaterialUnit.KG, "warehouse");
        order.Status.Should().Be(PurchaseOrderStatus.Received);
        line.OutstandingQuantity.Should().Be(0m);
        line.ReceivedQuantity.Should().Be(100m, "received quantities accumulate, never overwrite");
    }

    [Fact]
    public void LineThatWasReceivedFrom_CanNoLongerBeEditedOrRemoved()
    {
        var order = ApprovedOrder(out var line);
        order.RecordReceivedQuantity(line.Id, 20m, MaterialUnit.KG, "warehouse");

        // Receiving puts the order into PartiallyReceived, which is not editable.
        var update = () => order.UpdateLine(line.Id, 500m, MaterialUnit.KG, 1m, null, "buyer");
        var remove = () => order.RemoveLine(line.Id, "buyer");

        update.Should().Throw<DomainException>();
        remove.Should().Throw<DomainException>();
    }

    [Fact]
    public void Cancel_RequiresAReason_AndKeepsWhatAlreadyArrived()
    {
        var order = ApprovedOrder(out var line);
        order.RecordReceivedQuantity(line.Id, 30m, MaterialUnit.KG, "warehouse");

        var noReason = () => order.Cancel("buyer", "");
        noReason.Should().Throw<DomainException>();

        order.Cancel("buyer", "supplier cannot deliver the rest");

        order.Status.Should().Be(PurchaseOrderStatus.Cancelled);
        order.CancellationReason.Should().Be("supplier cannot deliver the rest");
        line.ReceivedQuantity.Should().Be(30m, "received stock is real and stays recorded (spec section 53)");
    }
}

/// <summary>
/// Spec sections 35 and 41: posting a supplier invoice is the single moment a
/// payable exists, and a posted invoice is cancelled, never edited or deleted.
/// </summary>
public class SupplierInvoiceTests
{
    private static SupplierInvoice NewInvoice() => new(
        "SI-9001", DateTime.UtcNow.Date, DateTime.UtcNow.Date.AddDays(30), Guid.NewGuid(), "accountant");

    [Fact]
    public void DueDate_BeforeInvoiceDate_Throws()
    {
        var act = () => new SupplierInvoice(
            "SI-9002", DateTime.UtcNow.Date, DateTime.UtcNow.Date.AddDays(-1), Guid.NewGuid(), "accountant");

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Post_WithoutLines_Throws()
    {
        var invoice = NewInvoice();

        var act = () => invoice.Post("accountant");

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Total_IsDerivedFromTheLinesMinusDiscountPlusTax()
    {
        var invoice = NewInvoice();
        invoice.AddLine(Guid.NewGuid(), "Caustic soda", 200m, MaterialUnit.KG, 5m);
        invoice.AddLine(Guid.Empty, "Freight", 1m, MaterialUnit.KG, 300m);

        invoice.SubTotal.Should().Be(1300m);

        invoice.UpdateHeader(invoice.InvoiceDate, invoice.DueDate, null, discount: 100m, tax: 180m, "accountant");

        invoice.Total.Should().Be(1380m, "total is derived, never a stored column that could drift");
    }

    [Fact]
    public void APostedInvoice_CanNoLongerBeEdited_ButCanBeCancelled()
    {
        var invoice = NewInvoice();
        invoice.AddLine(Guid.NewGuid(), "Dye", 10m, MaterialUnit.KG, 100m);
        invoice.Post("accountant");

        var addLine = () => invoice.AddLine(Guid.NewGuid(), "More dye", 1m, MaterialUnit.KG, 100m);
        addLine.Should().Throw<DomainException>();

        invoice.Cancel("accountant", "duplicate invoice from the supplier");

        invoice.Status.Should().Be(SupplierInvoiceStatus.Cancelled);
        invoice.CancellationReason.Should().Be("duplicate invoice from the supplier");
        invoice.Total.Should().Be(1000m, "the cancelled amount is still there to post the reversal against");
    }
}

/// <summary>
/// Spec section 36: one line per employee per period (never paid twice), and a run
/// can only be posted after it was approved and given a paying account.
/// </summary>
public class PayrollRunTests
{
    private static PayrollRun NewRun(Guid? accountId = null) => new(
        "PRL-2026-000001", 2026, 3, "hr", accountId);

    private static PayrollRun RunWithOneLine(Guid? accountId = null)
    {
        var run = NewRun(accountId);
        run.AddLine(Guid.NewGuid(), "EMP-001", "Ali Hassan", Guid.NewGuid(), "Dyeing", 6000m, 500m, 250m);
        return run;
    }

    [Fact]
    public void InvalidPeriod_Throws()
    {
        var act = () => new PayrollRun("PRL-2026-000002", 2026, 13, "hr");

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void TheSameEmployee_CannotBePaidTwiceInOneRun()
    {
        var run = NewRun();
        var employeeId = Guid.NewGuid();
        run.AddLine(employeeId, "EMP-001", "Ali Hassan", Guid.NewGuid(), "Dyeing", 6000m, 0m, 0m);

        var act = () => run.AddLine(employeeId, "EMP-001", "Ali Hassan", Guid.NewGuid(), "Dyeing", 6000m, 0m, 0m);

        act.Should().Throw<DomainException>("a period is never paid twice (spec section 53)");
    }

    [Fact]
    public void LineAmounts_DeriveGrossAndNet()
    {
        var run = RunWithOneLine();

        var line = run.Lines.Single();
        line.GrossPay.Should().Be(6500m);
        line.NetPay.Should().Be(6250m);
        run.TotalNet.Should().Be(6250m);
    }

    [Fact]
    public void Post_WithoutApproval_Throws()
    {
        var run = RunWithOneLine(Guid.NewGuid());

        var act = () => run.Post("accountant");

        act.Should().Throw<DomainException>("only an approved run can be posted");
    }

    [Fact]
    public void Approve_WithoutAPayingAccount_IsStillBlockedAtPosting()
    {
        var run = RunWithOneLine(); // no treasury account selected
        run.Approve("manager");

        var act = () => run.Post("accountant");

        act.Should().Throw<DomainException>("a payroll run must name the account it is paid from");
    }

    [Fact]
    public void ApproveThenPost_IsTheOnlyFinancialStep()
    {
        var accountId = Guid.NewGuid();
        var run = RunWithOneLine(accountId);

        run.Status.Should().Be(PayrollRunStatus.Draft);
        run.Approve("manager");
        run.Status.Should().Be(PayrollRunStatus.Approved);

        run.Post("accountant");
        run.Status.Should().Be(PayrollRunStatus.Posted);
        run.PostedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public void Cancel_RequiresAReason_AndPostedRunsStayTraceable()
    {
        var run = RunWithOneLine(Guid.NewGuid());
        run.Approve("manager");
        run.Post("accountant");

        var noReason = () => run.Cancel("accountant", "");
        noReason.Should().Throw<DomainException>();

        run.Cancel("accountant", "wrong period");

        run.Status.Should().Be(PayrollRunStatus.Cancelled);
        run.TotalNet.Should().Be(6250m, "the posted amount is kept so the reversal can reference it");
    }
}
