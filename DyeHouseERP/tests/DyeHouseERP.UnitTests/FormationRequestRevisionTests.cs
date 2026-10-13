using DyeHouseERP.Application.FormationRequests.Commands;
using DyeHouseERP.Domain.Common;
using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Enums;
using DyeHouseERP.Domain.Exceptions;
using FluentAssertions;
using Xunit;

namespace DyeHouseERP.UnitTests;

/// <summary>
/// Spec sections 29-33: an approved formation request freezes its specification snapshot and accumulates
/// production, so it is never edited in place. The correction path is a REVISION: a new Draft that copies the
/// plan, links back to the original and must be submitted and approved on its own. These tests pin the parts
/// that need no database - the revision marker's own guards and the permission - while the copy itself is
/// exercised through the command handler in the application layer.
/// </summary>
public class FormationRequestRevisionTests
{
    private static FormationRequest NewDraft()
        // A raw message is required to submit, and the header total must equal the group sum - both
        // supplied here so the submit path used by the revision test is realistic.
        => new("FRM-1", DateTime.UtcNow, Guid.NewGuid(), Guid.NewGuid(), UnitOfMeasure.KG, "planner",
            rawMessageId: Guid.NewGuid(), totalQuantity: 100m);

    [Fact]
    public void ARevision_CanBeMarked_WhileDraft_AndLinksToTheOriginal()
    {
        var originalId = Guid.NewGuid();
        var revision = NewDraft();

        revision.MarkAsRevision(originalId, "planner");

        revision.RevisionOfFormationRequestId.Should().Be(originalId);
    }

    [Fact]
    public void ARerevision_IsRefused()
    {
        var revision = NewDraft();
        revision.MarkAsRevision(Guid.NewGuid(), "planner");

        var act = () => revision.MarkAsRevision(Guid.NewGuid(), "planner");
        act.Should().Throw<DomainException>().WithMessage("*already a revision*");
    }

    [Fact]
    public void AMarkedRevision_IsRefusedOnceItIsNoLongerDraft()
    {
        var request = NewDraft();
        request.AddGroup("G1", 100m, UnitOfMeasure.KG, 4, null, null, "planner");
        request.Submit("planner");

        var act = () => request.MarkAsRevision(Guid.NewGuid(), "planner");
        act.Should().Throw<DomainException>().WithMessage("*draft*");
    }

    [Fact]
    public void ReviseCommand_RequiresAnId_AndTheRightIsConfigurable()
    {
        new ReviseFormationRequestCommandValidator()
            .Validate(new ReviseFormationRequestCommand(Guid.Empty))
            .IsValid.Should().BeFalse();

        new ReviseFormationRequestCommandValidator()
            .Validate(new ReviseFormationRequestCommand(Guid.NewGuid()))
            .IsValid.Should().BeTrue();

        Permissions.FormationEdit.Should().NotBe(Permissions.Admin);
        Permissions.All.Should().Contain(Permissions.FormationEdit);
    }
}
