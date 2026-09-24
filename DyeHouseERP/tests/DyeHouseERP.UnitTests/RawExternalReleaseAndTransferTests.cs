using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace DyeHouseERP.UnitTests;

public class RawExternalReleaseTests
{
    [Fact]
    public void Constructor_WithNeitherKgNorMeter_Throws()
    {
        var act = () => new RawExternalRelease(
            "REL-2026-000001", DateTime.UtcNow, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            quantityKg: null, quantityMeter: null, RawReleaseReason.Sale, "tester");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_RequiringApproval_WithoutApprover_Throws()
    {
        var act = () => new RawExternalRelease(
            "REL-2026-000002", DateTime.UtcNow, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            quantityKg: 100m, quantityMeter: null, RawReleaseReason.ExternalProcessing, "tester",
            approvalRequired: true, approvedBy: null);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_WithValidData_Succeeds()
    {
        var release = new RawExternalRelease(
            "REL-2026-000003", DateTime.UtcNow, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            quantityKg: 250m, quantityMeter: null, RawReleaseReason.ReturnToCustomer, "tester");

        release.QuantityKg.Should().Be(250m);
        release.Reason.Should().Be(RawReleaseReason.ReturnToCustomer);
    }
}

public class CustomerTransferTests
{
    [Fact]
    public void Constructor_TransferringToSameCustomer_Throws()
    {
        var customerId = Guid.NewGuid();

        var act = () => new CustomerTransfer(
            "TRF-2026-000001", DateTime.UtcNow, customerId, customerId,
            Guid.NewGuid(), Guid.NewGuid(), 100m, null, "test reason", "tester");

        act.Should().Throw<ArgumentException>("a customer cannot transfer raw material to themselves");
    }

    [Fact]
    public void Constructor_WithoutReason_Throws()
    {
        var act = () => new CustomerTransfer(
            "TRF-2026-000002", DateTime.UtcNow, Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), 100m, null, reason: "", createdBy: "tester");

        act.Should().Throw<ArgumentException>("spec section 15 requires a documented reason for every transfer");
    }

    [Fact]
    public void Constructor_WithValidData_Succeeds()
    {
        var fromCustomer = Guid.NewGuid();
        var toCustomer = Guid.NewGuid();

        var transfer = new CustomerTransfer(
            "TRF-2026-000003", DateTime.UtcNow, fromCustomer, toCustomer,
            Guid.NewGuid(), Guid.NewGuid(), 400m, null, "customer consolidation", "tester");

        transfer.FromCustomerId.Should().Be(fromCustomer);
        transfer.ToCustomerId.Should().Be(toCustomer);
        transfer.QuantityKg.Should().Be(400m);
    }
}
