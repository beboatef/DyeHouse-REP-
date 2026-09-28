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

    // ---- Spec section 51: the operational KPIs a real dyehouse watches ----

    /// <summary>CUSTOMER-OWNED raw material still in the warehouses, in KG. Never valued as factory stock.</summary>
    public decimal CustomerRawMaterialKg { get; set; }

    /// <summary>FACTORY-owned material/chemical stock (KG/Gram/Liter summed in their own unit - reported as a line count and a KG figure only).</summary>
    public decimal MaterialStockKg { get; set; }
    public int LowStockMaterialCount { get; set; }

    /// <summary>Stock of operating supplies still in the supplies store, in KG (spec section 27).</summary>
    public decimal OperatingSupplyStockKg { get; set; }

    public int WorkInProgressOrders { get; set; }
    public int ExternalProcessingOutstandingCount { get; set; }

    public decimal OutstandingSupplierBalance { get; set; }
    public decimal OutstandingCustomerBalance { get; set; }

    /// <summary>Items waiting for a decision in the Approval Center (spec section 44).</summary>
    public int PendingApprovalsCount { get; set; }

    /// <summary>The most recent posted documents, newest first (spec section 51).</summary>
    public List<RecentTransactionDto> RecentTransactions { get; set; } = new();
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

/// <summary>One row of the dashboard's "recent transactions" feed (spec section 51).</summary>
public class RecentTransactionDto
{
    public string DocumentType { get; set; } = string.Empty;
    public string DocumentNumber { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public string? Party { get; set; }
    public string? Summary { get; set; }
    public decimal? Amount { get; set; }
    public string LinkPath { get; set; } = string.Empty;
}
