using DyeHouseERP.Application.Approvals.Commands;
using DyeHouseERP.Application.Approvals.DTOs;
using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.Approvals.Queries;

/// <summary>
/// The separation-of-duties requests themselves (spec section 24), newest
/// first: who asked, what for, why, and - once decided - who decided it from
/// their own authenticated account and (for a rejection) why they refused.
///
/// Filtering is optional so the queue can show pending work and the decision
/// history behind it without a second endpoint.
/// </summary>
public record GetApprovalRequestsQuery(
    ApprovalRequestStatus? Status = null,
    ApprovalRequestType? Type = null) : IRequest<List<ApprovalRequestDto>>;

public class GetApprovalRequestsQueryHandler : IRequestHandler<GetApprovalRequestsQuery, List<ApprovalRequestDto>>
{
    private readonly IApplicationDbContext _db;
    public GetApprovalRequestsQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<List<ApprovalRequestDto>> Handle(GetApprovalRequestsQuery request, CancellationToken cancellationToken)
    {
        var query = _db.ApprovalRequests.AsNoTracking().AsQueryable();

        if (request.Status.HasValue)
            query = query.Where(a => a.Status == request.Status.Value);
        if (request.Type.HasValue)
            query = query.Where(a => a.Type == request.Type.Value);

        var items = await query
            .OrderByDescending(a => a.RequestedAtUtc)
            .Take(500)
            .ToListAsync(cancellationToken);

        return items.Select(ApprovalRequestKinds.ToDto).ToList();
    }
}
