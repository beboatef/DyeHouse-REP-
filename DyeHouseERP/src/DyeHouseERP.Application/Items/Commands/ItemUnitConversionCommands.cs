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
/// Creates or replaces the ONE explicit conversion rule for an (item + operation) pair
/// (spec sections 5-6). This is the only way a KG&lt;-&gt;Meter conversion can ever exist in
/// the system: it is opt-in per item, scoped to a named operation, and never global.
///
/// A rule may only convert between units the item actually CONFIGURES - an item that uses a
/// single unit has nothing to convert, and a rule may not smuggle in a unit the item does not
/// use. Nothing calls this rule implicitly; a workflow that wants the conversion asks for it.
/// </summary>
public record SetItemUnitConversionCommand(
    Guid ItemId, UnitOfMeasure FromUnit, UnitOfMeasure ToUnit, decimal Factor,
    string Operation, string? Notes = null, bool IsActive = true) : IRequest<ItemUnitConversionDto>;

public class SetItemUnitConversionCommandValidator : AbstractValidator<SetItemUnitConversionCommand>
{
    public SetItemUnitConversionCommandValidator()
    {
        RuleFor(x => x.ItemId).NotEmpty();
        RuleFor(x => x.FromUnit).IsInEnum();
        RuleFor(x => x.ToUnit).IsInEnum();
        RuleFor(x => x.FromUnit).NotEqual(x => x.ToUnit)
            .WithMessage("A conversion rule must convert between two different units - KG and Meter are independent.");
        RuleFor(x => x.Factor).GreaterThan(0);
        RuleFor(x => x.Operation).NotEmpty().MaximumLength(60);
        RuleFor(x => x.Notes).MaximumLength(500);
    }
}

public class SetItemUnitConversionCommandHandler
    : IRequestHandler<SetItemUnitConversionCommand, ItemUnitConversionDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public SetItemUnitConversionCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    { _db = db; _currentUser = currentUser; }

    public async Task<ItemUnitConversionDto> Handle(SetItemUnitConversionCommand request, CancellationToken cancellationToken)
    {
        var item = await _db.Items.FirstOrDefaultAsync(i => i.Id == request.ItemId, cancellationToken)
            ?? throw new NotFoundException("Item", request.ItemId);

        var configured = item.ConfiguredUnits;
        // A conversion rule may only ever be expressed between units the item actually uses.
        // A single-unit item configures exactly one unit, so neither a cross-unit rule nor a
        // "convert to itself" rule can be valid here - the message explains both cases.
        if (configured.Count < 2)
            throw new DomainException(
                $"Item '{item.Code}' uses a single unit ({string.Join(" + ", configured.Select(UnitName))}), " +
                "so there is nothing to convert. Configure a secondary unit on the item first.");

        if (!configured.Contains(request.FromUnit) || !configured.Contains(request.ToUnit))
            throw new DomainException(
                $"Item '{item.Code}' is configured to use {string.Join(" + ", configured.Select(UnitName))}. " +
                $"A conversion rule may only convert between the item's own configured units - " +
                $"'{UnitName(request.FromUnit)}' -> '{UnitName(request.ToUnit)}' is not available for this item.");

        var operation = request.Operation.Trim();
        var existing = await _db.ItemUnitConversions
            .FirstOrDefaultAsync(c => c.ItemId == item.Id && c.Operation == operation, cancellationToken);

        if (existing is null)
        {
            existing = new ItemUnitConversion(item.Id, request.FromUnit, request.ToUnit, request.Factor,
                operation, _currentUser.UserName, request.Notes);
            if (!request.IsActive) existing.Deactivate(_currentUser.UserName);
            _db.ItemUnitConversions.Add(existing);
        }
        else
        {
            existing.Update(request.FromUnit, request.ToUnit, request.Factor, request.Notes, _currentUser.UserName);
            if (request.IsActive) existing.Activate(_currentUser.UserName);
            else existing.Deactivate(_currentUser.UserName);
        }

        await _db.SaveChangesAsync(cancellationToken);
        return ItemUnitConversionMapper.ToDto(existing);
    }

    private static string UnitName(UnitOfMeasure unit) => unit == UnitOfMeasure.KG ? "KG" : "Meter";
}

/// <summary>Removes an explicit conversion rule for an (item + operation) pair.</summary>
public record RemoveItemUnitConversionCommand(Guid ItemId, string Operation) : IRequest<bool>;

public class RemoveItemUnitConversionCommandHandler : IRequestHandler<RemoveItemUnitConversionCommand, bool>
{
    private readonly IApplicationDbContext _db;
    public RemoveItemUnitConversionCommandHandler(IApplicationDbContext db) => _db = db;

    public async Task<bool> Handle(RemoveItemUnitConversionCommand request, CancellationToken cancellationToken)
    {
        var operation = request.Operation?.Trim() ?? string.Empty;
        var rule = await _db.ItemUnitConversions
            .FirstOrDefaultAsync(c => c.ItemId == request.ItemId && c.Operation == operation, cancellationToken)
            ?? throw new NotFoundException("ItemUnitConversion", $"{request.ItemId}/{operation}");

        _db.ItemUnitConversions.Remove(rule);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }
}

internal static class ItemUnitConversionMapper
{
    public static ItemUnitConversionDto ToDto(ItemUnitConversion c) => new()
    {
        Id = c.Id,
        ItemId = c.ItemId,
        FromUnit = c.FromUnit,
        ToUnit = c.ToUnit,
        Factor = c.Factor,
        Operation = c.Operation,
        IsActive = c.IsActive,
        Notes = c.Notes
    };
}
