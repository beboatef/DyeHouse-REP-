using DyeHouseERP.Application.Common.Exceptions;
using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Domain.Common;
using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Enums;
using DyeHouseERP.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.Common.Services;

/// <summary>The two people behind one authorised exception - both recorded from real accounts.</summary>
public record ApprovalDecision(Guid ApprovalRequestId, string RequestedBy, string ApprovedBy);

/// <summary>
/// Separation of duties for the five paths that may take a balance negative
/// (spec section 24): raw allocation, the pause/resume path's re-issue, customer
/// transfer, external processing release and stock adjustment.
///
/// The rule is enforced HERE, once, for all five:
///
///   1. the movement must carry a stored approval request - there is no other
///      way to override the negative-stock check;
///   2. that request must be PENDING, of the expected type, and about the SAME
///      document the movement targets (so an approval can never be replayed
///      against something else);
///   3. it must also be about the SAME OPERATION: the item, account(s),
///      quantity in each unit and the operation's own discriminator must match
///      the facts that were approved. An approval given for 120 KG from lot A to
///      customer X cannot authorise 400 KG to customer Y - changing any approved
///      detail requires a new request;
///   4. the requester can never approve their own request;
///   5. the approver is the AUTHENTICATED user calling the command - their
///      identity is never taken from the request body - and they must be an
///      active account holding inventory.approve_negative_stock (or admin).
///
/// Consuming marks the request Approved in the SAME SaveChanges as the
/// movement's posting, so the pair commits together or not at all, and the
/// rowversion token makes a racing second approval fail instead of executing
/// the movement twice.
/// </summary>
public static class ApprovalRequestGuard
{
    public static async Task<ApprovalDecision> ConsumeAsync(
        IApplicationDbContext db,
        ICurrentUserService currentUser,
        Guid? approvalRequestId,
        IApprovalBoundOperation operation,
        CancellationToken cancellationToken)
    {
        if (!approvalRequestId.HasValue || approvalRequestId.Value == Guid.Empty)
            throw new UnauthorizedAccessException(
                "This movement would take the balance negative and must be requested and approved by a different " +
                $"user in the Approval Center (separation of duties, '{Permissions.InventoryApproveNegativeStock}'). " +
                "There is no way to approve your own request.");

        var request = await db.ApprovalRequests
            .FirstOrDefaultAsync(a => a.Id == approvalRequestId.Value, cancellationToken)
            ?? throw new NotFoundException("ApprovalRequest", approvalRequestId.Value);

        if (request.Type != operation.ApprovalType)
            throw new DomainException(
                $"Approval request for '{request.DocumentNumber}' was raised for {request.Type}, not for this action.");

        if (request.DocumentId != operation.ApprovalDocumentId)
            throw new DomainException(
                $"Approval request for '{request.DocumentNumber}' applies to a different document and cannot authorise this one.");

        // The operation itself must be the one that was approved. Compared as a
        // canonical signature (item, account, quantities and unit, operation
        // discriminator), so an approval can never be stretched to cover a
        // different quantity, unit, item or account - those need a new request.
        var approved = ApprovalBinding.ReadSignature(request.PayloadJson);
        if (!string.Equals(approved, operation.OperationSignature, StringComparison.Ordinal))
            throw new DomainException(
                $"Approval request for '{request.DocumentNumber}' was given for different operation details " +
                "(item, account, quantity or unit). The approved details cannot be changed here - submit a new " +
                "approval request for the movement you actually want to post.");

        var decider = await EnsureDeciderAsync(db, currentUser, request, cancellationToken);

        request.Approve(decider, DateTime.UtcNow);

        return new ApprovalDecision(request.Id, request.RequestedBy, decider);
    }

    /// <summary>
    /// The decision-side checks shared by approving and rejecting: the request
    /// must still be pending, the decider must not be the requester, and the
    /// decider must be an active account holding the approval permission. It
    /// returns the approver's canonical username so the record stores the real
    /// account name, never a near-miss spelling of it.
    /// </summary>
    public static async Task<string> EnsureDeciderAsync(
        IApplicationDbContext db,
        ICurrentUserService currentUser,
        ApprovalRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Status != ApprovalRequestStatus.Pending)
            throw new DomainException(
                $"Approval request for '{request.DocumentNumber}' is already {request.Status}; only a pending request can be decided.");

        // Rule 3: the requester can never be the approver, checked against the
        // stored requester and the authenticated caller - never against text
        // supplied in a request body.
        if (string.Equals(request.RequestedBy, currentUser.UserName, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException(
                "Separation of duties: the user requesting an exception cannot also approve it. " +
                $"Ask a different user holding '{Permissions.InventoryApproveNegativeStock}' to approve.");

        // Rule 4: resolve the approver from the AUTHENTICATED account, and only
        // an active account with the approval permission counts.
        var approver = await db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Username == currentUser.UserName, cancellationToken)
            ?? throw new UnauthorizedAccessException("The signed-in user does not resolve to a system account.");

        if (!approver.IsActive)
            throw new UnauthorizedAccessException(
                $"User '{approver.Username}' is not an active account and cannot approve an exception.");

        if (!approver.RoleList.Contains(Permissions.InventoryApproveNegativeStock)
            && !approver.RoleList.Contains(Permissions.Admin))
            throw new UnauthorizedAccessException(
                $"User '{approver.Username}' does not hold the '{Permissions.InventoryApproveNegativeStock}' permission " +
                "and cannot approve a negative-stock override.");

        return approver.Username;
    }
}
