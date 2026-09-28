using DyeHouseERP.Application.Approvals.Queries;
using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.Dashboard.DTOs;
using DyeHouseERP.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.Dashboard.Queries;

public record GetDashboardSummaryQuery : IRequest<DashboardSummaryDto>;

public class GetDashboardSummaryQueryHandler : IRequestHandler<GetDashboardSummaryQuery, DashboardSummaryDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ISender _mediator;

    public GetDashboardSummaryQueryHandler(IApplicationDbContext db, ISender mediator)
    { _db = db; _mediator = mediator; }

    public async Task<DashboardSummaryDto> Handle(GetDashboardSummaryQuery request, CancellationToken cancellationToken)
    {
        var activeCustomers = await _db.Customers.AsNoTracking().CountAsync(c => c.IsActive, cancellationToken);

        var activeOrders = await _db.ProductionOrders.AsNoTracking()
            .CountAsync(o => o.Status == ProductionOrderStatus.InProduction || o.Status == ProductionOrderStatus.RawAllocated, cancellationToken);

        var pendingInspections = await _db.RawMessages.AsNoTracking()
            .CountAsync(m => m.InspectionStatus == InspectionStatus.PendingInspection, cancellationToken);

        var monthStart = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
        var overridesThisMonth = await _db.NegativeStockOverrides.AsNoTracking()
            .CountAsync(o => o.ApprovedAtUtc >= monthStart, cancellationToken);

        var readyGoodsKg = await _db.InventoryTransactions.AsNoTracking()
            .Where(t => t.RawMessageId == null && t.ProductionOrderId != null)
            .SumAsync(t => (decimal?)((t.QuantityKg ?? 0) * (int)t.Direction), cancellationToken) ?? 0;

        var openInvoices = await _db.Invoices.AsNoTracking()
            .Where(i => i.Status == InvoiceStatus.Issued || i.Status == InvoiceStatus.PartiallyPaid)
            .ToListAsync(cancellationToken);

        var openInvoiceIds = openInvoices.Select(i => i.Id).ToList();
        var openInvoiceLines = await _db.InvoiceLines.AsNoTracking()
            .Where(l => openInvoiceIds.Contains(l.InvoiceId))
            .ToListAsync(cancellationToken);
        var openInvoicesTotal = openInvoices.Sum(i =>
            openInvoiceLines.Where(l => l.InvoiceId == i.Id).Sum(l => l.Quantity * l.ProcessingPrice) - i.Discount + i.Tax);

        var pendingFormationRequests = await _db.FormationRequests.AsNoTracking()
            .CountAsync(r => r.Status == FormationRequestStatus.Submitted, cancellationToken);

        var formationRequestsInProgress = await _db.FormationRequests.AsNoTracking()
            .CountAsync(r => r.Status == FormationRequestStatus.InProgress ||
                r.Status == FormationRequestStatus.PartiallyCompleted, cancellationToken);

        var activeSuppliers = await _db.Suppliers.AsNoTracking().CountAsync(s => s.IsActive, cancellationToken);

        var today = DateTime.UtcNow.Date;
        var dueSoonCutoff = today.AddDays(7);
        var openChecks = await _db.Checks.AsNoTracking()
            .Where(c => c.Status != CheckStatus.Cleared && c.Status != CheckStatus.Cancelled)
            .Select(c => new { c.Amount, c.DueDate, c.Status })
            .ToListAsync(cancellationToken);

        var checksInHand = openChecks
            .Where(c => c.Status is CheckStatus.InHand or CheckStatus.Received)
            .Sum(c => c.Amount);
        var checksDueSoon = openChecks.Count(c => c.DueDate >= today && c.DueDate <= dueSoonCutoff);
        var overdueChecks = openChecks.Count(c => c.DueDate < today);

        // ---- Spec section 51 operational KPIs ----

        // CUSTOMER-OWNED raw material still held: the raw-material ledger only
        // (RawMessageId set), never mixed with factory material or ready goods.
        var customerRawMaterialKg = await _db.InventoryTransactions.AsNoTracking()
            .Where(t => t.RawMessageId != null)
            .SumAsync(t => (decimal?)((t.QuantityKg ?? 0) * (int)t.Direction), cancellationToken) ?? 0;

        var materialBalances = await _db.MaterialTransactions.AsNoTracking()
            .GroupBy(t => t.MaterialId)
            .Select(g => new { MaterialId = g.Key, Balance = g.Sum(t => t.Quantity * (int)t.Direction) })
            .ToListAsync(cancellationToken);

        var materialMasters = await _db.Materials.AsNoTracking()
            .Where(m => m.IsActive)
            .Select(m => new { m.Id, m.Kind, m.Unit, m.ReorderLevel })
            .ToListAsync(cancellationToken);

        var balanceByMaterial = materialBalances.ToDictionary(b => b.MaterialId, b => b.Balance);

        // Only KG-unit stock is summed into one figure - mixing Gram and Liter
        // into a single total would be meaningless, so those are simply not
        // added up here (they remain fully visible on the materials screen).
        var materialStockKg = materialMasters
            .Where(m => m.Kind == MaterialKind.Chemical && m.Unit == MaterialUnit.KG)
            .Sum(m => balanceByMaterial.GetValueOrDefault(m.Id));

        var operatingSupplyStockKg = materialMasters
            .Where(m => m.Kind == MaterialKind.OperatingSupply && m.Unit == MaterialUnit.KG)
            .Sum(m => balanceByMaterial.GetValueOrDefault(m.Id));

        // "Low stock" only counts materials that actually have a threshold set -
        // a null threshold means "not tracked", never a zero threshold.
        var lowStockCount = materialMasters.Count(m =>
            m.ReorderLevel is not null && balanceByMaterial.GetValueOrDefault(m.Id) < m.ReorderLevel.Value);

        var workInProgress = await _db.ProductionOrders.AsNoTracking()
            .CountAsync(o => o.Status == ProductionOrderStatus.InProduction, cancellationToken);

        var externalProcessingOutstanding = await _db.RawExternalReleases.AsNoTracking()
            .CountAsync(r => r.Reason == RawReleaseReason.ExternalProcessing &&
                (r.Status == ExternalProcessingStatus.AwaitingReturn || r.Status == ExternalProcessingStatus.PartiallyReturned),
                cancellationToken);

        var outstandingSupplierBalance = await _db.SupplierLedgerEntries.AsNoTracking()
            .SumAsync(e => (decimal?)(e.Credit - e.Debit), cancellationToken) ?? 0;

        var outstandingCustomerBalance = await _db.CustomerLedgerEntries.AsNoTracking()
            .SumAsync(e => (decimal?)(e.Debit - e.Credit), cancellationToken) ?? 0;

        // Same query the Approval Center uses, so the two can never disagree.
        var approvals = await _mediator.Send(new GetApprovalCenterQuery(), cancellationToken);

        var recentTransactions = await BuildRecentTransactionsAsync(cancellationToken);

        var statusCounts = await _db.ProductionOrders.AsNoTracking()
            .GroupBy(o => o.Status)
            .Select(g => new StatusCountDto { Status = g.Key.ToString(), Count = g.Count() })
            .ToListAsync(cancellationToken);

        var since = DateTime.UtcNow.Date.AddDays(-13);
        var recentMessages = await _db.RawMessages.AsNoTracking()
            .Include(m => m.Lines)
            .Where(m => m.ReceiptDate >= since)
            .ToListAsync(cancellationToken);

        var dailyReceipts = recentMessages
            .GroupBy(m => m.ReceiptDate.Date)
            .Select(g => new DailyReceiptDto { Date = g.Key, TotalKg = g.SelectMany(m => m.Lines).Sum(l => l.QuantityKg ?? 0) })
            .OrderBy(x => x.Date)
            .ToList();

        return new DashboardSummaryDto
        {
            ActiveCustomers = activeCustomers,
            ActiveProductionOrders = activeOrders,
            PendingInspections = pendingInspections,
            PendingNegativeStockRisk = overridesThisMonth,
            ReadyGoodsBalanceKg = readyGoodsKg,
            OpenInvoicesCount = openInvoices.Count,
            OpenInvoicesTotal = openInvoicesTotal,
            PendingFormationRequests = pendingFormationRequests,
            FormationRequestsInProgress = formationRequestsInProgress,
            ChecksInHandAmount = checksInHand,
            ChecksDueSoonCount = checksDueSoon,
            OverdueChecksCount = overdueChecks,
            ActiveSuppliers = activeSuppliers,
            CustomerRawMaterialKg = customerRawMaterialKg,
            MaterialStockKg = materialStockKg,
            LowStockMaterialCount = lowStockCount,
            OperatingSupplyStockKg = operatingSupplyStockKg,
            WorkInProgressOrders = workInProgress,
            ExternalProcessingOutstandingCount = externalProcessingOutstanding,
            OutstandingSupplierBalance = outstandingSupplierBalance,
            OutstandingCustomerBalance = outstandingCustomerBalance,
            PendingApprovalsCount = approvals.TotalPending,
            RecentTransactions = recentTransactions,
            ProductionOrdersByStatus = statusCounts,
            RawReceiptsLast14Days = dailyReceipts
        };
    }

    /// <summary>
    /// The dashboard's "recent transactions" feed (spec section 51). Reads real
    /// posted documents only - receipts, invoices, goods receipts and material
    /// sales - each linking back to the screen that owns it.
    /// </summary>
    private async Task<List<RecentTransactionDto>> BuildRecentTransactionsAsync(CancellationToken ct)
    {
        var feed = new List<RecentTransactionDto>();

        feed.AddRange(await _db.RawMessages.AsNoTracking()
            .OrderByDescending(m => m.ReceiptDate).Take(5)
            .Select(m => new RecentTransactionDto
            {
                DocumentType = "RawReceiptMessage", DocumentNumber = m.MessageNumber,
                Date = m.ReceiptDate, Summary = m.Status.ToString(), LinkPath = "/warehouse?tab=receipts"
            }).ToListAsync(ct));

        feed.AddRange(await _db.Invoices.AsNoTracking()
            .OrderByDescending(i => i.InvoiceDate).Take(5)
            .Select(i => new RecentTransactionDto
            {
                DocumentType = "Invoice", DocumentNumber = i.InvoiceNumber,
                Date = i.InvoiceDate, Summary = i.Status.ToString(), LinkPath = "/invoices"
            }).ToListAsync(ct));

        feed.AddRange(await _db.PurchaseReceipts.AsNoTracking()
            .OrderByDescending(r => r.ReceiptDate).Take(5)
            .Select(r => new RecentTransactionDto
            {
                DocumentType = "PurchaseReceipt", DocumentNumber = r.ReceiptNumber,
                Date = r.ReceiptDate, Summary = r.SupplierDocumentNumber, LinkPath = "/purchases?tab=receipts"
            }).ToListAsync(ct));

        feed.AddRange(await _db.MaterialSales.AsNoTracking()
            .OrderByDescending(s => s.SaleDate).Take(5)
            .Select(s => new RecentTransactionDto
            {
                DocumentType = "MaterialSale", DocumentNumber = s.SaleNumber,
                Date = s.SaleDate, Party = s.BuyerName, Summary = s.Status.ToString(), LinkPath = "/material-sales"
            }).ToListAsync(ct));

        return feed.OrderByDescending(r => r.Date).Take(10).ToList();
    }
}
