using DyeHouseERP.Application.Invoices.Commands;
using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Enums;
using DyeHouseERP.Domain.Exceptions;
using FluentAssertions;
using Xunit;

namespace DyeHouseERP.UnitTests;

/// <summary>
/// Spec section 32: a DRAFT invoice has posted nothing (no customer-ledger row), so its header and
/// its lines are ordinary working data and must be editable behind a configurable permission. The
/// moment it is Issued it is financially real: every edit is then refused by the domain, because the
/// correction path is cancellation/reversal - never editing a posted document in place.
/// </summary>
public class InvoiceDraftEditTests
{
    private static Invoice NewInvoice()
    {
        var invoice = new Invoice("INV-1", DateTime.UtcNow, Guid.NewGuid(), "accountant");
        invoice.AddLine(null, Guid.NewGuid(), "blue", 10m, 5m, UnitOfMeasure.KG, null);
        invoice.AddLine(null, Guid.NewGuid(), "red", 4m, 2.5m, UnitOfMeasure.KG, null);
        return invoice;
    }

    [Fact]
    public void Header_IsEditable_WhileDraft_AndTotalsFollow()
    {
        var invoice = NewInvoice();
        invoice.Discount.Should().Be(0m);
        invoice.SubTotal.Should().Be(60m); // 10*5 + 4*2.5

        invoice.UpdateHeader(DateTime.UtcNow.Date, deliveryId: null, discount: 6m, tax: 2m,
            notes: "agreed", modifiedBy: "accountant");

        invoice.Discount.Should().Be(6m);
        invoice.Tax.Should().Be(2m);
        invoice.Notes.Should().Be("agreed");
        invoice.Total.Should().Be(56m); // 60 - 6 + 2
    }

    [Fact]
    public void Line_IsEditable_WhileDraft_AndTotalFollows()
    {
        var invoice = NewInvoice();
        var line = invoice.Lines.First();

        invoice.UpdateLine(line.Id, quantity: 20m, processingPrice: 5m, unit: UnitOfMeasure.KG,
            color: "green", notes: "fixed", modifiedBy: "accountant");

        invoice.SubTotal.Should().Be(110m); // 20*5 + 4*2.5
        line.Color.Should().Be("green");
    }

    [Fact]
    public void Line_CanBeAdded_AndRemoved_WhileDraft()
    {
        var invoice = NewInvoice();
        var added = invoice.AddLine(null, Guid.NewGuid(), null, 1m, 3m, UnitOfMeasure.KG, null);
        invoice.Lines.Should().HaveCount(3);

        invoice.RemoveLine(added.Id, "accountant");
        invoice.Lines.Should().HaveCount(2);
        invoice.SubTotal.Should().Be(60m);
    }

    [Fact]
    public void Editing_AnIssuedInvoice_IsRefused()
    {
        var invoice = NewInvoice();
        invoice.Issue();

        var header = () => invoice.UpdateHeader(DateTime.UtcNow, null, 0, 0, null, "accountant");
        var line = () => invoice.UpdateLine(invoice.Lines.First().Id, 1m, 1m, UnitOfMeasure.KG, null, null, "accountant");
        var remove = () => invoice.RemoveLine(invoice.Lines.First().Id, "accountant");
        var add = () => invoice.AddLine(null, Guid.NewGuid(), null, 1m, 1m, UnitOfMeasure.KG, null);

        header.Should().Throw<DocumentLockedException>();
        line.Should().Throw<DocumentLockedException>();
        remove.Should().Throw<DocumentLockedException>();
        add.Should().Throw<DocumentLockedException>();
    }

    [Fact]
    public void UnknownLine_IsRefused_NotSilentlyIgnored()
    {
        var invoice = NewInvoice();
        var act = () => invoice.UpdateLine(Guid.NewGuid(), 1m, 1m, UnitOfMeasure.KG, null, null, "accountant");
        act.Should().Throw<DomainException>().WithMessage("*does not belong to invoice*");
    }

    [Fact]
    public void Validators_RejectBadValues()
    {
        new UpdateInvoiceCommandValidator()
            .Validate(new UpdateInvoiceCommand(Guid.NewGuid(), DateTime.UtcNow, null, -1m, 0m, null))
            .IsValid.Should().BeFalse("a negative discount is refused at the boundary");

        new UpdateInvoiceLineCommandValidator()
            .Validate(new UpdateInvoiceLineCommand(Guid.NewGuid(), Guid.NewGuid(), 0m, 5m, UnitOfMeasure.KG, null, null))
            .IsValid.Should().BeFalse("a zero quantity is refused at the boundary");

        new AddInvoiceLineCommandValidator()
            .Validate(new AddInvoiceLineCommand(Guid.NewGuid(), null, Guid.Empty, null, 1m, 1m, UnitOfMeasure.KG, null))
            .IsValid.Should().BeFalse("an empty item is refused at the boundary");

        new AddInvoiceLineCommandValidator()
            .Validate(new AddInvoiceLineCommand(Guid.NewGuid(), null, Guid.NewGuid(), null, 1m, 1m, (UnitOfMeasure)999, null))
            .IsValid.Should().BeFalse("the billing unit must be a real unit - the billing unit follows the service price");

        new RemoveInvoiceLineCommandValidator()
            .Validate(new RemoveInvoiceLineCommand(Guid.NewGuid(), Guid.NewGuid()))
            .IsValid.Should().BeTrue();
    }

    [Fact]
    public void InvoiceEdit_IsAConfigurablePermissionOfItsOwn()
    {
        DyeHouseERP.Domain.Common.Permissions.InvoicesEdit.Should().NotBe(DyeHouseERP.Domain.Common.Permissions.InvoicesCreate);
        DyeHouseERP.Domain.Common.Permissions.All.Should().Contain(DyeHouseERP.Domain.Common.Permissions.InvoicesEdit);
    }
}
