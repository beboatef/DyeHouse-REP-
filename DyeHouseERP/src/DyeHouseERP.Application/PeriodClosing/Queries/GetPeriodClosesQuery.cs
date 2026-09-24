using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.PeriodClosing.Commands;
using DyeHouseERP.Application.PeriodClosing.DTOs;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.PeriodClosing.Queries;

public record GetPeriodClosesQuery : IRequest<List<PeriodCloseDto>>;

public class GetPeriodClosesQueryHandler : IRequestHandler<GetPeriodClosesQuery, List<PeriodCloseDto>>
{
    private readonly IApplicationDbContext _db;
    public GetPeriodClosesQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<List<PeriodCloseDto>> Handle(GetPeriodClosesQuery request, CancellationToken cancellationToken)
    {
        var periods = await _db.PeriodCloses.AsNoTracking().OrderByDescending(p => p.PeriodStart).ToListAsync(cancellationToken);
        return periods.Select(ClosePeriodCommandHandler.Map).ToList();
    }
}
