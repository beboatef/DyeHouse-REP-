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
    public GetDashboardSummaryQueryHandler(IApplicationDbContext db) => _db = db;

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
            ProductionOrdersByStatus = statusCounts,
            RawReceiptsLast14Days = dailyReceipts
        };
    }
}
