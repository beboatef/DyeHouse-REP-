using System;
using System.Collections.Generic;

namespace DyeHouseERP.Application.Approvals.DTOs;

/// <summary>
/// The read-model row for a separation-of-duties request (spec section 24).
/// Used by the approval centre, the approval queue and the approval lifecycle
/// commands so every caller sees the same contract for what was requested,
/// by whom, for what document, and how it was decided.
/// </summary>
public class ApprovalRequestDto
{
    /// <summary>EF Core key.</summary>
    public Guid Id { get; set; }

    /// <summary>What the request is about (spec section 24), serialized as a string for the UI.</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>The subject document (Job Order / RawMessage / CustomerTransfer / etc.).</summary>
    public Guid DocumentId { get; set; }

    /// <summary>Human-readable subject number, resolved server-side at submit time.</summary>
    public string DocumentNumber { get; set; } = string.Empty;

    /// <summary>Why the request was raised.</summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>Canonical username of the requester.</summary>
    public string RequestedBy { get; set; } = string.Empty;

    /// <summary>When the request was created (UTC).</summary>
    public DateTime RequestedAtUtc { get; set; }

    /// <summary>Current lifecycle status, serialized as a string for the UI.</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>Canonical username of the decider, if decided.</summary>
    public string? DecidedBy { get; set; }

    /// <summary>When the decision was recorded (UTC), if decided.</summary>
    public DateTime? DecidedAtUtc { get; set; }

    /// <summary>Why it was rejected, if rejected.</summary>
    public string? DecisionReason { get; set; }

    /// <summary>The permission the decider must hold, for the UI to gate the action.</summary>
    public string RequiredPermission { get; set; } = string.Empty;

    /// <summary>Where the approver goes to act on the subject.</summary>
    public string LinkPath { get; set; } = string.Empty;
}

/// <summary>
/// One row in the centralized Approval Center (spec section 44).
/// It deliberately does NOT duplicate any approval logic - it is only the read model
/// the queue and dashboard present, each row pointing at the module-owned link/permission.
/// </summary>
public class ApprovalItemDto
{
    /// <summary>Category label the UI groups by (for example FormationRequest, PurchaseOrder, PayrollRun, NegativeStockException).</summary>
    public string Category { get; set; } = string.Empty;

    public Guid Id { get; set; }

    public string DocumentNumber { get; set; } = string.Empty;

    public DateTime Date { get; set; }

    /// <summary>Customer / supplier / department the item concerns, if any.</summary>
    public string? Party { get; set; }

    /// <summary>Quantity / amount / scope, for human-readable summary.</summary>
    public string? Summary { get; set; }

    /// <summary>Monetary amount, if the category has one.</summary>
    public decimal? Amount { get; set; }

    public string RequestedBy { get; set; } = string.Empty;

    /// <summary>ISO-8601 string, populated server-side, so the UI can render in any locale.</summary>
    public string? RequestedAtUtc { get; set; }

    /// <summary>Permission the decider must hold, so the UI can hide the action otherwise.</summary>
    public string RequiredPermission { get; set; } = string.Empty;

    /// <summary>Where the approver goes to act, for example "/formation-requests/{id}".</summary>
    public string LinkPath { get; set; } = string.Empty;

    /// <summary>True when this row is a recorded exception rather than an item awaiting a decision.</summary>
    public bool Informational { get; set; }
}

/// <summary>The centralized Approval Center model (spec section 44).</summary>
public class ApprovalCenterDto
{
    /// <summary>All rows the caller is allowed to see, informational ones included.</summary>
    public List<ApprovalItemDto> Items { get; set; } = new();

    /// <summary>Pending (non-informational) counts grouped by category.</summary>
    public Dictionary<string, int> CountsByCategory { get; set; } = new();

    /// <summary>Total pending actions across categories.</summary>
    public int TotalPending { get; set; }
}
