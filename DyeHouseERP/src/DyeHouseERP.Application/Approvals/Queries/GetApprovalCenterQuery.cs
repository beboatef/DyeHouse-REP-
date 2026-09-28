using DyeHouseERP.Application.Approvals.DTOs;
using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Domain.Common;
using DyeHouseERP.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.Approvals.Queries;

/// <summary>
/// The centralized Approval Center (spec section 44). It does NOT duplicate any
/// approval logic - it reads the real pending states that already exist in the
/// modules (a submitted Formation Request, a pending Production Request, a
/// submitted Purchase Order, a draft Payroll Run) and presents them in one
/// queue, each with the permission its own module enforces and the link where
/// it is actually acted on. Approving from the queue runs the module's own
/// command, so there is exactly one implementation of every rule.
///
/// Categories the caller is not allowed to approve are omitted entirely, so the
/// queue never shows an action that would be refused server-side.
/// </summary>
public record GetApprovalCenterQuery(int RecentDays = 30) : IRequest<ApprovalCenterDto>;

public class GetApprovalCenterQueryHandler : IRequestHandler<GetApprovalCenterQuery, ApprovalCenterDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public GetApprovalCenterQueryHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    { _db = db; _currentUser = currentUser; }

    public async Task<ApprovalCenterDto> Handle(GetApprovalCenterQuery request, CancellationToken cancellationToken)
    {
        var items = new List<ApprovalItemDto>();
        var recentCutoff = DateTime.UtcNow.AddDays(-Math.Abs(request.RecentDays));

        // ---- Formation Requests awaiting approval (spec sections 32 + 44) ----
        if (_currentUser.HasPermission(Permissions.FormationApprove) ||
            _currentUser.HasPermission(Permissions.FormationReject))
        {
            var requests = await _db.FormationRequests.AsNoTracking()
                .Where(r => r.Status == FormationRequestStatus.Submitted)
                .OrderBy(r => r.RequestDate)
                .Take(200)
                .ToListAsync(cancellationToken);

            var customerIds = requests.Select(r => r.CustomerId).Distinct().ToList();
            var customers = await _db.Customers.AsNoTracking()
                .Where(c => customerIds.Contains(c.Id))
                .Select(c => new { c.Id, c.Name })
                .ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken);

            items.AddRange(requests.Select(r => new ApprovalItemDto
            {
                Category = "FormationRequest",
                Id = r.Id,
                DocumentNumber = r.RequestNumber,
                Date = r.RequestDate,
                Party = customers.TryGetValue(r.CustomerId, out var cn) ? cn : null,
                Summary = $"{r.TotalQuantity:0.###} {r.Unit}",
                RequestedBy = r.CreatedBy,
                RequestedAtUtc = r.CreatedAtUtc.ToString("O"),
                RequiredPermission = Permissions.FormationApprove,
                LinkPath = $"/formation-requests/{r.Id}"
            }));
        }

        // ---- Production Requests awaiting approval (spec section 44) ----
        if (_currentUser.HasPermission(Permissions.ProductionEdit))
        {
            var productionRequests = await _db.ProductionRequests.AsNoTracking()
                .Where(r => r.Status == ProductionRequestStatus.Pending)
                .OrderBy(r => r.RequestDate)
                .Take(200)
                .ToListAsync(cancellationToken);

            var customerIds = productionRequests.Select(r => r.CustomerId).Distinct().ToList();
            var customers = await _db.Customers.AsNoTracking()
                .Where(c => customerIds.Contains(c.Id))
                .Select(c => new { c.Id, c.Name })
                .ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken);

            items.AddRange(productionRequests.Select(r => new ApprovalItemDto
            {
                Category = "ProductionRequest",
                Id = r.Id,
                DocumentNumber = r.RequestNumber,
                Date = r.RequestDate,
                Party = customers.TryGetValue(r.CustomerId, out var cn) ? cn : null,
                Summary = r.RequestedQuantityKg is not null ? $"{r.RequestedQuantityKg:0.###} KG"
                    : r.RequestedQuantityMeter is not null ? $"{r.RequestedQuantityMeter:0.###} m" : null,
                RequestedBy = r.CreatedBy,
                RequestedAtUtc = r.CreatedAtUtc.ToString("O"),
                RequiredPermission = Permissions.ProductionEdit,
                LinkPath = "/production-floor"
            }));
        }

        // ---- Purchase Orders awaiting approval (spec sections 35 + 44) ----
        if (_currentUser.HasPermission(Permissions.PurchasesApprove))
        {
            var orders = await _db.PurchaseOrders.AsNoTracking().Include(o => o.Lines)
                .Where(o => o.Status == PurchaseOrderStatus.Submitted)
                .OrderBy(o => o.OrderDate)
                .Take(200)
                .ToListAsync(cancellationToken);

            var supplierIds = orders.Select(o => o.SupplierId).Distinct().ToList();
            var suppliers = await _db.Suppliers.AsNoTracking()
                .Where(s => supplierIds.Contains(s.Id))
                .Select(s => new { s.Id, s.Name })
                .ToDictionaryAsync(s => s.Id, s => s.Name, cancellationToken);

            items.AddRange(orders.Select(o => new ApprovalItemDto
            {
                Category = "PurchaseOrder",
                Id = o.Id,
                DocumentNumber = o.OrderNumber,
                Date = o.OrderDate,
                Party = suppliers.TryGetValue(o.SupplierId, out var sn) ? sn : null,
                Amount = o.TotalValue,
                Summary = $"{o.Lines.Count} line(s)",
                RequestedBy = o.CreatedBy,
                RequestedAtUtc = o.SubmittedAtUtc?.ToString("O"),
                RequiredPermission = Permissions.PurchasesApprove,
                LinkPath = "/purchases?tab=orders"
            }));
        }

        // ---- Payroll runs awaiting approval (spec sections 36 + 44) ----
        if (_currentUser.HasPermission(Permissions.PayrollApprove))
        {
            var runs = await _db.PayrollRuns.AsNoTracking().Include(r => r.Lines)
                .Where(r => r.Status == PayrollRunStatus.Draft)
                .OrderBy(r => r.PeriodYear).ThenBy(r => r.PeriodMonth)
                .Take(60)
                .ToListAsync(cancellationToken);

            items.AddRange(runs.Select(r => new ApprovalItemDto
            {
                Category = "PayrollRun",
                Id = r.Id,
                DocumentNumber = r.RunNumber,
                Date = new DateTime(r.PeriodYear, r.PeriodMonth, 1),
                Amount = r.TotalNet,
                Summary = $"{r.PeriodMonth:00}/{r.PeriodYear} - {r.Lines.Count} employee(s)",
                RequestedBy = r.CreatedBy,
                RequestedAtUtc = r.CreatedAtUtc.ToString("O"),
                RequiredPermission = Permissions.PayrollApprove,
                LinkPath = "/payroll?tab=runs"
            }));
        }

        // ---- Recorded negative-stock exceptions (spec sections 12 + 44) ----
        // Informational: these are permanent, already-authorized exception records,
        // listed here so an owner can review who overrode stock and why.
        if (_currentUser.HasPermission(Permissions.InventoryApproveNegativeStock))
        {
            var overrides = await _db.NegativeStockOverrides.AsNoTracking()
                .Where(o => o.ApprovedAtUtc >= recentCutoff)
                .OrderByDescending(o => o.ApprovedAtUtc)
                .Take(100)
                .ToListAsync(cancellationToken);

            items.AddRange(overrides.Select(o => new ApprovalItemDto
            {
                Category = "NegativeStockException",
                Id = o.Id,
                DocumentNumber = $"NEG-{o.Id.ToString()[..8]}",
                Date = o.ApprovedAtUtc,
                Summary = o.Reason,
                RequestedBy = o.RequestedBy,
                RequestedAtUtc = o.ApprovedAtUtc.ToString("O"),
                RequiredPermission = Permissions.InventoryApproveNegativeStock,
                LinkPath = "/reports",
                Informational = true
            }));
        }

        var counts = items.Where(i => !i.Informational)
            .GroupBy(i => i.Category)
            .ToDictionary(g => g.Key, g => g.Count());

        return new ApprovalCenterDto
        {
            Items = items.OrderBy(i => i.Informational).ThenBy(i => i.Date).ToList(),
            CountsByCategory = counts,
            TotalPending = items.Count(i => !i.Informational)
        };
    }
}
