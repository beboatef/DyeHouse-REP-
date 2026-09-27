namespace DyeHouseERP.Application.Dashboard.DTOs;

/// <summary>
/// Every figure here is a live aggregate computed in the query below -
/// never a hardcoded or placeholder number (spec rule #19 "No fake
/// dashboard numbers").
/// </summary>
public class DashboardSummaryDto
{
    public int ActiveCustomers { get; set; }
    public int ActiveProductionOrders { get; set; }
    public int PendingInspections { get; set; }
    public int PendingNegativeStockRisk { get; set; } // messages currently with zero-or-negative-looking balance is out of scope; this counts overrides this month
    public decimal ReadyGoodsBalanceKg { get; set; }
    public int OpenInvoicesCount { get; set; }
    public decimal OpenInvoicesTotal { get; set; }

    /// <summary>Formation Requests waiting for approval (spec section 51 "Pending Formation Requests").</summary>
    public int PendingFormationRequests { get; set; }

    /// <summary>Formation Requests currently being produced or partially produced.</summary>
    public int FormationRequestsInProgress { get; set; }

    /// <summary>Checks physically in hand (spec section 51 "Due checks").</summary>
    public decimal ChecksInHandAmount { get; set; }
    public int ChecksDueSoonCount { get; set; }
    public int OverdueChecksCount { get; set; }

    /// <summary>Outstanding supplier balance placeholder is intentionally absent until Purchases lands - no fake numbers (spec rule #19).</summary>
    public int ActiveSuppliers { get; set; }
    public List<StatusCountDto> ProductionOrdersByStatus { get; set; } = new();
    public List<DailyReceiptDto> RawReceiptsLast14Days { get; set; } = new();
}

public class StatusCountDto
{
    public string Status { get; set; } = string.Empty;
    public int Count { get; set; }
}

public class DailyReceiptDto
{
    public DateTime Date { get; set; }
    public decimal TotalKg { get; set; }
}
