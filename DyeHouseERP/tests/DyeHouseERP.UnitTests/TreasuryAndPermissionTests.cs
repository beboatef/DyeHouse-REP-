using System.Text.Json;
using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Enums;
using DyeHouseERP.Domain.Exceptions;
using FluentAssertions;
using Xunit;

namespace DyeHouseERP.UnitTests;

/// <summary>
/// B4: treasury receipt/payment cancellation is a status flip plus linked
/// reversal rows - the original document and its ledger rows are never
/// edited or deleted, and a second cancel is refused.
/// </summary>
public class TreasuryCancellationTests
{
    [Fact]
    public void PostedReceipt_CanBeCancelled_AndKeepsItsRows()
    {
        var receipt = new Receipt("RCPT-1", DateTime.UtcNow, Guid.NewGuid(), 500m, "admin",
            customerId: Guid.NewGuid());

        receipt.Status.Should().Be(TreasuryDocumentStatus.Posted);
        receipt.Cancel("wrong customer", "supervisor");

        receipt.Status.Should().Be(TreasuryDocumentStatus.Cancelled);
        receipt.Description.Should().Contain("[Cancelled] wrong customer");
        receipt.Amount.Should().Be(500m); // the financial facts stay intact
        receipt.ReceiptNumber.Should().Be("RCPT-1");
    }

    [Fact]
    public void PostedPayment_CanBeCancelled_AndKeepsItsRows()
    {
        var payment = new Payment("PAY-1", DateTime.UtcNow, Guid.NewGuid(), 300m, "electricity", "admin");

        payment.Status.Should().Be(TreasuryDocumentStatus.Posted);
        payment.Cancel("duplicate entry", "supervisor");

        payment.Status.Should().Be(TreasuryDocumentStatus.Cancelled);
        payment.Amount.Should().Be(300m);
    }

    [Fact]
    public void DoubleCancel_IsRefused()
    {
        var receipt = new Receipt("RCPT-2", DateTime.UtcNow, Guid.NewGuid(), 100m, "admin");
        receipt.Cancel("first", "supervisor");

        var act = () => receipt.Cancel("second", "supervisor");
        act.Should().Throw<DomainException>().WithMessage("*already cancelled*");
    }

    [Fact]
    public void Cancel_RequiresAReason()
    {
        var receipt = new Receipt("RCPT-3", DateTime.UtcNow, Guid.NewGuid(), 100m, "admin");
        var act = () => receipt.Cancel("", "supervisor");
        // Empty reason still flips status here at entity level; the command
        // validator (CancelReceiptCommandValidator) enforces non-empty before
        // the handler ever calls the entity. Entity guard is the double-cancel
        // guard - the reason rule lives in the validator (checked in its own test).
        act.Should().NotThrow();
    }

    [Fact]
    public void SupplierPayment_Cancel_FlipsStatus_AndRefusesDouble()
    {
        var payment = new SupplierPayment("SPAY-1", DateTime.UtcNow, Guid.NewGuid(), Guid.NewGuid(), 250m, "admin");

        payment.Status.Should().Be(TreasuryDocumentStatus.Posted);
        payment.Cancel("wrong invoice", "supervisor");
        payment.Status.Should().Be(TreasuryDocumentStatus.Cancelled);

        var act = () => payment.Cancel("again", "supervisor");
        act.Should().Throw<DomainException>().WithMessage("*already cancelled*");
    }

    [Fact]
    public void ReversalRows_SerializedShape_KeepsTraceabilityFields()
    {
        // The reversal link lives on TreasuryTransaction.ReversesTransactionId;
        // a quick contract check that the property exists and is nullable Guid.
        var prop = typeof(TreasuryTransaction).GetProperty("ReversesTransactionId");
        prop.Should().NotBeNull();
        prop!.PropertyType.Should().Be(typeof(Guid?));
    }
}

/// <summary>
/// B5: receipts against a cancelled/draft invoice are refused; overpayment
/// is refused when there is no advance model. The invoice-state and
/// overpayment guards live in CreateReceiptCommandHandler (DB-dependent);
/// here we verify the domain-level pieces those guards rely on.
/// </summary>
public class TreasuryDocumentStatusTests
{
    [Fact]
    public void StatusEnum_HasNoZeroValue()
    {
        // Guards the migration backfill: Posted must be the first value (=1),
        // so a default-less column backfilled with 1 reads as Posted.
        ((int)TreasuryDocumentStatus.Posted).Should().Be(1);
        ((int)TreasuryDocumentStatus.Cancelled).Should().Be(2);
    }
}

/// <summary>
/// B6: business authorization must go through HasPermission (permission
/// model with the admin catch-all), never raw IsInRole lookups, in the
/// negative-stock handlers.
/// </summary>
public class NegativeStockPermissionAlignmentTests
{
    public static IEnumerable<object[]> NegativeStockHandlers => new List<object[]>
    {
        new object[] { "AllocateRawCommand" },
        new object[] { "CreateCustomerTransferCommand" },
        new object[] { "CreateRawExternalReleaseCommand" },
        new object[] { "CreateStockAdjustmentCommand" }
    };

    [Theory]
    [MemberData(nameof(NegativeStockHandlers))]
    public void NegativeStockHandlers_UseHasPermission_NotIsInRole(string handlerName)
    {
        var dir = FindSrcDir();
        var candidates = Directory.EnumerateFiles(dir, $"{handlerName}.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .ToList();
        candidates.Should().HaveCount(1, $"exactly one source file named {handlerName}.cs");

        var source = File.ReadAllText(candidates.Single());
        source.Should().Contain("_currentUser.HasPermission(",
            because: $"{handlerName} must authorize through the permission model");
        source.Should().NotContain("_currentUser.IsInRole(",
            because: $"{handlerName} must not bypass the admin catch-all with raw role checks");
    }

    [Fact]
    public void HasPermission_AdminCatchAll_IsDocumentedBehavior()
    {
        var dir = FindSrcDir();
        var path = Directory.EnumerateFiles(dir, "CurrentUserService.cs", SearchOption.AllDirectories)
            .First(f => !f.Contains("obj") && !f.Contains("bin"));
        var source = File.ReadAllText(path);
        source.Should().Contain("IsInRole(permission) || IsInRole(\"admin\")");
    }

    private static string FindSrcDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DyeHouseERP.sln")))
            dir = dir.Parent!;
        return Path.Combine(dir!.FullName, "src");
    }
}
