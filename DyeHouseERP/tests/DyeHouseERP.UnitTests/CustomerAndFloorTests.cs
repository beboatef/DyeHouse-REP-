using DyeHouseERP.Application.ProductionOrders.Queries;
using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace DyeHouseERP.UnitTests;

/// <summary>
/// Customer master data completion (spec section 7): contact/tax details, an editable name,
/// and deactivation instead of deletion. The Code stays immutable because it is the key every
/// downstream document already refers to.
/// </summary>
public class CustomerMasterTests
{
    private static Customer NewCustomer(string code = "CUS-001", string name = "Customer One")
        => new(code, name, "tester");

    [Fact]
    public void NewCustomer_StartsWithoutContactDetails()
    {
        var customer = NewCustomer();

        customer.Phone.Should().BeNull();
        customer.Address.Should().BeNull();
        customer.ContactPerson.Should().BeNull();
        customer.TaxNumber.Should().BeNull();
        customer.IsActive.Should().BeTrue();
    }

    [Fact]
    public void SetContact_StoresTheDetailsAndTrimsThem()
    {
        var customer = NewCustomer();

        customer.SetContact(" 01000000000 ", "  Cairo  ", "  Sales  ", "  T-1  ", "tester");

        customer.Phone.Should().Be("01000000000");
        customer.Address.Should().Be("Cairo");
        customer.ContactPerson.Should().Be("Sales");
        customer.TaxNumber.Should().Be("T-1");
    }

    [Fact]
    public void SetContact_WithBlankValues_ClearsThemRatherThanStoringWhitespace()
    {
        var customer = NewCustomer();
        customer.SetContact("0100", "Cairo", "Sales", "T-1", "tester");

        customer.SetContact("   ", "", null, "  ", "tester");

        customer.Phone.Should().BeNull();
        customer.Address.Should().BeNull();
        customer.ContactPerson.Should().BeNull();
        customer.TaxNumber.Should().BeNull();
    }

    [Fact]
    public void SetName_ChangesTheNameButNeverTheCode()
    {
        var customer = NewCustomer();

        customer.SetName("Customer One Renamed", "tester");

        customer.Name.Should().Be("Customer One Renamed");
        customer.Code.Should().Be("CUS-001", "the code is the key every document refers to");
        customer.AccountNumber.Should().Be("CUS-001");
    }

    [Fact]
    public void SetName_WithBlankName_Throws()
    {
        var customer = NewCustomer();

        var act = () => customer.SetName("   ", "tester");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Deactivate_KeepsTheRecordResolvableInsteadOfDeletingIt()
    {
        var customer = NewCustomer();

        customer.Deactivate("tester");

        customer.IsActive.Should().BeFalse();
        customer.Code.Should().Be("CUS-001");
        customer.Name.Should().Be("Customer One");

        customer.Activate("tester");
        customer.IsActive.Should().BeTrue();
    }

    [Fact]
    public void SetContact_RecordsWhoChangedIt()
    {
        var customer = NewCustomer();

        customer.SetContact("0100", "Cairo", "Sales", "T-1", "importer");

        customer.ModifiedBy.Should().Be("importer");
        customer.ModifiedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public void AccountNumber_DefaultsToTheCodeOnCreation()
    {
        var customer = NewCustomer("CUS-777");

        customer.AccountNumber.Should().Be("CUS-777");
    }
}

/// <summary>
/// The factory floor orders the shift by urgency: priority first, then how long a job has been
/// sitting on its stage. A very old normal job must never outrank an urgent one.
/// </summary>
public class ProductionFloorUrgencyTests
{
    [Fact]
    public void UrgentJob_OutranksAHighJob_OnTheSameStageTime()
    {
        var urgent = ProductionFloorSorting.Urgency(ProductionPriority.Urgent, 10);
        var high = ProductionFloorSorting.Urgency(ProductionPriority.High, 10);

        urgent.Should().BeGreaterThan(high);
    }

    [Fact]
    public void OlderJob_OnTheSamePriority_RanksHigher()
    {
        var fresh = ProductionFloorSorting.Urgency(ProductionPriority.Normal, 10);
        var old = ProductionFloorSorting.Urgency(ProductionPriority.Normal, 400);

        old.Should().BeGreaterThan(fresh);
    }

    [Fact]
    public void WaitingTime_IsCapped_SoAnAncientNormalJobCannotOutrankAnUrgentOne()
    {
        var ancientNormal = ProductionFloorSorting.Urgency(ProductionPriority.Normal, 100_000);
        var urgent = ProductionFloorSorting.Urgency(ProductionPriority.Urgent, 0);

        urgent.Should().BeGreaterThan(ancientNormal,
            "priority decides first; a job that has been open for days is not allowed to mask an urgent one");
    }

    [Fact]
    public void JobWithNoStageTime_StillRanksByPriority()
    {
        var waiting = ProductionFloorSorting.Urgency(ProductionPriority.High, null);
        var fresh = ProductionFloorSorting.Urgency(ProductionPriority.High, 0);

        waiting.Should().Be(fresh);
    }
}