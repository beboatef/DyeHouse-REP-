using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Enums;
using DyeHouseERP.Domain.Exceptions;
using FluentAssertions;
using Xunit;

namespace DyeHouseERP.UnitTests;

/// <summary>
/// Spec sections 28-33: a Formation Request carries many groups/cells, the reusable
/// specification cell is copied onto the request (snapshot) and master-data edits
/// afterwards can never rewrite a historical request.
/// </summary>
public class FormationRequestTests
{
    private static FormationRequest NewRequest(decimal totalQuantity = 500m) => new(
        "FRM-2026-000001", DateTime.UtcNow, Guid.NewGuid(), Guid.NewGuid(),
        UnitOfMeasure.KG, "tester", rawMessageId: Guid.NewGuid(), totalQuantity: totalQuantity);

    private static FormationSpecTemplate NewTemplate() => new(
        "SPEC-W160", "عرض 160", "Width 160", "tester",
        widthCm: 160m, metersPerKg: 5m, gsm: null, tubFormat: "50 Meter",
        qualityInstructions: "QC-A", customerInstructions: "Customer-A");

    [Fact]
    public void Submit_WithoutGroups_Throws()
    {
        var request = NewRequest();

        var act = () => request.Submit("tester");

        act.Should().Throw<DomainException>("a request with no groups/cells has nothing to produce (spec section 30)");
    }

    [Fact]
    public void Submit_WithoutRawMessage_Throws()
    {
        // Spec section 31: the chain customer -> raw message -> formation request
        // must never be broken, so a request cannot be submitted without its message.
        var request = new FormationRequest(
            "FRM-2026-000002", DateTime.UtcNow, Guid.NewGuid(), Guid.NewGuid(),
            UnitOfMeasure.KG, "tester", rawMessageId: null, totalQuantity: 100m);
        request.AddGroup("A", 100m, UnitOfMeasure.KG, 1, null, null, "tester");

        var act = () => request.Submit("tester");

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Submit_WhenTotalDoesNotMatchTheGroupSum_Throws()
    {
        var request = NewRequest(totalQuantity: 900m);
        request.AddGroup("A", 500m, UnitOfMeasure.KG, 6, "Blue", null, "tester");

        var act = () => request.Submit("tester");

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Submit_ThenApprove_FreezesTheSpecificationSnapshot()
    {
        var template = NewTemplate();
        var request = NewRequest();
        var group = request.AddGroup("Group A", 500m, UnitOfMeasure.KG, 6, "Blue", null, "tester");

        request.ApplySpecificationTemplate(group.Id, template, "tester");

        group.WidthCm.Should().Be(160m);
        group.MetersPerKg.Should().Be(5m);
        group.QualityInstructions.Should().Be("QC-A");
        group.SpecificationTemplateId.Should().Be(template.Id);

        request.Submit("tester");
        request.Approve("manager");

        request.Status.Should().Be(FormationRequestStatus.Approved);
        group.SpecificationSnapshotAtUtc.Should().NotBeNull("approval is the moment the specification is frozen");

        // The master cell is edited afterwards. The approved request must not move
        // (spec section 29: master data changes never rewrite history).
        template.SetSpecification(200m, 8m, 150m, "100 Meter", "Tape-B");
        template.SetInstructions("QC-B", null, null, "Customer-B");

        group.WidthCm.Should().Be(160m, "an approved request keeps its frozen snapshot");
        group.MetersPerKg.Should().Be(5m);
        group.QualityInstructions.Should().Be("QC-A");
        group.CustomerInstructions.Should().Be("Customer-A");
    }

    [Fact]
    public void ApprovedRequest_IsNoLongerEditable()
    {
        var request = NewRequest();
        var group = request.AddGroup("A", 500m, UnitOfMeasure.KG, 6, null, null, "tester");
        request.Submit("tester");
        request.Approve("manager");

        var addGroup = () => request.AddGroup("B", 10m, UnitOfMeasure.KG, 1, null, null, "tester");
        var applyTemplate = () => request.ApplySpecificationTemplate(group.Id, NewTemplate(), "tester");
        var updateGroup = () => request.UpdateGroup(
            groupId: group.Id, name: "renamed", plannedQuantity: 500m, unit: UnitOfMeasure.KG,
            tubCount: 6, color: "Red", widthCm: 200m, metersPerKg: 8m, gsm: null,
            tubFormat: null, windingTapeFormat: null, qualityInstructions: null, labInstructions: null,
            internalInstructions: null, customerInstructions: null, notes: null, modifiedBy: "tester");

        addGroup.Should().Throw<DomainException>();
        applyTemplate.Should().Throw<DomainException>();
        updateGroup.Should().Throw<DomainException>();
    }

    [Fact]
    public void RecordGroupProduction_AccumulatesAndDrivesTheCompletionStatus()
    {
        var request = NewRequest();
        var group = request.AddGroup("A", 500m, UnitOfMeasure.KG, 6, null, null, "tester");
        request.Submit("tester");
        request.Approve("manager");

        request.RecordGroupProduction(group.Id, 200m, "operator");

        request.Status.Should().Be(FormationRequestStatus.PartiallyCompleted);
        group.RemainingQuantity.Should().Be(300m);

        request.RecordGroupProduction(group.Id, 300m, "operator");

        request.Status.Should().Be(FormationRequestStatus.Completed);
        group.RemainingQuantity.Should().Be(0m);
        group.ProducedQuantity.Should().Be(500m, "quantities accumulate - the same output is never double counted");
    }
}

/// <summary>
/// Spec sections 37-40: a check is ONE financial instrument for its whole life.
/// Endorsing it to a supplier moves the holder; it never creates a second check
/// or new cash.
/// </summary>
public class CheckTests
{
    private static Check NewCustomerCheck(string holder = "Acme Textiles") => new(
        "CHK-1001", CheckDirection.CustomerCheck, "Bank Misr", 25000m,
        DateTime.UtcNow.Date, DateTime.UtcNow.Date.AddMonths(2), holder, holder,
        CheckHolderType.Customer, "tester", currency: "EGP", customerId: Guid.NewGuid());

    [Fact]
    public void Constructor_WithBothCustomerAndSupplier_Throws()
    {
        var act = () => new Check(
            "CHK-1002", CheckDirection.CustomerCheck, "Bank Misr", 100m,
            DateTime.UtcNow.Date, DateTime.UtcNow.Date.AddMonths(1), "Issuer", "Holder",
            CheckHolderType.Customer, "tester",
            customerId: Guid.NewGuid(), supplierId: Guid.NewGuid());

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void EndorsingToSupplier_KeepsTheSameInstrumentAndRecordsTheMovement()
    {
        var check = NewCustomerCheck();
        var supplierId = Guid.NewGuid();

        check.ConfirmReceipt(DateTime.UtcNow.Date, "DyeHouse Co.", "warehouse");
        check.Status.Should().Be(CheckStatus.InHand);
        check.CurrentHolderType.Should().Be(CheckHolderType.Company);

        var movementsBefore = check.Movements.Count;
        check.EndorseToSupplier(supplierId, "Chem Supplier", DateTime.UtcNow.Date, "accountant", "settle invoice");

        check.Status.Should().Be(CheckStatus.Endorsed);
        check.CurrentHolder.Should().Be("Chem Supplier");
        check.CurrentHolderType.Should().Be(CheckHolderType.Supplier);
        check.SupplierId.Should().Be(supplierId);
        check.CheckNumber.Should().Be("CHK-1001", "the same physical check, never a new one");
        check.Amount.Should().Be(25000m, "endorsement never creates new cash");
        check.Movements.Should().HaveCount(movementsBefore + 1);

        var last = check.Movements.Last();
        last.MovementType.Should().Be(CheckMovementType.Endorsed);
        last.FromHolder.Should().Be("DyeHouse Co.");
        last.ToHolder.Should().Be("Chem Supplier");
    }

    [Fact]
    public void EndorsingADepositedCheck_Throws()
    {
        var check = NewCustomerCheck();
        check.ConfirmReceipt(DateTime.UtcNow.Date, "DyeHouse Co.", "warehouse");
        check.Deposit(Guid.NewGuid(), "CIB Current", DateTime.UtcNow.Date, "accountant");

        var act = () => check.EndorseToSupplier(Guid.NewGuid(), "Some Supplier", DateTime.UtcNow.Date, "accountant", null);

        act.Should().Throw<DomainException>("an instrument already at the bank is no longer ours to hand over");
    }

    [Fact]
    public void ClearingIsFinal_AndAClearedCheckCanNeverBeCancelled()
    {
        var check = NewCustomerCheck();
        check.ConfirmReceipt(DateTime.UtcNow.Date, "DyeHouse Co.", "warehouse");
        check.Deposit(Guid.NewGuid(), "CIB Current", DateTime.UtcNow.Date, "accountant");
        check.Clear(DateTime.UtcNow.Date.AddDays(5), "accountant");

        check.Status.Should().Be(CheckStatus.Cleared);
        check.IsFinal.Should().BeTrue();
        check.ClearedAtUtc.Should().NotBeNull();

        var cancel = () => check.Cancel(DateTime.UtcNow.Date, "accountant", "changed my mind");
        var endorse = () => check.EndorseToSupplier(Guid.NewGuid(), "S", DateTime.UtcNow.Date, "accountant", null);

        cancel.Should().Throw<DomainException>();
        endorse.Should().Throw<DomainException>();
    }

    [Fact]
    public void BouncedCheck_RequiresAReason_AndCanBePhysicallyReturnedToTheCompany()
    {
        var check = NewCustomerCheck();
        check.ConfirmReceipt(DateTime.UtcNow.Date, "DyeHouse Co.", "warehouse");
        check.Deposit(Guid.NewGuid(), "CIB Current", DateTime.UtcNow.Date, "accountant");

        var noReason = () => check.Bounce(DateTime.UtcNow.Date, "accountant", "");
        noReason.Should().Throw<DomainException>();

        check.Bounce(DateTime.UtcNow.Date, "accountant", "insufficient funds");
        check.Status.Should().Be(CheckStatus.Bounced);
        check.BounceReason.Should().Be("insufficient funds");

        check.ReturnToCompany(DateTime.UtcNow.Date.AddDays(2), "DyeHouse Co.", "accountant", "returned by bank");
        check.Status.Should().Be(CheckStatus.InHand);
        check.CurrentHolderType.Should().Be(CheckHolderType.Company);
        check.Movements.Should().Contain(m => m.MovementType == CheckMovementType.Bounced);
        check.Movements.Should().Contain(m => m.MovementType == CheckMovementType.ReturnedToHolder);
    }
}
