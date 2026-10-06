using DyeHouseERP.Application.Common.Exceptions;
using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.PriceLists.DTOs;
using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Enums;
using DyeHouseERP.Domain.Exceptions;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.PriceLists.Commands;

// =====================================================================
// Actual Cost List (spec section 34A) - stage + unit -> company cost
// =====================================================================

public record SetStageCostRateCommand(
    Guid StageDefinitionId, UnitOfMeasure Unit, decimal CostPerUnit, string? Notes = null)
    : IRequest<StageCostRateDto>;

public class SetStageCostRateCommandValidator : AbstractValidator<SetStageCostRateCommand>
{
    public SetStageCostRateCommandValidator()
    {
        RuleFor(x => x.StageDefinitionId).NotEmpty();
        RuleFor(x => x.Unit).IsInEnum();
        RuleFor(x => x.CostPerUnit).GreaterThanOrEqualTo(0);
    }
}

public class SetStageCostRateCommandHandler : IRequestHandler<SetStageCostRateCommand, StageCostRateDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public SetStageCostRateCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<StageCostRateDto> Handle(SetStageCostRateCommand request, CancellationToken cancellationToken)
    {
        var stage = await _db.ProductionStageDefinitions.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == request.StageDefinitionId, cancellationToken)
            ?? throw new NotFoundException("ProductionStageDefinition", request.StageDefinitionId);

        // Upsert against the unique (stage, unit) index, so there is always exactly
        // one rate for a stage+unit rather than two rows nobody can choose between.
        var rate = await _db.StageCostRates
            .FirstOrDefaultAsync(r => r.StageDefinitionId == request.StageDefinitionId && r.Unit == request.Unit, cancellationToken);

        if (rate is null)
        {
            rate = new StageCostRate(request.StageDefinitionId, request.Unit, request.CostPerUnit, _currentUser.UserName, request.Notes);
            _db.StageCostRates.Add(rate);
        }
        else
        {
            rate.Update(request.CostPerUnit, request.Notes, _currentUser.UserName);
            rate.Activate(_currentUser.UserName);
        }

        await _db.SaveChangesAsync(cancellationToken);
        return PriceListMapping.ToDto(rate, stage);
    }
}

public record SetStageCostRateActiveCommand(Guid Id, bool IsActive) : IRequest<StageCostRateDto>;

public class SetStageCostRateActiveCommandHandler : IRequestHandler<SetStageCostRateActiveCommand, StageCostRateDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public SetStageCostRateActiveCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<StageCostRateDto> Handle(SetStageCostRateActiveCommand request, CancellationToken cancellationToken)
    {
        var rate = await _db.StageCostRates.FirstOrDefaultAsync(r => r.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("StageCostRate", request.Id);

        if (request.IsActive) rate.Activate(_currentUser.UserName);
        else rate.Deactivate(_currentUser.UserName);

        await _db.SaveChangesAsync(cancellationToken);

        var stage = await _db.ProductionStageDefinitions.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == rate.StageDefinitionId, cancellationToken);
        return PriceListMapping.ToDto(rate, stage);
    }
}

// =====================================================================
// Customer Service Price List (spec section 36) - stage + customer + unit
// =====================================================================

/// <summary>
/// Sets a service price. Omit <paramref name="CustomerId"/> to set the GENERAL
/// DEFAULT; supply it to set that customer's own override.
/// </summary>
public record SetCustomerServicePriceCommand(
    Guid StageDefinitionId, Guid? CustomerId, UnitOfMeasure Unit, decimal PricePerUnit, string? Notes = null)
    : IRequest<CustomerServicePriceDto>;

public class SetCustomerServicePriceCommandValidator : AbstractValidator<SetCustomerServicePriceCommand>
{
    public SetCustomerServicePriceCommandValidator()
    {
        RuleFor(x => x.StageDefinitionId).NotEmpty();
        RuleFor(x => x.Unit).IsInEnum();
        RuleFor(x => x.PricePerUnit).GreaterThanOrEqualTo(0);
    }
}

public class SetCustomerServicePriceCommandHandler
    : IRequestHandler<SetCustomerServicePriceCommand, CustomerServicePriceDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public SetCustomerServicePriceCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<CustomerServicePriceDto> Handle(SetCustomerServicePriceCommand request, CancellationToken cancellationToken)
    {
        var stage = await _db.ProductionStageDefinitions.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == request.StageDefinitionId, cancellationToken)
            ?? throw new NotFoundException("ProductionStageDefinition", request.StageDefinitionId);

        Customer? customer = null;
        if (request.CustomerId.HasValue)
        {
            customer = await _db.Customers.AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == request.CustomerId.Value, cancellationToken)
                ?? throw new NotFoundException("Customer", request.CustomerId.Value);
        }

        // The general-default row has CustomerId = NULL, and SQL Server counts NULL
        // as a value inside the unique (stage, customer, unit) index. The comparison
        // is therefore written out explicitly rather than as a plain `== null`
        // comparison, so this is guaranteed to find the existing default row instead
        // of falling through to an insert that the index would reject as a duplicate.
        var price = await _db.CustomerServicePrices
            .FirstOrDefaultAsync(p => p.StageDefinitionId == request.StageDefinitionId
                                      && p.Unit == request.Unit
                                      && (request.CustomerId == null
                                          ? p.CustomerId == null
                                          : p.CustomerId == request.CustomerId.Value),
                cancellationToken);

        if (price is null)
        {
            price = new CustomerServicePrice(
                request.StageDefinitionId, request.CustomerId, request.Unit, request.PricePerUnit,
                _currentUser.UserName, request.Notes);
            _db.CustomerServicePrices.Add(price);
        }
        else
        {
            price.Update(request.PricePerUnit, request.Notes, _currentUser.UserName);
            price.Activate(_currentUser.UserName);
        }

        await _db.SaveChangesAsync(cancellationToken);
        return PriceListMapping.ToDto(price, stage, customer);
    }
}

public record SetCustomerServicePriceActiveCommand(Guid Id, bool IsActive) : IRequest<CustomerServicePriceDto>;

public class SetCustomerServicePriceActiveCommandHandler
    : IRequestHandler<SetCustomerServicePriceActiveCommand, CustomerServicePriceDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public SetCustomerServicePriceActiveCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<CustomerServicePriceDto> Handle(SetCustomerServicePriceActiveCommand request, CancellationToken cancellationToken)
    {
        var price = await _db.CustomerServicePrices.FirstOrDefaultAsync(p => p.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("CustomerServicePrice", request.Id);

        if (request.IsActive) price.Activate(_currentUser.UserName);
        else price.Deactivate(_currentUser.UserName);

        await _db.SaveChangesAsync(cancellationToken);

        var stage = await _db.ProductionStageDefinitions.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == price.StageDefinitionId, cancellationToken);
        var customer = price.CustomerId.HasValue
            ? await _db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == price.CustomerId.Value, cancellationToken)
            : null;

        return PriceListMapping.ToDto(price, stage, customer);
    }
}

// =====================================================================
// Apply a price to a Job Order (spec section 34) - the snapshot
// =====================================================================

/// <summary>
/// Snapshots the price a Job Order is charged - ONE row per Job Order per unit.
///
/// Resolution, as confirmed with the business:
///   1. a supplied <paramref name="PricePerUnit"/> is a deliberate manual override
///      (a reason is required) and wins;
///   2. otherwise the customer's own active price for that stage+unit;
///   3. otherwise the general default price for that stage+unit;
///   4. otherwise the command refuses rather than inventing a number.
///
/// Once written, this row is the order's own figure: later edits to the price list
/// do NOT change it.
/// </summary>
public record ApplyServicePriceToJobOrderCommand(
    Guid ProductionOrderId, Guid StageDefinitionId, UnitOfMeasure Unit,
    decimal? PricePerUnit = null, string? OverrideReason = null)
    : IRequest<ProductionOrderServicePriceDto>;

public class ApplyServicePriceToJobOrderCommandValidator : AbstractValidator<ApplyServicePriceToJobOrderCommand>
{
    public ApplyServicePriceToJobOrderCommandValidator()
    {
        RuleFor(x => x.ProductionOrderId).NotEmpty();
        RuleFor(x => x.StageDefinitionId).NotEmpty();
        RuleFor(x => x.Unit).IsInEnum();
        RuleFor(x => x.PricePerUnit).GreaterThanOrEqualTo(0).When(x => x.PricePerUnit.HasValue);
        RuleFor(x => x.OverrideReason).NotEmpty().When(x => x.PricePerUnit.HasValue)
            .WithMessage("A manual price override requires a reason.");
    }
}

public class ApplyServicePriceToJobOrderCommandHandler
    : IRequestHandler<ApplyServicePriceToJobOrderCommand, ProductionOrderServicePriceDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public ApplyServicePriceToJobOrderCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<ProductionOrderServicePriceDto> Handle(
        ApplyServicePriceToJobOrderCommand request, CancellationToken cancellationToken)
    {
        var order = await _db.ProductionOrders
            .FirstOrDefaultAsync(o => o.Id == request.ProductionOrderId, cancellationToken)
            ?? throw new NotFoundException("ProductionOrder", request.ProductionOrderId);

        var stage = await _db.ProductionStageDefinitions.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == request.StageDefinitionId, cancellationToken)
            ?? throw new NotFoundException("ProductionStageDefinition", request.StageDefinitionId);

        decimal pricePerUnit;
        Guid? sourceCustomerId = null;
        var isOverride = request.PricePerUnit.HasValue;

        if (isOverride)
        {
            pricePerUnit = request.PricePerUnit!.Value;
        }
        else
        {
            // Customer-specific first, then the general default. Both must be active -
            // a deactivated price is a deliberate withdrawal of that offer.
            var specific = await _db.CustomerServicePrices.AsNoTracking()
                .FirstOrDefaultAsync(p => p.StageDefinitionId == request.StageDefinitionId
                                          && p.CustomerId == order.CustomerId
                                          && p.Unit == request.Unit
                                          && p.IsActive, cancellationToken);

            var resolved = specific ?? await _db.CustomerServicePrices.AsNoTracking()
                .FirstOrDefaultAsync(p => p.StageDefinitionId == request.StageDefinitionId
                                          && p.CustomerId == null
                                          && p.Unit == request.Unit
                                          && p.IsActive, cancellationToken);

            if (resolved is null)
                throw new DomainException(
                    $"No service price is defined for stage '{stage.Name}' in {request.Unit} - neither a price for this customer nor a general default. Set one in the price list, or apply an explicit override.");

            pricePerUnit = resolved.PricePerUnit;
            sourceCustomerId = resolved.CustomerId;
        }

        var snapshot = await _db.ProductionOrderServicePrices
            .FirstOrDefaultAsync(s => s.ProductionOrderId == request.ProductionOrderId && s.Unit == request.Unit, cancellationToken);

        if (snapshot is null)
        {
            snapshot = new ProductionOrderServicePrice(
                request.ProductionOrderId, request.StageDefinitionId, request.Unit,
                pricePerUnit, sourceCustomerId, isOverride, request.OverrideReason, _currentUser.UserName);
            _db.ProductionOrderServicePrices.Add(snapshot);
        }
        else
        {
            snapshot.Reapply(
                request.StageDefinitionId, pricePerUnit, sourceCustomerId, isOverride,
                request.OverrideReason, _currentUser.UserName);
        }

        await _db.SaveChangesAsync(cancellationToken);

        var customer = sourceCustomerId.HasValue
            ? await _db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == sourceCustomerId.Value, cancellationToken)
            : null;

        return PriceListMapping.ToDto(snapshot, stage, customer);
    }
}
