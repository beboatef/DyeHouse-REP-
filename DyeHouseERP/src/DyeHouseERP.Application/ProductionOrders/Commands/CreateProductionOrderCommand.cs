using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.Common.Services;
using DyeHouseERP.Application.ProductionOrders.DTOs;
using DyeHouseERP.Application.ProductionOrders.Queries;
using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.ProductionOrders.Commands;

/// <summary>
/// LEGACY / INTERNAL ONLY - not reachable from the API.
///
/// There is deliberately NO endpoint that creates a Job Order without an issue: the
/// canonical workflow is <see cref="IssueRawMaterialCommand"/>, where the raw issue posts the
/// inventory OUT, creates the order and links the two in ONE transaction (spec sections 5-6).
/// This handler survives only so existing tests and internal scenarios can build an order
/// with no issue link (which the old create-then-allocate path produced).
///
/// It must NOT be exposed through a controller, and it cannot produce a second stock OUT:
/// the order it creates has a NULL SourceIssueTransactionId, so the first allocation through
/// <see cref="AllocateRawCommand"/> posts the only deduction it will ever have, and any later
/// allocation on an order that DOES carry an issue link is refused outright.
/// </summary>
public record CreateProductionOrderCommand(
    Guid CustomerId, Guid ItemId, DateTime OrderDate,
    string? Color, decimal? RequestedQuantityKg, decimal? RequestedQuantityMeter,
    string? RawOrigin, string? CustomerReference, string? Notes,
    ProductionPriority Priority,
    JobOrderType JobOrderType = JobOrderType.ClosedLine) : IRequest<ProductionOrderDto>;

public class CreateProductionOrderCommandValidator : AbstractValidator<CreateProductionOrderCommand>
{
    public CreateProductionOrderCommandValidator()
    {
        RuleFor(x => x.CustomerId).NotEmpty();
        RuleFor(x => x.ItemId).NotEmpty();
        RuleFor(x => x.JobOrderType).IsInEnum();
        RuleFor(x => x)
            .Must(x => x.RequestedQuantityKg is > 0 || x.RequestedQuantityMeter is > 0)
            .WithMessage("Provide a requested quantity in KG and/or Meter.");
    }
}

public class CreateProductionOrderCommandHandler : IRequestHandler<CreateProductionOrderCommand, ProductionOrderDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IDocumentNumberGenerator _numberGenerator;

    public CreateProductionOrderCommandHandler(
        IApplicationDbContext db, ICurrentUserService currentUser, IDocumentNumberGenerator numberGenerator)
    {
        _db = db;
        _currentUser = currentUser;
        _numberGenerator = numberGenerator;
    }

    public async Task<ProductionOrderDto> Handle(CreateProductionOrderCommand request, CancellationToken cancellationToken)
    {
        // Spec sections 5-6: the requested quantity may only use the item's configured
        // units, and BOTH are required when the item demands both.
        await ItemUnitGuard.EnsureValidQuantitiesAsync(
            _db, request.ItemId, request.RequestedQuantityKg, request.RequestedQuantityMeter,
            cancellationToken, "on this Job Order's requested quantity");

        var orderNumber = await _numberGenerator.NextAsync(DocumentType.ProductionOrder, cancellationToken: cancellationToken);

        var order = new ProductionOrder(
            orderNumber, request.CustomerId, request.ItemId, request.OrderDate, _currentUser.UserName,
            request.Color, request.RequestedQuantityKg, request.RequestedQuantityMeter,
            request.RawOrigin, request.CustomerReference, request.Notes, request.Priority,
            jobOrderType: request.JobOrderType);

        // Snapshot the currently-active stage route (spec section 19) onto this order.
        var activeStages = await _db.ProductionStageDefinitions
            .AsNoTracking()
            .Where(s => s.IsActive)
            .OrderBy(s => s.Sequence)
            .ToListAsync(cancellationToken);

        order.BuildStageRoute(activeStages, _currentUser.UserName);

        _db.ProductionOrders.Add(order);
        await _db.SaveChangesAsync(cancellationToken);

        return await GetProductionOrderByIdQueryHandler.LoadDtoAsync(_db, order.Id, cancellationToken);
    }
}
