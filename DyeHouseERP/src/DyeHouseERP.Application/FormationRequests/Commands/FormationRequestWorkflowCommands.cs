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

/// <summary>
/// Turns an approved request (or one specific group/cell of it) into a Job Order (spec sections 16 and 31).
/// The Job Order carries the request Id and, when a single group is converted, the group Id too - so the chain
/// Customer -&gt; Message -&gt; Formation Request -&gt; Job Order -&gt; Production -&gt; Ready Goods -&gt; Delivery is navigable
/// in both directions. Groups can be converted one by one, which is how a mixed request (different colours /
/// specifications per group) is actually processed.
/// </summary>
public record ConvertFormationRequestToJobOrderCommand(
    Guid Id, DateTime OrderDate, JobOrderType JobOrderType, ProductionPriority Priority = ProductionPriority.Normal,
    Guid? GroupId = null, string? Color = null, string? Notes = null, string? CustomerReference = null)
    : IRequest<FormationRequestDto>;

public class ConvertFormationRequestToJobOrderCommandValidator : AbstractValidator<ConvertFormationRequestToJobOrderCommand>
{
    public ConvertFormationRequestToJobOrderCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.JobOrderType).IsInEnum();
        RuleFor(x => x.Priority).IsInEnum();
    }
}

public class ConvertFormationRequestToJobOrderCommandHandler
    : IRequestHandler<ConvertFormationRequestToJobOrderCommand, FormationRequestDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IDocumentNumberGenerator _numberGenerator;

    public ConvertFormationRequestToJobOrderCommandHandler(
        IApplicationDbContext db, ICurrentUserService currentUser, IDocumentNumberGenerator numberGenerator)
    { _db = db; _currentUser = currentUser; _numberGenerator = numberGenerator; }

    public async Task<FormationRequestDto> Handle(ConvertFormationRequestToJobOrderCommand request, CancellationToken cancellationToken)
    {
        var entity = await _db.FormationRequests.Include(r => r.Groups)
            .FirstOrDefaultAsync(r => r.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("Formation Request", request.Id);

        if (entity.Status is FormationRequestStatus.Draft or FormationRequestStatus.Submitted or FormationRequestStatus.Rejected)
            throw new DomainException("Only an approved formation request can be converted into a Job Order.");
        if (entity.Status == FormationRequestStatus.Cancelled)
            throw new DomainException("A cancelled formation request cannot be converted into a Job Order.");

        FormationGroup? group = null;
        if (request.GroupId.HasValue)
        {
            group = entity.Groups.FirstOrDefault(g => g.Id == request.GroupId.Value)
                ?? throw new DomainException($"Formation group ({request.GroupId}) does not belong to request {entity.RequestNumber}.");

            var alreadyLinked = await _db.ProductionOrders.AsNoTracking()
                .AnyAsync(o => o.FormationGroupId == group.Id && o.Status != ProductionOrderStatus.Cancelled, cancellationToken);
            if (alreadyLinked)
                throw new DomainException(
                    $"Group {group.GroupNumber} already has a Job Order. Each group is converted once - the same physical quantity must never be planned twice.");
        }

        var unit = group?.Unit ?? entity.Unit;
        var quantity = group?.PlannedQuantity ?? entity.Groups.Sum(g => g.PlannedQuantity);
        if (quantity <= 0)
            throw new DomainException("The quantity being converted must be greater than zero.");

        var color = request.Color ?? group?.Color;
        var orderNumber = await _numberGenerator.NextAsync(DocumentType.ProductionOrder, cancellationToken: cancellationToken);

        var order = new ProductionOrder(
            orderNumber, entity.CustomerId, entity.ItemId, request.OrderDate, _currentUser.UserName,
            color: color,
            requestedQuantityKg: unit == UnitOfMeasure.KG ? quantity : null,
            requestedQuantityMeter: unit == UnitOfMeasure.Meter ? quantity : null,
            customerReference: request.CustomerReference,
            notes: request.Notes,
            priority: request.Priority,
            jobOrderType: request.JobOrderType,
            formationRequestId: entity.Id,
            formationGroupId: group?.Id);

        var activeStages = await _db.ProductionStageDefinitions.AsNoTracking()
            .Where(s => s.IsActive).OrderBy(s => s.Sequence).ToListAsync(cancellationToken);
        order.BuildStageRoute(activeStages);

        _db.ProductionOrders.Add(order);
        entity.LinkProductionOrder(order.Id, _currentUser.UserName);

        await _db.SaveChangesAsync(cancellationToken);
        return await FormationRequestDtoBuilder.BuildAsync(_db, entity, cancellationToken);
    }
}

/// <summary>Loads a request with its groups, applies one domain transition, and returns the fresh DTO.</summary>
internal static class FormationRequestWorkflow
{
    public static async Task<FormationRequestDto> MutateAsync(
        IApplicationDbContext db, Guid id, string user, Action<FormationRequest> mutate, CancellationToken cancellationToken)
    {
        var entity = await db.FormationRequests.Include(r => r.Groups)
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken)
            ?? throw new NotFoundException("Formation Request", id);

        mutate(entity);
        await db.SaveChangesAsync(cancellationToken);

        return await FormationRequestDtoBuilder.BuildAsync(db, entity, cancellationToken);
    }
}
