using DyeHouseERP.Domain.Enums;

namespace DyeHouseERP.Application.Checks.DTOs;

public class CheckMovementDto
{
    public Guid Id { get; set; }
    public CheckMovementType MovementType { get; set; }
    public string FromHolder { get; set; } = string.Empty;
    public string ToHolder { get; set; } = string.Empty;
    public CheckHolderType ToHolderType { get; set; }
    public DateTime MovementDate { get; set; }
    public string? Reason { get; set; }
    public Guid? SupplierId { get; set; }
    public string? SupplierName { get; set; }
    public Guid? TreasuryAccountId { get; set; }
    public string? TreasuryAccountName { get; set; }
    public string? Notes { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
}

public class CheckDto
{
    public Guid Id { get; set; }
    public string CheckNumber { get; set; } = string.Empty;
    public CheckDirection Direction { get; set; }
    public CheckStatus Status { get; set; }

    public string BankName { get; set; } = string.Empty;
    public string? BranchName { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "EGP";

    public DateTime IssueDate { get; set; }
    public DateTime DueDate { get; set; }

    public string Issuer { get; set; } = string.Empty;
    public string OriginalHolder { get; set; } = string.Empty;
    public string CurrentHolder { get; set; } = string.Empty;
    public CheckHolderType CurrentHolderType { get; set; }

    public Guid? CustomerId { get; set; }
    public string? CustomerCode { get; set; }
    public string? CustomerName { get; set; }
    public Guid? SupplierId { get; set; }
    public string? SupplierCode { get; set; }
    public string? SupplierName { get; set; }

    public Guid? TreasuryAccountId { get; set; }
    public string? TreasuryAccountName { get; set; }

    public string? CustomerReference { get; set; }
    public string? Notes { get; set; }

    public DateTime? ReceivedAtUtc { get; set; }
    public DateTime? DepositedAtUtc { get; set; }
    public DateTime? ClearedAtUtc { get; set; }
    public DateTime? BouncedAtUtc { get; set; }
    public string? BounceReason { get; set; }
    public DateTime? CancelledAtUtc { get; set; }
    public string? CancellationReason { get; set; }

    /// <summary>Days until the due date (negative = overdue). Null once the check is cleared or cancelled.</summary>
    public int? DaysToDueDate { get; set; }

    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }

    public List<CheckMovementDto> Movements { get; set; } = new();
}

/// <summary>Register totals used by the checks reports (spec section 40).</summary>
public class CheckRegisterSummaryDto
{
    public int TotalChecks { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal CustomerChecksAmount { get; set; }
    public decimal SupplierChecksAmount { get; set; }
    public decimal InHandAmount { get; set; }
    public decimal DepositedAmount { get; set; }
    public decimal EndorsedAmount { get; set; }
    public decimal ClearedAmount { get; set; }
    public decimal BouncedAmount { get; set; }
    public int DueSoonCount { get; set; }
    public decimal DueSoonAmount { get; set; }
    public int OverdueCount { get; set; }
    public decimal OverdueAmount { get; set; }
    public List<CheckStatusCountDto> ByStatus { get; set; } = new();
}

public class CheckStatusCountDto
{
    public CheckStatus Status { get; set; }
    public int Count { get; set; }
    public decimal Amount { get; set; }
}
