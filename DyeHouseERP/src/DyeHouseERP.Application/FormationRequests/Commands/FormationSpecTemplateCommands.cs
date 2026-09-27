using DyeHouseERP.Application.Common.Exceptions;
using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.FormationRequests.DTOs;
using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Exceptions;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.FormationRequests.Commands;

/// <summary>
/// Creates a reusable specification master record / "cell" (spec section 29):
/// width, meter-per-kg or g/m², tub/tube format, winding tape format and the
/// instruction blocks, saved once and selectable on every future request.
/// </summary>
public record CreateFormationSpecTemplateCommand(
    string Code, string NameAr, string NameEn,
    decimal? WidthCm = null, decimal? MetersPerKg = null, decimal? Gsm = null,
    string? TubFormat = null, string? WindingTapeFormat = null, string? Notes = null,
    string? QualityInstructions = null, string? LabInstructions = null,
    string? InternalInstructions = null, string? CustomerInstructions = null)
    : IRequest<FormationSpecTemplateDto>;

public class CreateFormationSpecTemplateCommandValidator : AbstractValidator<CreateFormationSpecTemplateCommand>
{
    public CreateFormationSpecTemplateCommandValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(30);
        RuleFor(x => x)
            .Must(x => !string.IsNullOrWhiteSpace(x.NameAr) || !string.IsNullOrWhiteSpace(x.NameEn))
            .WithMessage("A specification name is required (Arabic and/or English).");
        RuleFor(x => x.WidthCm).GreaterThanOrEqualTo(0).When(x => x.WidthCm.HasValue);
        RuleFor(x => x.MetersPerKg).GreaterThanOrEqualTo(0).When(x => x.MetersPerKg.HasValue);
        RuleFor(x => x.Gsm).GreaterThanOrEqualTo(0).When(x => x.Gsm.HasValue);
    }
}

public class CreateFormationSpecTemplateCommandHandler
    : IRequestHandler<CreateFormationSpecTemplateCommand, FormationSpecTemplateDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    public CreateFormationSpecTemplateCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    { _db = db; _currentUser = currentUser; }

    public async Task<FormationSpecTemplateDto> Handle(CreateFormationSpecTemplateCommand request, CancellationToken cancellationToken)
    {
        var code = request.Code.Trim();
        var exists = await _db.FormationSpecTemplates.AnyAsync(t => t.Code == code, cancellationToken);
        if (exists)
            throw new DuplicateCodeException("Formation specification", code);

        var entity = new FormationSpecTemplate(
            code, request.NameAr, request.NameEn, _currentUser.UserName,
            request.WidthCm, request.MetersPerKg, request.Gsm, request.TubFormat, request.WindingTapeFormat,
            request.Notes, request.QualityInstructions, request.LabInstructions,
            request.InternalInstructions, request.CustomerInstructions);

        _db.FormationSpecTemplates.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);

        return FormationSpecTemplateMapper.Map(entity);
    }
}

/// <summary>
/// Edits a specification master record. This NEVER rewrites requests that already used it:
/// every request group holds its own copy of the values captured when the template was
/// selected (spec section 29), so history is immutable by construction.
/// </summary>
public record UpdateFormationSpecTemplateCommand(
    Guid Id, string? NameAr = null, string? NameEn = null,
    decimal? WidthCm = null, decimal? MetersPerKg = null, decimal? Gsm = null,
    string? TubFormat = null, string? WindingTapeFormat = null, string? Notes = null,
    string? QualityInstructions = null, string? LabInstructions = null,
    string? InternalInstructions = null, string? CustomerInstructions = null,
    bool? IsActive = null)
    : IRequest<FormationSpecTemplateDto>;

public class UpdateFormationSpecTemplateCommandValidator : AbstractValidator<UpdateFormationSpecTemplateCommand>
{
    public UpdateFormationSpecTemplateCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.WidthCm).GreaterThanOrEqualTo(0).When(x => x.WidthCm.HasValue);
        RuleFor(x => x.MetersPerKg).GreaterThanOrEqualTo(0).When(x => x.MetersPerKg.HasValue);
        RuleFor(x => x.Gsm).GreaterThanOrEqualTo(0).When(x => x.Gsm.HasValue);
    }
}

public class UpdateFormationSpecTemplateCommandHandler
    : IRequestHandler<UpdateFormationSpecTemplateCommand, FormationSpecTemplateDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    public UpdateFormationSpecTemplateCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    { _db = db; _currentUser = currentUser; }

    public async Task<FormationSpecTemplateDto> Handle(UpdateFormationSpecTemplateCommand request, CancellationToken cancellationToken)
    {
        var entity = await _db.FormationSpecTemplates.FirstOrDefaultAsync(t => t.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("Formation specification", request.Id);

        if (!string.IsNullOrWhiteSpace(request.NameAr) || !string.IsNullOrWhiteSpace(request.NameEn))
            entity.SetNames(request.NameAr ?? entity.NameAr, request.NameEn ?? entity.NameEn);

        entity.SetSpecification(
            request.WidthCm ?? entity.WidthCm,
            request.MetersPerKg ?? entity.MetersPerKg,
            request.Gsm ?? entity.Gsm,
            request.TubFormat ?? entity.TubFormat,
            request.WindingTapeFormat ?? entity.WindingTapeFormat);

        entity.SetInstructions(
            request.QualityInstructions ?? entity.QualityInstructions,
            request.LabInstructions ?? entity.LabInstructions,
            request.InternalInstructions ?? entity.InternalInstructions,
            request.CustomerInstructions ?? entity.CustomerInstructions);

        if (request.Notes is not null) entity.SetNotes(request.Notes);

        if (request.IsActive.HasValue)
        {
            if (request.IsActive.Value) entity.Activate(_currentUser.UserName);
            else entity.Deactivate(_currentUser.UserName);
        }

        await _db.SaveChangesAsync(cancellationToken);
        return FormationSpecTemplateMapper.Map(entity);
    }
}

/// <summary>
/// Activates/deactivates a specification master record. Deactivation only hides it from
/// pickers - requests that already used it keep their snapshot untouched.
/// </summary>
public record SetFormationSpecTemplateActiveCommand(Guid Id, bool IsActive) : IRequest<FormationSpecTemplateDto>;

public class SetFormationSpecTemplateActiveCommandHandler
    : IRequestHandler<SetFormationSpecTemplateActiveCommand, FormationSpecTemplateDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    public SetFormationSpecTemplateActiveCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    { _db = db; _currentUser = currentUser; }

    public async Task<FormationSpecTemplateDto> Handle(SetFormationSpecTemplateActiveCommand request, CancellationToken cancellationToken)
    {
        var entity = await _db.FormationSpecTemplates.FirstOrDefaultAsync(t => t.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("Formation specification", request.Id);

        if (request.IsActive) entity.Activate(_currentUser.UserName);
        else entity.Deactivate(_currentUser.UserName);

        await _db.SaveChangesAsync(cancellationToken);
        return FormationSpecTemplateMapper.Map(entity);
    }
}

internal static class FormationSpecTemplateMapper
{
    public static FormationSpecTemplateDto Map(FormationSpecTemplate entity) => new()
    {
        Id = entity.Id,
        Code = entity.Code,
        NameAr = entity.NameAr,
        NameEn = entity.NameEn,
        WidthCm = entity.WidthCm,
        MetersPerKg = entity.MetersPerKg,
        Gsm = entity.Gsm,
        TubFormat = entity.TubFormat,
        WindingTapeFormat = entity.WindingTapeFormat,
        Notes = entity.Notes,
        QualityInstructions = entity.QualityInstructions,
        LabInstructions = entity.LabInstructions,
        InternalInstructions = entity.InternalInstructions,
        CustomerInstructions = entity.CustomerInstructions,
        IsActive = entity.IsActive
    };
}
