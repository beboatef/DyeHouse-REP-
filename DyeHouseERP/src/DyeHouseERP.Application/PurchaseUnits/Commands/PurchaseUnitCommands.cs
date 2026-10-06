using DyeHouseERP.Application.Common.Exceptions;
using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.PurchaseUnits.DTOs;
using DyeHouseERP.Application.PurchaseUnits.Queries;
using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Exceptions;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.PurchaseUnits.Commands;

// ---------------------------------------------------------------------
// Create
// ---------------------------------------------------------------------

/// <summary>
/// Creates a purchase unit (spec section 44). Both names are required - custom
/// units are bilingual by definition, not "Arabic with an English fallback".
/// A conversion factor is optional; omit it and the unit simply does not
/// convert, exactly as the spec requires.
/// </summary>
public record CreatePurchaseUnitCommand(
    string Code, string NameAr, string NameEn,
    decimal? ConversionFactor = null, Guid? BaseUnitId = null, string? Notes = null) : IRequest<PurchaseUnitDto>;

public class CreatePurchaseUnitCommandValidator : AbstractValidator<CreatePurchaseUnitCommand>
{
    public CreatePurchaseUnitCommandValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(30);
        RuleFor(x => x.NameAr).NotEmpty().MaximumLength(100);
        RuleFor(x => x.NameEn).NotEmpty().MaximumLength(100);
        RuleFor(x => x.ConversionFactor).GreaterThan(0).When(x => x.ConversionFactor.HasValue);
    }
}

public class CreatePurchaseUnitCommandHandler : IRequestHandler<CreatePurchaseUnitCommand, PurchaseUnitDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public CreatePurchaseUnitCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<PurchaseUnitDto> Handle(CreatePurchaseUnitCommand request, CancellationToken cancellationToken)
    {
        var code = request.Code.Trim().ToUpperInvariant();
        if (await _db.PurchaseUnits.AnyAsync(u => u.Code == code, cancellationToken))
            throw new DuplicateCodeException("Purchase Unit", code);

        var baseUnit = await PurchaseUnitValidation.ResolveBaseUnitAsync(_db, request.BaseUnitId, null, cancellationToken);

        var unit = new PurchaseUnit(
            code, request.NameAr, request.NameEn, _currentUser.UserName,
            request.ConversionFactor, request.BaseUnitId, request.Notes);

        _db.PurchaseUnits.Add(unit);
        await _db.SaveChangesAsync(cancellationToken);

        return GetPurchaseUnitsQueryHandler.Map(unit, baseUnit);
    }
}

// ---------------------------------------------------------------------
// Update
// ---------------------------------------------------------------------

/// <summary>Renames a unit and re-configures its conversion. The code is immutable - purchase documents reference units by code.</summary>
public record UpdatePurchaseUnitCommand(
    Guid Id, string NameAr, string NameEn,
    decimal? ConversionFactor = null, Guid? BaseUnitId = null, string? Notes = null) : IRequest<PurchaseUnitDto>;

public class UpdatePurchaseUnitCommandValidator : AbstractValidator<UpdatePurchaseUnitCommand>
{
    public UpdatePurchaseUnitCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.NameAr).NotEmpty().MaximumLength(100);
        RuleFor(x => x.NameEn).NotEmpty().MaximumLength(100);
        RuleFor(x => x.ConversionFactor).GreaterThan(0).When(x => x.ConversionFactor.HasValue);
    }
}

public class UpdatePurchaseUnitCommandHandler : IRequestHandler<UpdatePurchaseUnitCommand, PurchaseUnitDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public UpdatePurchaseUnitCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<PurchaseUnitDto> Handle(UpdatePurchaseUnitCommand request, CancellationToken cancellationToken)
    {
        var unit = await _db.PurchaseUnits.FirstOrDefaultAsync(u => u.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("PurchaseUnit", request.Id);

        var baseUnit = await PurchaseUnitValidation.ResolveBaseUnitAsync(_db, request.BaseUnitId, unit.Id, cancellationToken);

        unit.Update(request.NameAr, request.NameEn, request.ConversionFactor, request.BaseUnitId, request.Notes, _currentUser.UserName);

        await _db.SaveChangesAsync(cancellationToken);
        return GetPurchaseUnitsQueryHandler.Map(unit, baseUnit);
    }
}

// ---------------------------------------------------------------------
// Activate / deactivate
// ---------------------------------------------------------------------

/// <summary>
/// Activates or deactivates a unit. There is deliberately NO delete command:
/// a unit that has ever been used on a document must stay resolvable, so
/// deactivation is the supported lifecycle (spec section 44).
/// </summary>
public record SetPurchaseUnitActiveCommand(Guid Id, bool IsActive) : IRequest<PurchaseUnitDto>;

public class SetPurchaseUnitActiveCommandHandler : IRequestHandler<SetPurchaseUnitActiveCommand, PurchaseUnitDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public SetPurchaseUnitActiveCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<PurchaseUnitDto> Handle(SetPurchaseUnitActiveCommand request, CancellationToken cancellationToken)
    {
        var unit = await _db.PurchaseUnits.FirstOrDefaultAsync(u => u.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("PurchaseUnit", request.Id);

        if (request.IsActive) unit.Activate(_currentUser.UserName);
        else unit.Deactivate(_currentUser.UserName);

        await _db.SaveChangesAsync(cancellationToken);

        var baseUnit = await PurchaseUnitValidation.ResolveBaseUnitAsync(_db, unit.BaseUnitId, unit.Id, cancellationToken);
        return GetPurchaseUnitsQueryHandler.Map(unit, baseUnit);
    }
}

internal static class PurchaseUnitValidation
{
    /// <summary>
    /// Resolves and validates the conversion target unit. The calling unit may
    /// never be its own base, and the target must actually exist - a conversion
    /// that points at nothing would silently disable itself.
    /// </summary>
    internal static async Task<PurchaseUnit?> ResolveBaseUnitAsync(
        IApplicationDbContext db, Guid? baseUnitId, Guid? selfId, CancellationToken cancellationToken)
    {
        if (!baseUnitId.HasValue) return null;

        if (selfId.HasValue && baseUnitId.Value == selfId.Value)
            throw new DomainException("A unit cannot be defined as converting into itself.");

        return await db.PurchaseUnits.AsNoTracking().FirstOrDefaultAsync(u => u.Id == baseUnitId.Value, cancellationToken)
            ?? throw new NotFoundException("PurchaseUnit (conversion target)", baseUnitId.Value);
    }
}
