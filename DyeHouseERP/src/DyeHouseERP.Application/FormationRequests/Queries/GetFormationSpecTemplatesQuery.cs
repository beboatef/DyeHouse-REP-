using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.FormationRequests.DTOs;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.FormationRequests.Queries;

/// <summary>
/// The reusable specification master (spec section 29). These are the "cells"
/// the user picks from so the same width / meter-per-kg / tub format and
/// instruction blocks never have to be retyped.
/// </summary>
public record GetFormationSpecTemplatesQuery(bool? ActiveOnly = null, string? Search = null)
    : IRequest<List<FormationSpecTemplateDto>>;

public class GetFormationSpecTemplatesQueryHandler
    : IRequestHandler<GetFormationSpecTemplatesQuery, List<FormationSpecTemplateDto>>
{
    private readonly IApplicationDbContext _db;
    public GetFormationSpecTemplatesQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<List<FormationSpecTemplateDto>> Handle(GetFormationSpecTemplatesQuery request, CancellationToken cancellationToken)
    {
        var query = _db.FormationSpecTemplates.AsNoTracking().AsQueryable();

        if (request.ActiveOnly == true)
            query = query.Where(t => t.IsActive);

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim();
            query = query.Where(t =>
                t.Code.Contains(term) || t.NameAr.Contains(term) || t.NameEn.Contains(term) ||
                (t.TubFormat != null && t.TubFormat.Contains(term)));
        }

        return await query
            .OrderBy(t => t.Code)
            .Select(t => new FormationSpecTemplateDto
            {
                Id = t.Id,
                Code = t.Code,
                NameAr = t.NameAr,
                NameEn = t.NameEn,
                WidthCm = t.WidthCm,
                MetersPerKg = t.MetersPerKg,
                Gsm = t.Gsm,
                TubFormat = t.TubFormat,
                WindingTapeFormat = t.WindingTapeFormat,
                Notes = t.Notes,
                QualityInstructions = t.QualityInstructions,
                LabInstructions = t.LabInstructions,
                InternalInstructions = t.InternalInstructions,
                CustomerInstructions = t.CustomerInstructions,
                IsActive = t.IsActive
            })
            .ToListAsync(cancellationToken);
    }
}
