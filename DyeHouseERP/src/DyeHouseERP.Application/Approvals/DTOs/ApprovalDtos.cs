namespace DyeHouseERP.Application.Approvals.DTOs;

/// <summary>One row in the centralized Approval Center (spec section 44).</summary>
public class ApprovalItemDto
{
    /// <summary>FormationRequest | ProductionRequest | PurchaseOrder | PayrollRun | NegativeStockException.</summary>
    public string Category { get; set; } = string.Empty;
    public Guid Id { get; set; }
    public string DocumentNumber { get; set; } = string.Empty;
    public DateTime Date { get; set; }

    public string? Party { get; set; }        // customer / supplier / department the item concerns
    public string? Summary { get; set; }      // quantity, amount or scope
    public decimal? Amount { get; set; }

    public string RequestedBy { get; set; } = string.Empty;
    public string? RequestedAtUtc { get; set; }

    /// <summary>Permission needed to act on this item - the UI hides the action otherwise.</summary>
    public string RequiredPermission { get; set; } = string.Empty;

    /// <summary>Where the approver goes to act, e.g. "/formation-requests/{id}".</summary>
    public string LinkPath { get; set; } = string.Empty;

    /// <summary>True when this row is a recorded exception rather than an item awaiting a decision.</summary>
    public bool Informational { get; set; }
}

public class ApprovalCenterDto
{
    public List<ApprovalItemDto> Items { get; set; } = new();
    public Dictionary<string, int> CountsByCategory { get; set; } = new();
    public int TotalPending { get; set; }
}
