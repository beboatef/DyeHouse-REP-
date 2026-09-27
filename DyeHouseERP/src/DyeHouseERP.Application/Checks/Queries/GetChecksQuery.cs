using DyeHouseERP.Application.Checks.DTOs;
using DyeHouseERP.Application.Common.Exceptions;
using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.Checks.Queries;

/// <summary>
/// Check register with the filters the reports need (spec sections 40 and 49):
/// direction (incoming customer / outgoing supplier), status, customer,
/// supplier, issue-date range and due-date cut-off.
/// </summary>
public record GetChecksQuery(
    CheckDirection? Direction = null,
    CheckStatus? Status = null,
    Guid? CustomerId = null,
    Guid? SupplierId = null,
    DateTime? From = null,
    DateTime? To = null,
    DateTime? DueBefore = null,
    bool? OverdueOnly = null,
    string? Search = null) : IRequest<List<CheckDto>>;

public class GetChecksQueryHandler : IRequestHandler<GetChecksQuery, List<CheckDto>>
{
    private readonly IApplicationDbContext _db;
    public GetChecksQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<List<CheckDto>> Handle(GetChecksQuery request, CancellationToken cancellationToken)
    {
        var query = _db.Checks.AsNoTracking().Include(c => c.Movements).AsQueryable();

        if (request.Direction.HasValue) query = query.Where(c => c.Direction == request.Direction);
        if (request.Status.HasValue) query = query.Where(c => c.Status == request.Status);
        if (request.CustomerId.HasValue) query = query.Where(c => c.CustomerId == request.CustomerId);
        if (request.SupplierId.HasValue) query = query.Where(c => c.SupplierId == request.SupplierId);
        if (request.From.HasValue) query = query.Where(c => c.IssueDate >= request.From);
        if (request.To.HasValue) query = query.Where(c => c.IssueDate <= request.To);
        if (request.DueBefore.HasValue) query = query.Where(c => c.DueDate <= request.DueBefore);

        if (request.OverdueOnly == true)
        {
            var today = DateTime.UtcNow.Date;
            query = query.Where(c => c.DueDate < today &&
                c.Status != CheckStatus.Cleared && c.Status != CheckStatus.Cancelled);
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim();
            query = query.Where(c => c.CheckNumber.Contains(term) || c.BankName.Contains(term) ||
                c.CurrentHolder.Contains(term) || c.Issuer.Contains(term));
        }

        var checks = await query
            .OrderBy(c => c.DueDate)
            .ThenByDescending(c => c.IssueDate)
            .ToListAsync(cancellationToken);

        return await CheckDtoBuilder.BuildManyAsync(_db, checks, includeMovements: true, cancellationToken);
    }
}

public record GetCheckByIdQuery(Guid Id) : IRequest<CheckDto>;

public class GetCheckByIdQueryHandler : IRequestHandler<GetCheckByIdQuery, CheckDto>
{
    private readonly IApplicationDbContext _db;
    public GetCheckByIdQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<CheckDto> Handle(GetCheckByIdQuery request, CancellationToken cancellationToken)
    {
        var entity = await _db.Checks.AsNoTracking().Include(c => c.Movements)
            .FirstOrDefaultAsync(c => c.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("Check", request.Id);

        var list = await CheckDtoBuilder.BuildManyAsync(_db, new List<Check> { entity }, includeMovements: true, cancellationToken);
        return list[0];
    }
}

/// <summary>
/// Checks received / issued / in hand / deposited / cleared / bounced / transferred totals (spec section 40).
/// Every figure is a live sum over the register - nothing is hardcoded.
/// </summary>
public record GetCheckRegisterSummaryQuery(DateTime? From = null, DateTime? To = null) : IRequest<CheckRegisterSummaryDto>;

public class GetCheckRegisterSummaryQueryHandler
    : IRequestHandler<GetCheckRegisterSummaryQuery, CheckRegisterSummaryDto>
{
    private readonly IApplicationDbContext _db;
    public GetCheckRegisterSummaryQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<CheckRegisterSummaryDto> Handle(GetCheckRegisterSummaryQuery request, CancellationToken cancellationToken)
    {
        var query = _db.Checks.AsNoTracking().AsQueryable();
        if (request.From.HasValue) query = query.Where(c => c.IssueDate >= request.From);
        if (request.To.HasValue) query = query.Where(c => c.IssueDate <= request.To);

        var checks = await query.ToListAsync(cancellationToken);
        var today = DateTime.UtcNow.Date;
        var dueSoonCutoff = today.AddDays(7);

        decimal SumOf(Func<Check, bool> predicate) => checks.Where(predicate).Sum(c => c.Amount);
        decimal SumStatus(CheckStatus status) => SumOf(c => c.Status == status);

        return new CheckRegisterSummaryDto
        {
            TotalChecks = checks.Count,
            TotalAmount = checks.Sum(c => c.Amount),
            CustomerChecksAmount = SumOf(c => c.Direction == CheckDirection.CustomerCheck),
            SupplierChecksAmount = SumOf(c => c.Direction == CheckDirection.SupplierCheck),
            InHandAmount = SumStatus(CheckStatus.InHand) + SumStatus(CheckStatus.Received),
            DepositedAmount = SumStatus(CheckStatus.Deposited),
            EndorsedAmount = SumStatus(CheckStatus.Endorsed),
            ClearedAmount = SumStatus(CheckStatus.Cleared),
            BouncedAmount = SumStatus(CheckStatus.Bounced),
            DueSoonCount = checks.Count(c => c.DueDate >= today && c.DueDate <= dueSoonCutoff &&
                c.Status != CheckStatus.Cleared && c.Status != CheckStatus.Cancelled),
            DueSoonAmount = SumOf(c => c.DueDate >= today && c.DueDate <= dueSoonCutoff &&
                c.Status != CheckStatus.Cleared && c.Status != CheckStatus.Cancelled),
            OverdueCount = checks.Count(c => c.DueDate < today &&
                c.Status != CheckStatus.Cleared && c.Status != CheckStatus.Cancelled),
            OverdueAmount = SumOf(c => c.DueDate < today &&
                c.Status != CheckStatus.Cleared && c.Status != CheckStatus.Cancelled),
            ByStatus = checks.GroupBy(c => c.Status)
                .Select(g => new CheckStatusCountDto { Status = g.Key, Count = g.Count(), Amount = g.Sum(c => c.Amount) })
                .OrderBy(x => x.Status)
                .ToList()
        };
    }
}

internal static class CheckDtoBuilder
{
    public static async Task<List<CheckDto>> BuildManyAsync(
        IApplicationDbContext db, List<Check> checks, bool includeMovements, CancellationToken cancellationToken)
    {
        var customerIds = checks.Where(c => c.CustomerId.HasValue).Select(c => c.CustomerId!.Value).Distinct().ToList();
        var supplierIds = checks.Where(c => c.SupplierId.HasValue).Select(c => c.SupplierId!.Value).Distinct().ToList();
        var accountIds = checks.Where(c => c.TreasuryAccountId.HasValue).Select(c => c.TreasuryAccountId!.Value).Distinct().ToList();

        var customers = await db.Customers.AsNoTracking()
            .Where(c => customerIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => (c.Code, c.Name), cancellationToken);

        var suppliers = await db.Suppliers.AsNoTracking()
            .Where(s => supplierIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => (s.Code, s.Name), cancellationToken);

        var accounts = await db.TreasuryAccounts.AsNoTracking()
            .Where(a => accountIds.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, a => a.Name, cancellationToken);

        var today = DateTime.UtcNow.Date;

        return checks.Select(c =>
        {
            string? customerCode = null, customerName = null;
            if (c.CustomerId.HasValue && customers.TryGetValue(c.CustomerId.Value, out var cu))
            {
                customerCode = cu.Code;
                customerName = cu.Name;
            }

            string? supplierCode = null, supplierName = null;
            if (c.SupplierId.HasValue && suppliers.TryGetValue(c.SupplierId.Value, out var su))
            {
                supplierCode = su.Code;
                supplierName = su.Name;
            }

            return new CheckDto
            {
                Id = c.Id,
                CheckNumber = c.CheckNumber,
                Direction = c.Direction,
                Status = c.Status,
                BankName = c.BankName,
                BranchName = c.BranchName,
                Amount = c.Amount,
                Currency = c.Currency,
                IssueDate = c.IssueDate,
                DueDate = c.DueDate,
                Issuer = c.Issuer,
                OriginalHolder = c.OriginalHolder,
                CurrentHolder = c.CurrentHolder,
                CurrentHolderType = c.CurrentHolderType,
                CustomerId = c.CustomerId,
                CustomerCode = customerCode,
                CustomerName = customerName,
                SupplierId = c.SupplierId,
                SupplierCode = supplierCode,
                SupplierName = supplierName,
                TreasuryAccountId = c.TreasuryAccountId,
                TreasuryAccountName = c.TreasuryAccountId.HasValue && accounts.TryGetValue(c.TreasuryAccountId.Value, out var acc) ? acc : null,
                CustomerReference = c.CustomerReference,
                Notes = c.Notes,
                ReceivedAtUtc = c.ReceivedAtUtc,
                DepositedAtUtc = c.DepositedAtUtc,
                ClearedAtUtc = c.ClearedAtUtc,
                BouncedAtUtc = c.BouncedAtUtc,
                BounceReason = c.BounceReason,
                CancelledAtUtc = c.CancelledAtUtc,
                CancellationReason = c.CancellationReason,
                DaysToDueDate = c.Status is CheckStatus.Cleared or CheckStatus.Cancelled
                    ? null
                    : (int)(c.DueDate.Date - today).TotalDays,
                CreatedBy = c.CreatedBy,
                CreatedAtUtc = c.CreatedAtUtc,
                Movements = includeMovements
                    ? c.Movements.OrderBy(m => m.MovementDate).ThenBy(m => m.CreatedAtUtc).Select(m => new CheckMovementDto
                    {
                        Id = m.Id,
                        MovementType = m.MovementType,
                        FromHolder = m.FromHolder,
                        ToHolder = m.ToHolder,
                        ToHolderType = m.ToHolderType,
                        MovementDate = m.MovementDate,
                        Reason = m.Reason,
                        SupplierId = m.SupplierId,
                        SupplierName = m.SupplierId.HasValue && suppliers.TryGetValue(m.SupplierId.Value, out var ms) ? ms.Name : null,
                        TreasuryAccountId = m.TreasuryAccountId,
                        TreasuryAccountName = m.TreasuryAccountId.HasValue && accounts.TryGetValue(m.TreasuryAccountId.Value, out var ma) ? ma : null,
                        Notes = m.Notes,
                        CreatedBy = m.CreatedBy,
                        CreatedAtUtc = m.CreatedAtUtc
                    }).ToList()
                    : new List<CheckMovementDto>()
            };
        }).ToList();
    }
}
