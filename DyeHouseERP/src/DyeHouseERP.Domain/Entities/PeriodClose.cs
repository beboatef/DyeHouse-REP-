using DyeHouseERP.Domain.Common;
using DyeHouseERP.Domain.Exceptions;

namespace DyeHouseERP.Domain.Entities;

/// <summary>
/// Closes a date range to normal posting (spec section 43). While closed,
/// new financial/inventory documents dated inside the range are rejected
/// (see IPeriodCloseService) - corrections must use reversal, or an
/// authorized user must explicitly Reopen the period (itself audited).
/// </summary>
public class PeriodClose : AuditableEntity
{
    public DateTime PeriodStart { get; private set; }
    public DateTime PeriodEnd { get; private set; }
    public string? Notes { get; private set; }
    public bool IsReopened { get; private set; }
    public string? ReopenedBy { get; private set; }
    public DateTime? ReopenedAtUtc { get; private set; }
    public string? ReopenReason { get; private set; }

    private PeriodClose() { } // EF Core

    public PeriodClose(DateTime periodStart, DateTime periodEnd, string createdBy, string? notes = null)
    {
        if (periodEnd < periodStart) throw new DomainException("Period end cannot be before period start.");

        PeriodStart = periodStart.Date;
        PeriodEnd = periodEnd.Date;
        Notes = notes;
        CreatedBy = createdBy;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public bool Covers(DateTime date) => date.Date >= PeriodStart && date.Date <= PeriodEnd;

    public void Reopen(string reason, string reopenedBy)
    {
        if (IsReopened) throw new DomainException("This period is already reopened.");
        IsReopened = true;
        ReopenedBy = reopenedBy;
        ReopenedAtUtc = DateTime.UtcNow;
        ReopenReason = reason;
    }
}
