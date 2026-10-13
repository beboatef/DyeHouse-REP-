using System.Text.Json;
using System.Text.Json.Serialization;
using DyeHouseERP.Application.Approvals.DTOs;
using DyeHouseERP.Application.Common.Exceptions;
using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.Common.Services;
using DyeHouseERP.Application.CustomerTransfers.Commands;
using DyeHouseERP.Application.ProductionOrders.Commands;
using DyeHouseERP.Application.RawExternalReleases.Commands;
using DyeHouseERP.Application.StockAdjustments.Commands;
using DyeHouseERP.Domain.Common;
using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Enums;
using DyeHouseERP.Domain.Exceptions;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.Approvals.Commands;

/// <summary>
/// Serialization for approval payloads: the shape the UI posts (camelCase,
/// enums as strings), read case-insensitively so a hand-built payload still
/// binds. The payload NEVER carries an approver - the approver is whoever
/// approves the request from their own session.
/// </summary>
public static class ApprovalPayload
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        PropertyNameCaseInsensitive = true
    };

    public static T Deserialize<T>(string json) =>
        JsonSerializer.Deserialize<T>(json, Options)
        ?? throw new DomainException("The action stored on this approval request could not be read.");

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);
}

/// <summary>
/// The fixed facts about each separation-of-duties request type: the permission
/// a requester must hold to ask for it, and where its subject lives in the UI.
/// The approval side always requires inventory.approve_negative_stock (or
/// admin), which the shared guard enforces.
/// </summary>
public static class ApprovalRequestKinds
{
    public static string ActionPermission(ApprovalRequestType type) => type switch
    {
        ApprovalRequestType.AllocateRaw => Permissions.RawConsume,
        ApprovalRequestType.ResumeProduction => Permissions.ProductionResume,
        ApprovalRequestType.CustomerTransfer => Permissions.RawTransfer,
        ApprovalRequestType.RawExternalRelease => Permissions.RawExternalRelease,
        ApprovalRequestType.StockAdjustment => Permissions.InventoryAdjust,
        ApprovalRequestType.RawIssue => Permissions.RawConsume,
        _ => throw new DomainException($"Unknown approval request type '{type}'.")
    };

    public static string LinkPath(ApprovalRequestType type, Guid documentId) => type switch
    {
        ApprovalRequestType.AllocateRaw => $"/production-orders/{documentId}",
        ApprovalRequestType.ResumeProduction => $"/production-orders/{documentId}",
        ApprovalRequestType.CustomerTransfer => "/customer-transfers",
        ApprovalRequestType.RawExternalRelease => "/external-releases",
        ApprovalRequestType.StockAdjustment => "/stock-adjustments",
        ApprovalRequestType.RawIssue => "/production-orders",
        _ => "/approvals"
    };

    public static ApprovalRequestDto ToDto(ApprovalRequest request) => new()
    {
        Id = request.Id,
        Type = request.Type.ToString(),
        DocumentId = request.DocumentId,
        DocumentNumber = request.DocumentNumber,
        Reason = request.Reason,
        RequestedBy = request.RequestedBy,
        RequestedAtUtc = request.RequestedAtUtc,
        Status = request.Status.ToString(),
        DecidedBy = request.DecidedBy,
        DecidedAtUtc = request.DecidedAtUtc,
        DecisionReason = request.DecisionReason,
        RequiredPermission = Permissions.InventoryApproveNegativeStock,
        LinkPath = LinkPath(request.Type, request.DocumentId)
    };
}

/// <summary>
/// Raises a separation-of-duties request (spec section 24): "I need to do THIS
/// action, here is WHY". The payload is the module's own command, normalized
/// server-side - always an override, always carrying this request's reason, and
/// stripped of any approver field - then validated with the module's own
/// validator, so a request can never ask for something the module would refuse.
/// </summary>
public record SubmitApprovalRequestCommand(
    ApprovalRequestType Type,
    string Reason,
    string PayloadJson) : IRequest<ApprovalRequestDto>;

public class SubmitApprovalRequestCommandValidator : AbstractValidator<SubmitApprovalRequestCommand>
{
    public SubmitApprovalRequestCommandValidator()
    {
        RuleFor(x => x.Type).IsInEnum();
        RuleFor(x => x.Reason).NotEmpty()
            .WithMessage("Explain why this exception is needed - the approver sees exactly this reason.")
            .MaximumLength(1000);
        RuleFor(x => x.PayloadJson).NotEmpty();
    }
}

public class SubmitApprovalRequestCommandHandler : IRequestHandler<SubmitApprovalRequestCommand, ApprovalRequestDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IDateTime _clock;

    public SubmitApprovalRequestCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser, IDateTime clock)
    {
        _db = db; _currentUser = currentUser; _clock = clock;
    }

    public async Task<ApprovalRequestDto> Handle(SubmitApprovalRequestCommand request, CancellationToken cancellationToken)
    {
        // Server-side authorization: the requester must be able to perform the
        // action they are asking someone else to authorise.
        var actionPermission = ApprovalRequestKinds.ActionPermission(request.Type);
        if (!_currentUser.HasPermission(actionPermission))
            throw new UnauthorizedAccessException(
                $"You do not hold the '{actionPermission}' permission required to request this action.");

        var reason = request.Reason.Trim();

        // Build the normalized payload for this type: the subject id comes from
        // the payload itself (never from a separate, spoofable field).
        object normalized;
        Guid documentId;
        string documentNumber;

        switch (request.Type)
        {
            case ApprovalRequestType.AllocateRaw:
            {
                var cmd = ApprovalPayload.Deserialize<AllocateRawCommand>(request.PayloadJson) with
                {
                    OverrideNegativeStock = true,
                    OverrideReason = reason,
                    // Placeholder for validation: an override must reference an
                    // approval request. The real id is attached only when this
                    // request is approved.
                    ApprovalRequestId = Guid.NewGuid()
                };
                documentId = cmd.ProductionOrderId;
                if (!new AllocateRawCommandValidator().Validate(cmd).IsValid)
                    throw new DomainException("The allocation stored on this request is not valid.");
                var order = await _db.ProductionOrders.AsNoTracking()
                    .FirstOrDefaultAsync(o => o.Id == documentId, cancellationToken)
                    ?? throw new NotFoundException("ProductionOrder", documentId);
                documentNumber = order.OrderNumber;
                normalized = cmd with { ApprovalRequestId = null };
                break;
            }
            case ApprovalRequestType.ResumeProduction:
            {
                var cmd = ApprovalPayload.Deserialize<ResumeProductionOrderCommand>(request.PayloadJson) with
                {
                    OverrideNegativeStock = true,
                    OverrideReason = reason,
                    ApprovalRequestId = Guid.NewGuid()
                };
                documentId = cmd.ProductionOrderId;
                if (!new ResumeProductionOrderCommandValidator().Validate(cmd).IsValid)
                    throw new DomainException("The resume stored on this request is not valid.");
                var order = await _db.ProductionOrders.AsNoTracking()
                    .FirstOrDefaultAsync(o => o.Id == documentId, cancellationToken)
                    ?? throw new NotFoundException("ProductionOrder", documentId);
                documentNumber = order.OrderNumber;
                normalized = cmd with { ApprovalRequestId = null };
                break;
            }
            case ApprovalRequestType.CustomerTransfer:
            {
                var cmd = ApprovalPayload.Deserialize<CreateCustomerTransferCommand>(request.PayloadJson) with
                {
                    OverrideNegativeStock = true,
                    OverrideReason = reason,
                    ApprovalRequestId = Guid.NewGuid()
                };
                documentId = cmd.RawMessageId;
                if (!new CreateCustomerTransferCommandValidator().Validate(cmd).IsValid)
                    throw new DomainException("The transfer stored on this request is not valid.");
                documentNumber = await MessageNumberAsync(documentId, cancellationToken);
                normalized = cmd with { ApprovalRequestId = null };
                break;
            }
            case ApprovalRequestType.RawExternalRelease:
            {
                var cmd = ApprovalPayload.Deserialize<CreateRawExternalReleaseCommand>(request.PayloadJson) with
                {
                    OverrideNegativeStock = true,
                    OverrideReason = reason,
                    ApprovalRequestId = Guid.NewGuid()
                };
                documentId = cmd.RawMessageId;
                if (!new CreateRawExternalReleaseCommandValidator().Validate(cmd).IsValid)
                    throw new DomainException("The movement stored on this request is not valid.");
                documentNumber = await MessageNumberAsync(documentId, cancellationToken);
                normalized = cmd with { ApprovalRequestId = null };
                break;
            }
            case ApprovalRequestType.StockAdjustment:
            {
                var cmd = ApprovalPayload.Deserialize<CreateStockAdjustmentCommand>(request.PayloadJson) with
                {
                    OverrideNegativeStock = true,
                    OverrideReason = reason,
                    ApprovalRequestId = Guid.NewGuid()
                };
                documentId = cmd.RawMessageId;
                if (!new CreateStockAdjustmentCommandValidator().Validate(cmd).IsValid)
                    throw new DomainException("The adjustment stored on this request is not valid.");
                documentNumber = await MessageNumberAsync(documentId, cancellationToken);
                normalized = cmd with { ApprovalRequestId = null };
                break;
            }
            case ApprovalRequestType.RawIssue:
            {
                // The canonical issue has no order yet, so a negative-stock issue is bound
                // to the customer's raw lot it draws from.
                var cmd = ApprovalPayload.Deserialize<IssueRawMaterialCommand>(request.PayloadJson)
                    .AsApprovalOverride(reason, Guid.NewGuid());
                documentId = cmd.RawMessageId;
                if (!new IssueRawMaterialCommandValidator().Validate(cmd).IsValid)
                    throw new DomainException("The raw issue stored on this request is not valid.");
                documentNumber = await MessageNumberAsync(documentId, cancellationToken);
                normalized = cmd with { ApprovalRequestId = null };
                break;
            }
            default:
                throw new DomainException($"Unknown approval request type '{request.Type}'.");
        }

        // One open request per subject and type: a second submission would be
        // ambiguous for the approver and is refused with a clear message instead
        // of silently doubling the queue.
        var alreadyPending = await _db.ApprovalRequests.AsNoTracking().AnyAsync(
            a => a.Type == request.Type && a.DocumentId == documentId &&
                 a.Status == ApprovalRequestStatus.Pending,
            cancellationToken);
        if (alreadyPending)
            throw new DomainException(
                $"A pending {request.Type} approval request already exists for '{documentNumber}'.");

        // The stored action is BOUND to the exact facts that were approved: the
        // signature travels with the payload, so consuming the request can
        // verify item, account, quantity and unit, not just the document id.
        var operationSignature = ((IApprovalBoundOperation)normalized).OperationSignature;

        var approval = new ApprovalRequest(
            request.Type, documentId, documentNumber, reason,
            ApprovalBinding.EmbedSignature(ApprovalPayload.Serialize(normalized), operationSignature),
            _currentUser.UserName, _clock.UtcNow);

        _db.ApprovalRequests.Add(approval);
        await _db.SaveChangesAsync(cancellationToken);

        return ApprovalRequestKinds.ToDto(approval);
    }

    private async Task<string> MessageNumberAsync(Guid rawMessageId, CancellationToken cancellationToken)
        => await _db.RawMessages.AsNoTracking()
               .Where(m => m.Id == rawMessageId)
               .Select(m => m.MessageNumber)
               .FirstOrDefaultAsync(cancellationToken)
           ?? throw new NotFoundException("RawMessage", rawMessageId);
}

/// <summary>
/// Approves a pending request. The approver's identity comes from the
/// authenticated caller; the stored payload is replayed into the module's own
/// command, so every module rule, lock and audit trail still runs exactly once,
/// and the shared guard inside that command enforces requester ≠ approver
/// before anything is posted. The approval is consumed in the SAME SaveChanges
/// as the movement itself.
/// </summary>
public record ApproveApprovalRequestCommand(Guid Id) : IRequest<ApprovalRequestDto>;

public class ApproveApprovalRequestCommandHandler : IRequestHandler<ApproveApprovalRequestCommand, ApprovalRequestDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ISender _sender;

    public ApproveApprovalRequestCommandHandler(IApplicationDbContext db, ISender sender)
    {
        _db = db; _sender = sender;
    }

    public async Task<ApprovalRequestDto> Handle(ApproveApprovalRequestCommand request, CancellationToken cancellationToken)
    {
        var approval = await _db.ApprovalRequests
                .FirstOrDefaultAsync(a => a.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("ApprovalRequest", request.Id);

        // Load-tracking matters: the dispatched command consumes THIS row through
        // the same change tracker, so the decision commits with the posting.
        switch (approval.Type)
        {
            case ApprovalRequestType.AllocateRaw:
                await _sender.Send(ApprovalPayload.Deserialize<AllocateRawCommand>(approval.PayloadJson)
                    with { ApprovalRequestId = approval.Id }, cancellationToken);
                break;
            case ApprovalRequestType.ResumeProduction:
                await _sender.Send(ApprovalPayload.Deserialize<ResumeProductionOrderCommand>(approval.PayloadJson)
                    with { ApprovalRequestId = approval.Id }, cancellationToken);
                break;
            case ApprovalRequestType.CustomerTransfer:
                await _sender.Send(ApprovalPayload.Deserialize<CreateCustomerTransferCommand>(approval.PayloadJson)
                    with { ApprovalRequestId = approval.Id }, cancellationToken);
                break;
            case ApprovalRequestType.RawExternalRelease:
                await _sender.Send(ApprovalPayload.Deserialize<CreateRawExternalReleaseCommand>(approval.PayloadJson)
                    with { ApprovalRequestId = approval.Id }, cancellationToken);
                break;
            case ApprovalRequestType.StockAdjustment:
                await _sender.Send(ApprovalPayload.Deserialize<CreateStockAdjustmentCommand>(approval.PayloadJson)
                    with { ApprovalRequestId = approval.Id }, cancellationToken);
                break;
            case ApprovalRequestType.RawIssue:
                await _sender.Send(ApprovalPayload.Deserialize<IssueRawMaterialCommand>(approval.PayloadJson)
                    with { ApprovalRequestId = approval.Id }, cancellationToken);
                break;
            default:
                throw new DomainException($"Unknown approval request type '{approval.Type}'.");
        }

        // The dispatched command persisted the consumed (Approved) request; if it
        // threw, nothing was saved and this request is still Pending.
        return ApprovalRequestKinds.ToDto(approval);
    }
}

/// <summary>Rejects a pending request. A reason is mandatory - a refusal has to be explainable to the requester.</summary>
public record RejectApprovalRequestCommand(Guid Id, string Reason) : IRequest<ApprovalRequestDto>;

public class RejectApprovalRequestCommandValidator : AbstractValidator<RejectApprovalRequestCommand>
{
    public RejectApprovalRequestCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty()
            .WithMessage("Rejecting a request requires a reason.")
            .MaximumLength(1000);
    }
}

public class RejectApprovalRequestCommandHandler : IRequestHandler<RejectApprovalRequestCommand, ApprovalRequestDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IDateTime _clock;

    public RejectApprovalRequestCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser, IDateTime clock)
    {
        _db = db; _currentUser = currentUser; _clock = clock;
    }

    public async Task<ApprovalRequestDto> Handle(RejectApprovalRequestCommand request, CancellationToken cancellationToken)
    {
        var approval = await _db.ApprovalRequests
                .FirstOrDefaultAsync(a => a.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("ApprovalRequest", request.Id);

        // Same decision-side rules as approving: pending only, never your own
        // request, and only an active account holding the approval permission.
        var decider = await ApprovalRequestGuard.EnsureDeciderAsync(
            _db, _currentUser, approval, cancellationToken);

        approval.Reject(decider, request.Reason, _clock.UtcNow);
        await _db.SaveChangesAsync(cancellationToken);

        return ApprovalRequestKinds.ToDto(approval);
    }
}
