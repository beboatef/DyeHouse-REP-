using DyeHouseERP.Application.Common.Exceptions;
using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.Items.DTOs;
using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Enums;
using DyeHouseERP.Domain.Exceptions;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.Items.Commands;

/// <summary>
/// Creates an item (spec sections 5-6). Code is manually entered and must be
/// unique; BaseUnit is the required primary unit and SecondaryUnit the optional
/// second one - both KG or Meter and never converted automatically. An item may
/// use ONE unit or TWO DIFFERENT units; <see cref="RequireBothUnits"/> decides
/// whether a movement must record both measurements. NameAr/NameEn are the
/// bilingual pair shown across the UI; Name is the canonical fallback used by
/// documents and reports.
/// </summary>
public record CreateItemCommand(
    string Code, string Name, UnitOfMeasure BaseUnit,
    string? NameAr = null, string? NameEn = null, string? Category = null,
    UnitOfMeasure? SecondaryUnit = null, bool RequireBothUnits = false) : IRequest<ItemDto>;

public class CreateItemCommandValidator : AbstractValidator<CreateItemCommand>
{
    public CreateItemCommandValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(30);
        RuleFor(x => x.Name).MaximumLength(200);
        RuleFor(x => x)
            .Must(x => !string.IsNullOrWhiteSpace(x.Name) || !string.IsNullOrWhiteSpace(x.NameAr) || !string.IsNullOrWhiteSpace(x.NameEn))
            .WithMessage("An item name is required (Arabic and/or English).");
        RuleFor(x => x.NameAr).MaximumLength(200);
        RuleFor(x => x.NameEn).MaximumLength(200);
        RuleFor(x => x.Category).MaximumLength(100);
        RuleFor(x => x.BaseUnit).IsInEnum();
        RuleFor(x => x.SecondaryUnit).IsInEnum().When(x => x.SecondaryUnit.HasValue);
        RuleFor(x => x.SecondaryUnit)
            .NotEqual(x => x.BaseUnit)
            .When(x => x.SecondaryUnit.HasValue)
            .WithMessage("The secondary unit must be different from the base unit - KG and Meter are independent measurements.");
        RuleFor(x => x.SecondaryUnit)
            .NotNull()
            .When(x => x.RequireBothUnits)
            .WithMessage("Configure a secondary unit before requiring both units.");
    }
}

public class CreateItemCommandHandler : IRequestHandler<CreateItemCommand, ItemDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public CreateItemCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<ItemDto> Handle(CreateItemCommand request, CancellationToken cancellationToken)
    {
        var code = request.Code.Trim();
        var exists = await _db.Items.AnyAsync(i => i.Code == code, cancellationToken);
        if (exists)
            throw new DuplicateCodeException("Item", code);

        var item = new Item(code, request.Name, request.BaseUnit, _currentUser.UserName,
            request.NameAr, request.NameEn, request.Category,
            request.SecondaryUnit, request.RequireBothUnits);

        _db.Items.Add(item);
        await _db.SaveChangesAsync(cancellationToken);

        return ToDto(item);
    }

    internal static ItemDto ToDto(Item item) => new()
    {
        Id = item.Id,
        Code = item.Code,
        Name = item.Name,
        NameAr = item.NameAr,
        NameEn = item.NameEn,
        Category = item.Category,
        BaseUnit = item.BaseUnit,
        SecondaryUnit = item.SecondaryUnit,
        RequireBothUnits = item.RequireBothUnits,
        IsActive = item.IsActive
    };
}

/// <summary>
/// Edits an item master record. The unit configuration (primary and/or secondary
/// unit) may only be changed while the item has no inventory history at all -
/// switching KG/Meter or adding/removing a secondary unit after stock was received
/// would silently reinterpret every historical quantity (spec sections 5-6 and 53).
/// Turning <see cref="UpdateItemCommand.RequireBothUnits"/> on or off is safe with
/// history: it only changes the requirement for NEW movements, never the meaning of
/// what was already posted.
/// </summary>
public record UpdateItemCommand(
    Guid Id, string? NameAr, string? NameEn, string? Category, UnitOfMeasure? BaseUnit, bool? IsActive,
    UnitOfMeasure? SecondaryUnit = null, bool ClearSecondaryUnit = false, bool? RequireBothUnits = null)
    : IRequest<ItemDto>;

public class UpdateItemCommandValidator : AbstractValidator<UpdateItemCommand>
{
    public UpdateItemCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.NameAr).MaximumLength(200);
        RuleFor(x => x.NameEn).MaximumLength(200);
        RuleFor(x => x.Category).MaximumLength(100);
        RuleFor(x => x.BaseUnit).IsInEnum().When(x => x.BaseUnit.HasValue);
        RuleFor(x => x.SecondaryUnit).IsInEnum().When(x => x.SecondaryUnit.HasValue);

        RuleFor(x => x.SecondaryUnit)
            .NotEqual(x => x.BaseUnit)
            .When(x => x.SecondaryUnit.HasValue && x.BaseUnit.HasValue)
            .WithMessage("The secondary unit must be different from the base unit - KG and Meter are independent measurements.");
    }
}

public class UpdateItemCommandHandler : IRequestHandler<UpdateItemCommand, ItemDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public UpdateItemCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    {
        _db = db; _currentUser = currentUser;
    }

    public async Task<ItemDto> Handle(UpdateItemCommand request, CancellationToken cancellationToken)
    {
        var item = await _db.Items.FirstOrDefaultAsync(i => i.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("Item", request.Id);

        if (!string.IsNullOrWhiteSpace(request.NameAr) || !string.IsNullOrWhiteSpace(request.NameEn))
            item.SetNames(request.NameAr, request.NameEn);

        item.SetCategory(request.Category);

        // Work out the intended unit configuration and whether it changes at all.
        var newBaseUnit = request.BaseUnit ?? item.BaseUnit;

        UnitOfMeasure? newSecondaryUnit;
        if (request.ClearSecondaryUnit)
            newSecondaryUnit = null;
        else
            newSecondaryUnit = request.SecondaryUnit ?? item.SecondaryUnit;

        var unitConfigurationChanged =
            newBaseUnit != item.BaseUnit || newSecondaryUnit != item.SecondaryUnit;

        if (unitConfigurationChanged)
        {
            var hasHistory = await _db.InventoryTransactions.AsNoTracking()
                .AnyAsync(t => t.ItemId == item.Id, cancellationToken);
            if (hasHistory)
                throw new DomainException(
                    $"The unit configuration of item '{item.Code}' cannot be changed: inventory transactions already " +
                    "exist for it. Create a new item instead - historical quantities must never be silently reinterpreted.");

            item.ConfigureUnits(newBaseUnit, newSecondaryUnit, request.RequireBothUnits ?? item.RequireBothUnits,
                _currentUser.UserName);
        }
        else if (request.RequireBothUnits.HasValue)
        {
            // Same units, only the per-item "require both" setting changes - safe with
            // history because it never reinterprets an already-posted quantity.
            item.ConfigureUnits(item.BaseUnit, item.SecondaryUnit,
                request.RequireBothUnits.Value, _currentUser.UserName);
        }

        if (request.IsActive.HasValue)
        {
            if (request.IsActive.Value) item.Activate(_currentUser.UserName);
            else item.Deactivate(_currentUser.UserName);
        }

        await _db.SaveChangesAsync(cancellationToken);
        return CreateItemCommandHandler.ToDto(item);
    }
}
