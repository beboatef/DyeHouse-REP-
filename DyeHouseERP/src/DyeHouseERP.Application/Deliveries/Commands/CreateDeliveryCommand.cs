using DyeHouseERP.Application.Common.Exceptions;
using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.Common.Services;
using DyeHouseERP.Application.Deliveries.DTOs;
using DyeHouseERP.Application.Deliveries.Queries;
using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Enums;
using DyeHouseERP.Domain.Exceptions;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.Deliveries.Commands;

/// <summary>
/// Creates a Draft permit, which may cover several Production Orders (spec section 31).
///
/// Creating the permit moves NO stock: a draft is a document, and the physical delivery is the
/// separate Delivered transaction that posts the OUT rows exactly once. That separation is what makes
/// a sub-customer permit safe to issue - <see cref="PrimaryDeliveryId"/> links this permit to the
/// primary one and still posts nothing at all.
/// </summary>
public record CreateDeliveryCommand(
    Guid CustomerId, DateTime DeliveryDate, string? Notes, List<DeliveryLineInput> Lines,
    // Set when this is a SUB-CUSTOMER permit: the primary permit it is issued against (spec sect. 31).
    Guid? PrimaryDeliveryId = null) : IRequest<DeliveryDto>;

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
            l.RuleFor(x => x.OriginalRawQuantityKg).GreaterThanOrEqualTo(0).When(x => x.OriginalRawQuantityKg.HasValue);
            l.RuleFor(x => x.OriginalRawQuantityMeter).GreaterThanOrEqualTo(0).When(x => x.OriginalRawQuantityMeter.HasValue);
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
            .ToDictionaryAsync(o => o.Id, o => new
            {
                o.CustomerId, o.OrderNumber,
                o.RequestedQuantityKg, o.RequestedQuantityMeter, o.OrderDate
            }, cancellationToken);

        foreach (var orderId in lineOrderIds)
        {
            if (!ordersById.TryGetValue(orderId, out var order))
                throw new NotFoundException("ProductionOrder", orderId);

            if (order.CustomerId != request.CustomerId)
                throw new DomainException(
                    $"Production order '{order.OrderNumber}' does not belong to the delivery's customer. " +
                    "A delivery may only contain finished goods for its own customer.");
        }

        // ---- Sub-customer permit (spec section 31) --------------------------------
        var primaryCustomerId = await ResolvePrimaryPermitAsync(request, cancellationToken);

        // ---- Original raw accounting across partial deliveries (spec section 31) ----
        // The order's original quantity is FIXED and is never overwritten, so a partial delivery may
        // only ever settle what is still outstanding. KG and Meter are compared within their own unit
        // (never converted), and every quantity the caller sent is checked before anything is created.
        await EnsureOriginalRawWithinLimitsAsync(_db, request, cancellationToken);

        // Spec sections 5-6: every delivery line may only carry the units configured on
        // its item, and both measurements when the item requires them.
        await ItemUnitGuard.EnsureValidQuantitiesAsync(
            _db, request.Lines.Select(l => (l.ItemId, l.QuantityKg, l.QuantityMeter)),
            cancellationToken, "on this delivery");

        var deliveryNumber = await _numberGenerator.NextAsync(DocumentType.Delivery, cancellationToken: cancellationToken);

        var delivery = new Delivery(
            deliveryNumber, request.DeliveryDate, request.CustomerId, _currentUser.UserName, request.Notes,
            primaryDeliveryId: request.PrimaryDeliveryId, primaryCustomerId: primaryCustomerId);

        foreach (var line in request.Lines)
            delivery.AddLine(
                line.ProductionOrderId, line.ItemId, line.Color, line.QuantityKg, line.QuantityMeter,
                line.PieceCount, line.RawOrigin, line.Notes,
                line.OriginalRawQuantityKg, line.OriginalRawQuantityMeter);

        _db.Deliveries.Add(delivery);
        await _db.SaveChangesAsync(cancellationToken);

        return await GetDeliveriesQueryHandler.LoadDtoAsync(_db, delivery.Id, cancellationToken);
    }

    /// <summary>
    /// Validates a sub-customer permit and returns the PRIMARY customer it belongs to (null for an
    /// ordinary permit).
    ///
    /// The chain is data, not a convention: the named primary permit must exist, must not be
    /// cancelled, and the permit being created must genuinely be for a SUB-customer of that primary
    /// permit's customer (the stored parent relationship, spec section 7). A permit issued to an
    /// unrelated customer is refused - otherwise "sub-customer permit" would mean nothing and the
    /// traceability chain would be fiction.
    /// </summary>
    private async Task<Guid?> ResolvePrimaryPermitAsync(CreateDeliveryCommand request, CancellationToken cancellationToken)
    {
        if (!request.PrimaryDeliveryId.HasValue) return null;

        var primary = await _db.Deliveries.AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == request.PrimaryDeliveryId.Value, cancellationToken)
            ?? throw new NotFoundException("Delivery", request.PrimaryDeliveryId.Value);

        if (primary.Status == DeliveryStatus.Cancelled)
            throw new DomainException(
                $"Primary permit {primary.DeliveryNumber} is cancelled, so no sub-customer permit can be issued against it.");

        if (primary.CustomerId == request.CustomerId)
            throw new DomainException(
                $"Permit {primary.DeliveryNumber} was issued to this same customer, so it is not a primary permit for them.");

        var subCustomer = await _db.Customers.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == request.CustomerId, cancellationToken)
            ?? throw new NotFoundException("Customer", request.CustomerId);

        if (subCustomer.ParentCustomerId != primary.CustomerId)
            throw new DomainException(
                $"Customer '{subCustomer.Code}' is not a sub-customer of the primary permit's customer, so a " +
                $"sub-customer permit cannot be issued against {primary.DeliveryNumber}. Record the parent " +
                "customer on the customer master first.");

        return primary.CustomerId;
    }

    /// <summary>
    /// Refuses a delivery that would settle more original raw material than the Job Order actually
    /// started from (spec section 31): remaining original raw = original PO quantity - everything
    /// earlier non-cancelled deliveries of that order already settled, judged by the domain rule that
    /// owns it (<see cref="DyeHouseERP.Domain.Entities.ProductionOrder.EnsureOriginalRawWithinLimit"/>)
    /// so the screen and the server can never disagree. KG and Meter are checked separately.
    /// </summary>
    private static async Task EnsureOriginalRawWithinLimitsAsync(
        IApplicationDbContext db, CreateDeliveryCommand request, CancellationToken cancellationToken)
    {
        // Nothing to check when no line claims an original-raw quantity.
        if (!request.Lines.Any(l => l.OriginalRawQuantityKg is > 0 || l.OriginalRawQuantityMeter is > 0))
            return;

        var orderIds = request.Lines.Select(l => l.ProductionOrderId).Distinct().ToList();

        // Everything an earlier delivery of these orders already settled sits on its lines. A CANCELLED
        // delivery is excluded because cancelling reverses its stock effect, so it settles nothing.
        var cancelledDeliveryIds = await db.Deliveries.AsNoTracking()
            .Where(d => d.Status == DeliveryStatus.Cancelled)
            .Select(d => d.Id)
            .ToListAsync(cancellationToken);

        var settled = await db.DeliveryLines.AsNoTracking()
            .Where(l => orderIds.Contains(l.ProductionOrderId) && !cancelledDeliveryIds.Contains(l.DeliveryId))
            .Select(l => new { l.ProductionOrderId, l.OriginalRawQuantityKg, l.OriginalRawQuantityMeter })
            .ToListAsync(cancellationToken);

        foreach (var group in request.Lines.GroupBy(l => l.ProductionOrderId))
        {
            var order = await db.ProductionOrders.AsNoTracking()
                .FirstOrDefaultAsync(o => o.Id == group.Key, cancellationToken)
                ?? throw new NotFoundException("ProductionOrder", group.Key);

            var alreadyKg = settled.Where(s => s.ProductionOrderId == group.Key).Sum(s => s.OriginalRawQuantityKg ?? 0m);
            var alreadyMeter = settled.Where(s => s.ProductionOrderId == group.Key).Sum(s => s.OriginalRawQuantityMeter ?? 0m);

            var requestedKg = group.Sum(l => l.OriginalRawQuantityKg ?? 0m);
            var requestedMeter = group.Sum(l => l.OriginalRawQuantityMeter ?? 0m);

            order.EnsureOriginalRawWithinLimit(alreadyKg, alreadyMeter, requestedKg, requestedMeter);
        }
    }
}
