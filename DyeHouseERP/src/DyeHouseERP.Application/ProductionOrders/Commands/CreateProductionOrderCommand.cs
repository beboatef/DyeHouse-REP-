using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.ProductionOrders.DTOs;
using DyeHouseERP.Application.ProductionOrders.Queries;
using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.ProductionOrders.Commands;

public record CreateProductionOrderCommand(
    Guid CustomerId, Guid ItemId, DateTime OrderDate,
    string? Color, decimal? RequestedQuantityKg, decimal? RequestedQuantityMeter,
    string? RawOrigin, string? CustomerReference, string? Notes,
    ProductionPriority Priority) : IRequest<ProductionOrderDto>;

public class CreateProductionOrderCommandValidator : AbstractValidator<CreateProductionOrderCommand>
{
    public CreateProductionOrderCommandValidator()
    {
        RuleFor(x => x.CustomerId).NotEmpty();
        RuleFor(x => x.ItemId).NotEmpty();
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
        var orderNumber = await _numberGenerator.NextAsync(DocumentType.ProductionOrder, cancellationToken: cancellationToken);

        var order = new ProductionOrder(
            orderNumber, request.CustomerId, request.ItemId, request.OrderDate, _currentUser.UserName,
            request.Color, request.RequestedQuantityKg, request.RequestedQuantityMeter,
            request.RawOrigin, request.CustomerReference, request.Notes, request.Priority);

        // Snapshot the currently-active stage route (spec section 19) onto this order.
        var activeStages = await _db.ProductionStageDefinitions
            .AsNoTracking()
            .Where(s => s.IsActive)
            .OrderBy(s => s.Sequence)
            .ToListAsync(cancellationToken);

        order.BuildStageRoute(activeStages);

        _db.ProductionOrders.Add(order);
        await _db.SaveChangesAsync(cancellationToken);

        return await GetProductionOrderByIdQueryHandler.LoadDtoAsync(_db, order.Id, cancellationToken);
    }
}
