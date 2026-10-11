using System.ComponentModel.DataAnnotations;
using DyeHouseERP.Domain.Common;
using DyeHouseERP.Domain.Enums;
using DyeHouseERP.Domain.Exceptions;

namespace DyeHouseERP.Domain.Entities;

/// <summary>
/// A request for a SECOND user to authorise an action (spec section 24).
///
/// This is what turns the negative-stock override from "type the approver's
/// name" into a real workflow: the requester submits WHAT they want to do
/// (the module command as JSON) and WHY; a different, authenticated user
/// approves or rejects it, and the approver's identity is taken from their
/// own session - never from anything the client sent.
///
/// The request is single-use: it is consumed by the very command it
/// authorises, in the same SaveChanges as that command's posting, so a
/// decision can never be replayed against a second movement.
/// </summary>
public class ApprovalRequest : AuditableEntity
{
    public ApprovalRequestType Type { get; private set; }

    /// <summary>
    /// The subject the request is about: the Job Order for allocation /
    /// resume, the raw message for the three stock-movement paths. It is
    /// re-checked when the request is consumed, so an approved request can
    /// never be replayed against a different document.
    /// </summary>
    public Guid DocumentId { get; private set; }

    /// <summary>Human-readable subject for the approval queue (order/message number), resolved server-side at submit time.</summary>
    public string DocumentNumber { get; private set; } = string.Empty;

    /// <summary>Why the override is needed - mandatory, shown to the approver, stored on the decision.</summary>
    public string Reason { get; private set; } = string.Empty;

    /// <summary>
    /// The module command to execute on approval, serialized WITHOUT any
    /// approver identity: the approver is whoever approves this request.
    /// </summary>
    public string PayloadJson { get; private set; } = string.Empty;

    public string RequestedBy { get; private set; } = string.Empty;
    public DateTime RequestedAtUtc { get; private set; }

    public ApprovalRequestStatus Status { get; private set; } = ApprovalRequestStatus.Pending;

    /// <summary>The canonical username of the user who decided - derived from their authenticated session.</summary>
    public string? DecidedBy { get; private set; }
    public DateTime? DecidedAtUtc { get; private set; }

    /// <summary>Why it was rejected (mandatory on rejection, empty on approval).</summary>
    public string? DecisionReason { get; private set; }

    /// <summary>
    /// R3: optimistic concurrency token, maintained by SQL Server. Two approvers
    /// racing on the same request must not both win - the second update fails on
    /// the token instead of double-executing the movement.
    /// </summary>
    [Timestamp]
    public byte[] RowVersion { get; private set; } = Array.Empty<byte>();

    private ApprovalRequest() { } // EF Core

    public ApprovalRequest(
        ApprovalRequestType type, Guid documentId, string documentNumber,
        string reason, string payloadJson, string requestedBy, DateTime requestedAtUtc)
    {
        if (documentId == Guid.Empty)
            throw new DomainException("An approval request must name the document it applies to.");
        if (string.IsNullOrWhiteSpace(reason))
            throw new DomainException("An approval request must explain why the action is needed.");
        if (string.IsNullOrWhiteSpace(payloadJson))
            throw new DomainException("An approval request must carry the action to approve.");

        Type = type;
        DocumentId = documentId;
        DocumentNumber = documentNumber.Trim();
        Reason = reason.Trim();
        PayloadJson = payloadJson;
        RequestedBy = requestedBy;
        RequestedAtUtc = requestedAtUtc;
    }

    /// <summary>
    /// Records the approval. Only a PENDING request can be decided, so a second
    /// approver racing this one loses on the state check (and on the rowversion
    /// token at the database) rather than executing the movement twice.
    /// </summary>
    public void Approve(string decidedBy, DateTime decidedAtUtc)
    {
        if (Status != ApprovalRequestStatus.Pending)
            throw new DomainException(
                $"This approval request is already {Status} and cannot be decided again.");

        Status = ApprovalRequestStatus.Approved;
        DecidedBy = decidedBy;
        DecidedAtUtc = decidedAtUtc;
        DecisionReason = null;
        ModifiedBy = decidedBy;
        ModifiedAtUtc = decidedAtUtc;
    }

    /// <summary>Records the rejection. A reason is mandatory - a refusal must be explainable to the requester.</summary>
    public void Reject(string decidedBy, string reason, DateTime decidedAtUtc)
    {
        if (Status != ApprovalRequestStatus.Pending)
            throw new DomainException(
                $"This approval request is already {Status} and cannot be decided again.");
        if (string.IsNullOrWhiteSpace(reason))
            throw new DomainException("Rejecting an approval request requires a reason.");

        Status = ApprovalRequestStatus.Rejected;
        DecidedBy = decidedBy;
        DecidedAtUtc = decidedAtUtc;
        DecisionReason = reason.Trim();
        ModifiedBy = decidedBy;
        ModifiedAtUtc = decidedAtUtc;
    }
}
