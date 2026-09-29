using DyeHouseERP.Application.Common.Exceptions;
using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.Deliveries.DTOs;
using DyeHouseERP.Application.Deliveries.Queries;
using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Enums;
using DyeHouseERP.Domain.Exceptions;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.Deliveries.Commands;

/// <summary>Creates a Draft delivery, which may cover several Production Orders (spec section 31).</summary>
public record CreateDeliveryCommand(Guid CustomerId, DateTime DeliveryDate, string? Notes, List<DeliveryLineInput> Lines)
    : IRequest<DeliveryDto>;

public class CreateDeliveryCommandValidator : AbstractValidator<CreateDeliveryCommand>
{
    public CreateDeliveryCommandValidator()
    {
        RuleFor(x => x.CustomerId).NotEmpty();
        RuleFor(x => x.Lines).NotEmpty();
        RuleForEach(x => x.Lines).ChildRules(l =>
        {
            l.RuleFor(x => x.ProductionOrderId).NotEmpty();
            l.RuleFor(x => x.ItemId).NotEmpty();
            l.RuleFor(x => x).Must(x => x.QuantityKg is > 0 || x.QuantityMeter is > 0);
        });
    }
}

public class CreateDeliveryCommandHandler : IRequestHandler<CreateDeliveryCommand, DeliveryDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IDocumentNumberGenerator _numberGenerator;

    public CreateDeliveryCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser, IDocumentNumberGenerator numberGenerator)
    {
        _db = db; _currentUser = currentUser; _numberGenerator = numberGenerator;
    }

    public async Task<DeliveryDto> Handle(CreateDeliveryCommand request, CancellationToken cancellationToken)
    {
        // R2: a delivery line names a production order, and finished goods belong
        // to the order's own customer. Accepting an order that belongs to a
        // DIFFERENT customer would let a delivery (and its ledger rows, which are
        // written under the delivery's customer) consume and record another
        // customer's stock. Verified here, before anything is created.
        var lineOrderIds = request.Lines.Select(l => l.ProductionOrderId).Distinct().ToList();

        var ordersById = await _db.ProductionOrders
            .AsNoTracking()
            .Where(o => lineOrderIds.Contains(o.Id))
            .ToDictionaryAsync(o => o.Id, o => new { o.CustomerId, o.OrderNumber }, cancellationToken);

        foreach (var orderId in lineOrderIds)
        {
            if (!ordersById.TryGetValue(orderId, out var order))
                throw new NotFoundException("ProductionOrder", orderId);

            if (order.CustomerId != request.CustomerId)
                throw new DomainException(
                    $"Production order '{order.OrderNumber}' does not belong to the delivery's customer. " +
                    "A delivery may only contain finished goods for its own customer.");
        }

        var deliveryNumber = await _numberGenerator.NextAsync(DocumentType.Delivery, cancellationToken: cancellationToken);

        var delivery = new Delivery(deliveryNumber, request.DeliveryDate, request.CustomerId, _currentUser.UserName, request.Notes);
        foreach (var line in request.Lines)
            delivery.AddLine(line.ProductionOrderId, line.ItemId, line.Color, line.QuantityKg, line.QuantityMeter, line.PieceCount, line.RawOrigin, line.Notes);

        _db.Deliveries.Add(delivery);
        await _db.SaveChangesAsync(cancellationToken);

        return await GetDeliveriesQueryHandler.LoadDtoAsync(_db, delivery.Id, cancellationToken);
    }
}
