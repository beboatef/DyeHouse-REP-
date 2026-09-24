using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.ReportBuilder.DTOs;
using DyeHouseERP.Application.ReportBuilder.Queries;
using DyeHouseERP.Domain.Entities;
using FluentValidation;
using MediatR;

namespace DyeHouseERP.Application.ReportBuilder.Commands;

public record SaveReportTemplateCommand(string NameAr, string NameEn, string EntityKey, List<string> Columns) : IRequest<SavedReportTemplateDto>;

public class SaveReportTemplateCommandValidator : AbstractValidator<SaveReportTemplateCommand>
{
    public SaveReportTemplateCommandValidator()
    {
        RuleFor(x => x.NameAr).NotEmpty();
        RuleFor(x => x.NameEn).NotEmpty();
        RuleFor(x => x.EntityKey).Must(k => ReportableEntitiesRegistry.Entities.ContainsKey(k));
        RuleFor(x => x.Columns).NotEmpty();
    }
}

public class SaveReportTemplateCommandHandler : IRequestHandler<SaveReportTemplateCommand, SavedReportTemplateDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    public SaveReportTemplateCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser) { _db = db; _currentUser = currentUser; }

    public async Task<SavedReportTemplateDto> Handle(SaveReportTemplateCommand request, CancellationToken cancellationToken)
    {
        var template = new SavedReportTemplate(request.NameAr, request.NameEn, request.EntityKey, string.Join(",", request.Columns), _currentUser.UserName);
        _db.SavedReportTemplates.Add(template);
        await _db.SaveChangesAsync(cancellationToken);

        return new SavedReportTemplateDto { Id = template.Id, NameAr = template.NameAr, NameEn = template.NameEn, EntityKey = template.EntityKey, Columns = template.Columns.ToList() };
    }
}
