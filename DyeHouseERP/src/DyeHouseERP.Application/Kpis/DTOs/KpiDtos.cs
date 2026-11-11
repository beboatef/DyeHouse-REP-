namespace DyeHouseERP.Application.Kpis.DTOs;

/// <summary>
/// Window every KPI result carries (task rule: each result carries the date
/// range and the timezone used). All ERP timestamps are stored in UTC and all
/// KPI filters compare UTC values, so <see cref="KpiQueryHelpers.KpiTimeZone"/>
/// is always "UTC" - the range is echoed back exactly as it was applied, with
/// nulls meaning "unbounded" for point-in-time KPIs (exposure) or the stated
/// defaults for period KPIs (machine utilisation defaults to the current month).
/// </summary>
public abstract class KpiResultBase
{
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public string TimeZone { get; set; } = KpiQueryHelpers.KpiTimeZone;
    public DateTime ComputedAtUtc { get; set; }
}

/// <summary>Waste rate for one job order (spec Part 1 metric 1).</summary>
public class WasteRateOrderRow
{
    public Guid ProductionOrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public DateTime OrderDate { get; set; }
    public string CustomerCode { get; set; } = string.Empty;
    public decimal RawKg { get; set; }
    public decimal WasteKg { get; set; }
    /// <summary>Null when RawKg is 0 (division by zero returns null, never 0 or Infinity).</summary>
    public decimal? WasteRatePct { get; set; }
}

public class WasteRateKpiResult : KpiResultBase
{
    public List<WasteRateOrderRow> Orders { get; set; } = new();
    public decimal TotalRawKg { get; set; }
    public decimal TotalWasteKg { get; set; }
    public decimal? OverallRatePct { get; set; }
    public int OrdersWithWaste { get; set; }
}

/// <summary>Lead time for one finished job order (spec Part 1 metric 2).</summary>
public class LeadTimeOrderRow
{
    public Guid ProductionOrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public DateTime IssueDate { get; set; }
    public DateTime CompletionDate { get; set; }
    public double LeadTimeDays { get; set; }
}

public class LeadTimeKpiResult : KpiResultBase
{
    public List<LeadTimeOrderRow> Orders { get; set; } = new();
    public int FinishedOrders { get; set; }
    public double? AverageLeadTimeDays { get; set; }
    public double? MinLeadTimeDays { get; set; }
    public double? MaxLeadTimeDays { get; set; }
}

/// <summary>Running hours for one machine in the period (spec Part 1 metric 3).</summary>
public class MachineUtilisationRow
{
    public Guid? MachineId { get; set; }
    public string MachineCode { get; set; } = string.Empty;
    public string MachineName { get; set; } = string.Empty;
    public double RunningHours { get; set; }
    public double AvailableHours { get; set; }
    /// <summary>Null when AvailableHours is 0 (division by zero returns null).</summary>
    public decimal? UtilisationPct { get; set; }
}

/// <summary>Running hours for one production shift in the period (spec Part 1 metric 3).</summary>
public class ShiftUtilisationRow
{
    public Guid? ProductionShiftId { get; set; }
    public string ShiftCode { get; set; } = string.Empty;
    public string ShiftName { get; set; } = string.Empty;
    public decimal HoursPerDay { get; set; }
    public double RunningHours { get; set; }
    public double AvailableHours { get; set; }
    /// <summary>Null when AvailableHours is 0 (division by zero returns null).</summary>
    public decimal? UtilisationPct { get; set; }
}

public class MachineUtilisationKpiResult : KpiResultBase
{
    public int DaysInPeriod { get; set; }
    public List<MachineUtilisationRow> Machines { get; set; } = new();
    public List<ShiftUtilisationRow> Shifts { get; set; } = new();
    public double TotalRunningHours { get; set; }
    public double TotalAvailableHours { get; set; }
    public decimal? OverallUtilisationPct { get; set; }
}

/// <summary>Moved quantity grouped by stage or by shift (spec Part 1 metric 4).</summary>
public class ThroughputGroupRow
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int TransfersCount { get; set; }
    public decimal QuantityKg { get; set; }
}

public class ThroughputKpiResult : KpiResultBase
{
    public List<ThroughputGroupRow> ByStage { get; set; } = new();
    public List<ThroughputGroupRow> ByShift { get; set; } = new();
    public int TotalTransfers { get; set; }
    public decimal TotalQuantityKg { get; set; }
}

/// <summary>Open exposure of one customer (spec Part 1 metric 5).</summary>
public class CustomerExposureRow
{
    public Guid CustomerId { get; set; }
    public string CustomerCode { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    /// <summary>Open balance from the append-only customer ledger (debits minus credits).</summary>
    public decimal OpenBalance { get; set; }
    public int OpenInvoicesCount { get; set; }
    public decimal OpenInvoicesTotal { get; set; }
    public string? OldestUnpaidInvoiceNumber { get; set; }
    /// <summary>Null when the customer has no open invoice with a positive balance.</summary>
    public int? OldestUnpaidInvoiceAgeDays { get; set; }
}

public class CustomerExposureKpiResult : KpiResultBase
{
    public DateTime AsOfUtc { get; set; }
    public List<CustomerExposureRow> Customers { get; set; } = new();
    public decimal TotalOpenBalance { get; set; }
    public decimal TotalOpenInvoices { get; set; }
}

/// <summary>One cheque due inside the window (spec Part 1 metric 6).</summary>
public class ChequeDueRow
{
    public Guid CheckId { get; set; }
    public string CheckNumber { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string PartyCode { get; set; } = string.Empty;
    public string PartyName { get; set; } = string.Empty;
    public DateTime DueDate { get; set; }
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
    public int DaysUntilDue { get; set; }
}

public class ChequesDueKpiResult : KpiResultBase
{
    public int Days { get; set; }
    public List<ChequeDueRow> Cheques { get; set; } = new();
    public int ChequesCount { get; set; }
    public decimal TotalAmount { get; set; }
}

/// <summary>One negative-stock override in the period (spec Part 1 metric 7).</summary>
public class NegativeStockOverrideKpiRow
{
    public Guid Id { get; set; }
    public DateTime ApprovedAtUtc { get; set; }
    public string MessageNumber { get; set; } = string.Empty;
    public string CustomerCode { get; set; } = string.Empty;
    public string ItemCode { get; set; } = string.Empty;
    public decimal RequestedQuantity { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string ApprovedBy { get; set; } = string.Empty;
}

/// <summary>One period reopening in the period, sourced from the audit log (spec Part 1 metric 7).</summary>
public class PeriodReopeningKpiRow
{
    public Guid PeriodCloseId { get; set; }
    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }
    public DateTime ReopenedAtUtc { get; set; }
    public string? ReopenedBy { get; set; }
    public DateTime AuditOccurredAtUtc { get; set; }
    public string AuditUserName { get; set; } = string.Empty;
}

public class GovernanceKpiResult : KpiResultBase
{
    public int OverridesCount { get; set; }
    public List<NegativeStockOverrideKpiRow> Overrides { get; set; } = new();
    public int ReopeningsCount { get; set; }
    public List<PeriodReopeningKpiRow> Reopenings { get; set; } = new();
}

/// <summary>Compact dashboard rollup composing the seven KPIs (same numbers, no second computation).</summary>
public class KpiSummaryResult : KpiResultBase
{
    public decimal? OverallWasteRatePct { get; set; }
    public decimal TotalWasteKg { get; set; }
    public double? AverageLeadTimeDays { get; set; }
    public int FinishedOrders { get; set; }
    public decimal? OverallUtilisationPct { get; set; }
    public double TotalRunningHours { get; set; }
    public decimal TotalThroughputKg { get; set; }
    public decimal TotalCustomerExposure { get; set; }
    public int ChequesDueCount { get; set; }
    public decimal ChequesDueTotal { get; set; }
    public int OverridesCount { get; set; }
    public int ReopeningsCount { get; set; }
}
