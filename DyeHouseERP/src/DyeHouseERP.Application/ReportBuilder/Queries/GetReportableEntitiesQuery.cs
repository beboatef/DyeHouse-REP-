using DyeHouseERP.Application.ReportBuilder.DTOs;
using MediatR;

namespace DyeHouseERP.Application.ReportBuilder.Queries;

public record GetReportableEntitiesQuery : IRequest<List<ReportableEntityDto>>;

public class GetReportableEntitiesQueryHandler : IRequestHandler<GetReportableEntitiesQuery, List<ReportableEntityDto>>
{
    public Task<List<ReportableEntityDto>> Handle(GetReportableEntitiesQuery request, CancellationToken cancellationToken)
    {
        var result = ReportableEntitiesRegistry.Entities.Select(e => new ReportableEntityDto
        {
            EntityKey = e.Key,
            Label = e.Value.Label,
            Columns = e.Value.Columns.ToList()
        }).ToList();

        return Task.FromResult(result);
    }
}
