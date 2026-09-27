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
/// Creates an item (spec section 6). Code is manually entered and must be
/// unique; BaseUnit is KG or Meter and can never be converted automatically.
/// NameAr/NameEn are the bilingual pair shown across the UI; Name is the
/// canonical fallback used by documents and reports.
/// </summary>
public record CreateItemCommand(
    string Code, string Name, UnitOfMeasure BaseUnit,
    string? NameAr = null, string? NameEn = null, string? Category = null) : IRequest<ItemDto>;

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
            request.NameAr, request.NameEn, request.Category);

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
        IsActive = item.IsActive
    };
}

/// <summary>
/// Edits an item master record. The base unit may only be changed while the
/// item has no inventory history at all - switching KG/Meter after stock was
/// received would silently reinterpret every historical quantity (spec
/// sections 6 and 53).
/// </summary>
public record UpdateItemCommand(
    Guid Id, string? NameAr, string? NameEn, string? Category, UnitOfMeasure? BaseUnit, bool? IsActive)
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

        if (request.BaseUnit.HasValue && request.BaseUnit.Value != item.BaseUnit)
        {
            var hasHistory = await _db.InventoryTransactions.AsNoTracking()
                .AnyAsync(t => t.ItemId == item.Id, cancellationToken);
            if (hasHistory)
                throw new DomainException(
                    $"The base unit of item '{item.Code}' cannot be changed: inventory transactions already exist for it. " +
                    "Create a new item instead - historical quantities must never be silently reinterpreted.");

            item.ChangeBaseUnit(request.BaseUnit.Value, _currentUser.UserName);
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
