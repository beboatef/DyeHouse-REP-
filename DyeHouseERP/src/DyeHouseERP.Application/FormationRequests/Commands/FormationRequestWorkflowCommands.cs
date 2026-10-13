using DyeHouseERP.Application.Common.Exceptions;
using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.FormationRequests.DTOs;
using DyeHouseERP.Application.FormationRequests.Queries;
using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Enums;
using DyeHouseERP.Domain.Exceptions;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.FormationRequests.Commands;

/// <summary>
/// The controlled, audited status workflow of a Formation Request (spec sections 32-33):
/// Draft -&gt; Submitted -&gt; Approved -&gt; In Progress -&gt; Partially Completed -&gt; Completed,
/// plus Rejected and Cancelled. Every transition is its own command behind its own
/// permission, and the audit interceptor records who did what and when.
/// </summary>
public record SubmitFormationRequestCommand(Guid Id) : IRequest<FormationRequestDto>;

public class SubmitFormationRequestCommandHandler : IRequestHandler<SubmitFormationRequestCommand, FormationRequestDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    public SubmitFormationRequestCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    { _db = db; _currentUser = currentUser; }

    public Task<FormationRequestDto> Handle(SubmitFormationRequestCommand request, CancellationToken cancellationToken)
        => FormationRequestWorkflow.MutateAsync(_db, request.Id, _currentUser.UserName,
            entity => entity.Submit(_currentUser.UserName), cancellationToken);
}

public record ApproveFormationRequestCommand(Guid Id) : IRequest<FormationRequestDto>;

public class ApproveFormationRequestCommandHandler : IRequestHandler<ApproveFormationRequestCommand, FormationRequestDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    public ApproveFormationRequestCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    { _db = db; _currentUser = currentUser; }

    public Task<FormationRequestDto> Handle(ApproveFormationRequestCommand request, CancellationToken cancellationToken)
        // Approving freezes the specification snapshot on every group (spec section 29).
        => FormationRequestWorkflow.MutateAsync(_db, request.Id, _currentUser.UserName,
            entity => entity.Approve(_currentUser.UserName), cancellationToken);
}

public record RejectFormationRequestCommand(Guid Id, string Reason) : IRequest<FormationRequestDto>;

public class RejectFormationRequestCommandValidator : AbstractValidator<RejectFormationRequestCommand>
{
    public RejectFormationRequestCommandValidator() => RuleFor(x => x.Reason).NotEmpty().MaximumLength(1000);
}

public class RejectFormationRequestCommandHandler : IRequestHandler<RejectFormationRequestCommand, FormationRequestDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    public RejectFormationRequestCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    { _db = db; _currentUser = currentUser; }

    public Task<FormationRequestDto> Handle(RejectFormationRequestCommand request, CancellationToken cancellationToken)
        => FormationRequestWorkflow.MutateAsync(_db, request.Id, _currentUser.UserName,
            entity => entity.Reject(_currentUser.UserName, request.Reason), cancellationToken);
}

public record CancelFormationRequestCommand(Guid Id, string Reason) : IRequest<FormationRequestDto>;

public class CancelFormationRequestCommandValidator : AbstractValidator<CancelFormationRequestCommand>
{
    public CancelFormationRequestCommandValidator() => RuleFor(x => x.Reason).NotEmpty().MaximumLength(1000);
}

public class CancelFormationRequestCommandHandler : IRequestHandler<CancelFormationRequestCommand, FormationRequestDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    public CancelFormationRequestCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    { _db = db; _currentUser = currentUser; }

    public Task<FormationRequestDto> Handle(CancelFormationRequestCommand request, CancellationToken cancellationToken)
        => FormationRequestWorkflow.MutateAsync(_db, request.Id, _currentUser.UserName,
            entity => entity.Cancel(_currentUser.UserName, request.Reason), cancellationToken);
}

public record DeleteFormationRequestCommand(Guid Id) : IRequest<Unit>;

public class DeleteFormationRequestCommandValidator : AbstractValidator<DeleteFormationRequestCommand>
{
    public DeleteFormationRequestCommandValidator() => RuleFor(x => x.Id).NotEmpty();
}

/// <summary>
/// Deletes a planning-only Formation Request (spec sections 6 and 7). Allowed only while the request is still a
/// draft or was rejected, and only when no Job Order was ever created from it - an approved, in-progress or
/// completed request is history and must be CANCELLED instead, never deleted. The request's groups and basins are
/// removed with it (the aggregate is configured cascade-delete). Nothing outside the request is touched, because a
/// Formation Request never moved stock or created a production movement.
/// </summary>
public class DeleteFormationRequestCommandHandler : IRequestHandler<DeleteFormationRequestCommand, Unit>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    public DeleteFormationRequestCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    { _db = db; _currentUser = currentUser; }

    public async Task<Unit> Handle(DeleteFormationRequestCommand request, CancellationToken cancellationToken)
    {
        var entity = await _db.FormationRequests.Include(r => r.Groups).ThenInclude(g => g.Basins)
            .FirstOrDefaultAsync(r => r.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("Formation Request", request.Id);

        if (!entity.IsEditable)
            throw new DomainException(
                $"Formation request {entity.RequestNumber} is {entity.Status} and cannot be deleted. Only a draft or rejected request can be deleted; cancel it instead.");

        _db.FormationRequests.Remove(entity);
        await _db.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}

/// <summary>
/// Records a basin's ACTUAL weighed quantity without converting it (spec sections 8 and 10).
/// Weighing is what the Job Order quantity will be taken from, so it happens while the request
/// is approved and before the basin is converted - never after production has started.
/// </summary>
public record RecordFormationBasinActualQuantityCommand(
    Guid Id, Guid GroupId, Guid BasinId, decimal ActualQuantity) : IRequest<FormationRequestDto>;

public class RecordFormationBasinActualQuantityCommandValidator
    : AbstractValidator<RecordFormationBasinActualQuantityCommand>
{
    public RecordFormationBasinActualQuantityCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.GroupId).NotEmpty();
        RuleFor(x => x.BasinId).NotEmpty();
        RuleFor(x => x.ActualQuantity).GreaterThan(0)
            .WithMessage("A basin's actual weighed quantity must be greater than zero.");
    }
}

public class RecordFormationBasinActualQuantityCommandHandler
    : IRequestHandler<RecordFormationBasinActualQuantityCommand, FormationRequestDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public RecordFormationBasinActualQuantityCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    { _db = db; _currentUser = currentUser; }

    public Task<FormationRequestDto> Handle(
        RecordFormationBasinActualQuantityCommand request, CancellationToken cancellationToken)
        => FormationRequestWorkflow.MutateAsync(_db, request.Id, _currentUser.UserName,
            entity => entity.RecordBasinActualQuantity(
                request.GroupId, request.BasinId, request.ActualQuantity, _currentUser.UserName),
            cancellationToken);
}

/// <summary>
/// Moves an approved request into production (spec section 32): Approved -&gt; In Progress.
/// A Formation Request is its own document, so this is where its own production starts - it never
/// creates, modifies or cancels a Job Order, and it writes nothing to any stock ledger.
/// </summary>
public record StartFormationProductionCommand(Guid Id) : IRequest<FormationRequestDto>;

public class StartFormationProductionCommandValidator : AbstractValidator<StartFormationProductionCommand>
{
    public StartFormationProductionCommandValidator() => RuleFor(x => x.Id).NotEmpty();
}

public class StartFormationProductionCommandHandler
    : IRequestHandler<StartFormationProductionCommand, FormationRequestDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    public StartFormationProductionCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    { _db = db; _currentUser = currentUser; }

    public Task<FormationRequestDto> Handle(StartFormationProductionCommand request, CancellationToken cancellationToken)
        => FormationRequestWorkflow.MutateAsync(_db, request.Id, _currentUser.UserName,
            entity => entity.StartProduction(_currentUser.UserName), cancellationToken);
}

/// <summary>
/// Records produced quantity against ONE group of a request that has no basins (spec sections 32-33).
/// Quantities accumulate - the same physical quantity can never be counted twice - and the request's own
/// completion status is re-derived from what its groups hold. No stock ledger is touched: a Formation
/// Request is an independent document, so recording its output never posts inventory.
/// </summary>
public record RecordFormationGroupProductionCommand(Guid Id, Guid GroupId, decimal Quantity)
    : IRequest<FormationRequestDto>;

public class RecordFormationGroupProductionCommandValidator
    : AbstractValidator<RecordFormationGroupProductionCommand>
{
    public RecordFormationGroupProductionCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.GroupId).NotEmpty();
        RuleFor(x => x.Quantity).GreaterThan(0)
            .WithMessage("A produced quantity must be greater than zero.");
    }
}

public class RecordFormationGroupProductionCommandHandler
    : IRequestHandler<RecordFormationGroupProductionCommand, FormationRequestDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    public RecordFormationGroupProductionCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    { _db = db; _currentUser = currentUser; }

    public Task<FormationRequestDto> Handle(
        RecordFormationGroupProductionCommand request, CancellationToken cancellationToken)
        => FormationRequestWorkflow.MutateAsync(_db, request.Id, _currentUser.UserName,
            entity => entity.RecordGroupProduction(request.GroupId, request.Quantity, _currentUser.UserName),
            cancellationToken);
}

/// <summary>
/// Records produced quantity against ONE basin of a group that is measured per basin (spec sections 10-11
/// and 32). The group's produced figure is re-derived from its basins immediately afterwards, so a group
/// can never report more than its basins hold. Nothing is posted to stock and no Job Order is touched.
/// </summary>
public record RecordFormationBasinProductionCommand(Guid Id, Guid GroupId, Guid BasinId, decimal Quantity)
    : IRequest<FormationRequestDto>;

public class RecordFormationBasinProductionCommandValidator
    : AbstractValidator<RecordFormationBasinProductionCommand>
{
    public RecordFormationBasinProductionCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.GroupId).NotEmpty();
        RuleFor(x => x.BasinId).NotEmpty();
        RuleFor(x => x.Quantity).GreaterThan(0)
            .WithMessage("A produced quantity must be greater than zero.");
    }
}

public class RecordFormationBasinProductionCommandHandler
    : IRequestHandler<RecordFormationBasinProductionCommand, FormationRequestDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    public RecordFormationBasinProductionCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    { _db = db; _currentUser = currentUser; }

    public Task<FormationRequestDto> Handle(
        RecordFormationBasinProductionCommand request, CancellationToken cancellationToken)
        => FormationRequestWorkflow.MutateAsync(_db, request.Id, _currentUser.UserName,
            entity => entity.RecordBasinProduction(
                request.GroupId, request.BasinId, request.Quantity, _currentUser.UserName),
            cancellationToken);
}

/// <summary>
/// Creates a REVISION of a formation request that can no longer be edited directly because it has been
/// submitted or approved (spec sections 29-33). The original is never reopened, never rewritten and never
/// silently re-approved: the revision is a brand-new DRAFT carrying a copy of the current groups, basins and
/// specification snapshots, linked back to the original through RevisionOfFormationRequestId. It must be
/// submitted and approved on its own, so both documents' approval history stays intact.
///
/// Only a draft or rejected request is refused (it is edited in place instead), and a cancelled request is
/// refused (it is dead history). Gated by the configurable formation.edit permission.
/// </summary>
public record ReviseFormationRequestCommand(Guid Id) : IRequest<FormationRequestDto>;

public class ReviseFormationRequestCommandValidator : AbstractValidator<ReviseFormationRequestCommand>
{
    public ReviseFormationRequestCommandValidator() => RuleFor(x => x.Id).NotEmpty();
}

public class ReviseFormationRequestCommandHandler : IRequestHandler<ReviseFormationRequestCommand, FormationRequestDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IDocumentNumberGenerator _numberGenerator;

    public ReviseFormationRequestCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser,
        IDocumentNumberGenerator numberGenerator)
    { _db = db; _currentUser = currentUser; _numberGenerator = numberGenerator; }

    public async Task<FormationRequestDto> Handle(ReviseFormationRequestCommand request, CancellationToken cancellationToken)
    {
        var original = await _db.FormationRequests.Include(r => r.Groups).ThenInclude(g => g.Basins)
            .FirstOrDefaultAsync(r => r.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("Formation Request", request.Id);

        if (original.Status is FormationRequestStatus.Draft or FormationRequestStatus.Rejected)
            throw new DomainException(
                $"Formation request {original.RequestNumber} is {original.Status} and can be edited in place - a revision is only needed once it has been submitted or approved.");
        if (original.Status == FormationRequestStatus.Cancelled)
            throw new DomainException(
                $"Formation request {original.RequestNumber} is cancelled and cannot be revised.");

        var user = _currentUser.UserName;
        var number = await _numberGenerator.NextAsync(DocumentType.FormationRequest, cancellationToken: cancellationToken);

        var revision = new FormationRequest(
            number, original.RequestDate, original.CustomerId, original.ItemId, original.Unit,
            user, original.RawMessageId, original.TotalQuantity, original.Notes);

        revision.MarkAsRevision(original.Id, user);
        _db.FormationRequests.Add(revision);

        CopyGroupsAndBasins(original, revision, user);

        revision.UpdateHeader(original.RequestDate, original.RawMessageId, original.Notes,
            revision.Groups.Sum(g => g.PlannedQuantity), user);

        await _db.SaveChangesAsync(cancellationToken);
        return await FormationRequestDtoBuilder.BuildAsync(_db, revision, cancellationToken);
    }

    /// <summary>
    /// Deep-copies the groups, basins and their specification snapshots onto the revision. Produced quantities are
    /// deliberately NOT copied: production belongs to the original document's history, and the revision starts as a
    /// fresh plan. Each new group/basin is registered explicitly for INSERT, exactly like the ordinary create path.
    /// </summary>
    private void CopyGroupsAndBasins(FormationRequest original, FormationRequest revision, string user)
    {
        foreach (var group in original.Groups.OrderBy(g => g.GroupNumber))
        {
            var newGroup = revision.AddGroup(
                group.Name, group.PlannedQuantity, group.Unit, group.TubCount, group.Color, group.Notes, user);
            _db.FormationGroups.Add(newGroup);

            foreach (var basin in group.Basins.OrderBy(b => b.BasinNumber))
            {
                var newBasin = revision.AddBasin(
                    newGroup.Id, basin.Name, basin.PlannedQuantity, basin.Unit, basin.TubCount, basin.Color, basin.Notes, user);
                _db.FormationBasins.Add(newBasin);

                // Copy the basin's own snapshot (which may differ from its group's).
                revision.UpdateBasin(
                    newGroup.Id, newBasin.Id, basin.Name, basin.PlannedQuantity, basin.Unit, basin.TubCount, basin.Color,
                    basin.WidthCm, basin.MetersPerKg, basin.Gsm, basin.TubFormat, basin.WindingTapeFormat,
                    basin.QualityInstructions, basin.LabInstructions, basin.InternalInstructions,
                    basin.CustomerInstructions, basin.Notes, user);
            }

            // Copy the group's own cell specification last (a group with basins keeps the basin-summed quantity).
            revision.UpdateGroup(
                newGroup.Id, group.Name, group.PlannedQuantity, group.Unit, group.TubCount, group.Color,
                group.WidthCm, group.MetersPerKg, group.Gsm, group.TubFormat, group.WindingTapeFormat,
                group.QualityInstructions, group.LabInstructions, group.InternalInstructions,
                group.CustomerInstructions, group.Notes, user);
        }
    }
}

/// <summary>Loads a request with its groups, applies one domain transition, and returns the fresh DTO.</summary>
internal static class FormationRequestWorkflow
{
    public static async Task<FormationRequestDto> MutateAsync(
        IApplicationDbContext db, Guid id, string user, Action<FormationRequest> mutate, CancellationToken cancellationToken)
    {
        var entity = await db.FormationRequests.Include(r => r.Groups).ThenInclude(g => g.Basins)
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken)
            ?? throw new NotFoundException("Formation Request", id);

        mutate(entity);
        await db.SaveChangesAsync(cancellationToken);

        return await FormationRequestDtoBuilder.BuildAsync(db, entity, cancellationToken);
    }
}
