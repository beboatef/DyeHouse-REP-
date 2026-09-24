using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.ReportBuilder.DTOs;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.ReportBuilder.Queries;

public record GetReportTemplatesQuery : IRequest<List<SavedReportTemplateDto>>;

public class GetReportTemplatesQueryHandler : IRequestHandler<GetReportTemplatesQuery, List<SavedReportTemplateDto>>
{
    private readonly IApplicationDbContext _db;
    public GetReportTemplatesQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<List<SavedReportTemplateDto>> Handle(GetReportTemplatesQuery request, CancellationToken cancellationToken)
    {
        var templates = await _db.SavedReportTemplates.AsNoTracking().OrderBy(t => t.NameAr).ToListAsync(cancellationToken);
        return templates.Select(t => new SavedReportTemplateDto
        {
            Id = t.Id, NameAr = t.NameAr, NameEn = t.NameEn, EntityKey = t.EntityKey, Columns = t.Columns.ToList()
        }).ToList();
    }
}
