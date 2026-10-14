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

        // ---- Sub-customer permit (spec section 31) --------------------------------
        // Resolved BEFORE the ownership check, because the linked primary permit is exactly what
        // makes the primary customer's own Job Order a valid line on a sub-customer's permit.
        var primaryPermit = await ResolvePrimaryPermitAsync(request, cancellationToken);
        var primaryCustomerId = primaryPermit?.CustomerId;

        foreach (var orderId in lineOrderIds)
        {
            if (!ordersById.TryGetValue(orderId, out var order))
                throw new NotFoundException("ProductionOrder", orderId);

            // Ordinary permit: the order must belong to the permit's own customer. SUB-customer
            // permit: the order may also belong to the LINKED PRIMARY customer - that is the job the
            // primary permit delivered - while any third customer is still refused. The relationship
            // is checked against stored data (the primary permit's customer), never by dropping the
            // check or inferring an owner from a code.
            if (order.CustomerId != request.CustomerId && order.CustomerId != primaryCustomerId)
                throw new DomainException(
                    $"Production order '{order.OrderNumber}' does not belong to the delivery's customer" +
                    (primaryCustomerId.HasValue ? " or to the primary permit's customer" : "") + ". " +
                    (primaryCustomerId.HasValue
                        ? "A sub-customer permit may only contain finished goods for its own customer or for the primary customer it is issued against."
                        : "A delivery may only contain finished goods for its own customer."));
        }

        // ---- Where a sub-permit's item and colour come from (spec section 31) ------
        // The item is NOT the sub-permit's own choice: it is the item of the lot the sub-permit
        // slices, and the colour is one of the colours the primary permit recorded for it. Resolved
        // here, before anything is created, so a line can never name goods the primary permit does
        // not carry - and so the stored colour is the primary permit's own spelling, never a new one.
        var lineColors = await ResolveSubPermitLineSourcesAsync(_db, request, primaryPermit, cancellationToken);

        // ---- Original raw accounting across partial deliveries (spec section 31) ----
        // The order's original quantity is FIXED and is never overwritten, so a partial delivery may
        // only ever settle what is still outstanding. KG and Meter are compared within their own unit
        // (never converted), and every quantity the caller sent is checked before anything is created.
        await EnsureOriginalRawWithinLimitsAsync(_db, request, cancellationToken);

        // A sub-customer permit may never promise a sub-customer more than the PRIMARY permit it is
        // issued against actually delivered (spec section 31): its lines are a slicing of that permit,
        // never additional goods. Checked cumulatively against every other non-cancelled sub-permit.
        await EnsureSubPermitWithinPrimaryAsync(_db, request, primaryPermit, cancellationToken);

        // Spec sections 5-6: every delivery line may only carry the units configured on
        // its item, and both measurements when the item requires them.
        //
        // A sub-customer permit is exempt from the item's "require both units" setting (and only
        // from that setting): it owns no movement, its goods are a slice of a permit that already
        // recorded both units, and the sub-customer may receive the single unit they asked for. The
        // unit-selection rules still hold - a quantity in a unit the item does not use is refused,
        // and nothing is ever converted between KG and Meter.
        await ItemUnitGuard.EnsureValidQuantitiesAsync(
            _db, request.Lines.Select(l => (l.ItemId, l.QuantityKg, l.QuantityMeter)),
            cancellationToken, "on this delivery",
            enforceRequireBothUnits: !request.PrimaryDeliveryId.HasValue);

        // A SUB-customer permit numbers itself from its OWN sequence (spec section 31): the primary
        // permit's DEL counter is untouched, and several sub-permits of one primary can be issued
        // without competing for the same number. The numbering engine is exactly the same one
        // (sp_getapplock + counter row, keyed by DocumentType), so the concurrency guarantee travels
        // with it - only the sequence key differs.
        var numberType = request.PrimaryDeliveryId.HasValue ? DocumentType.SubDelivery : DocumentType.Delivery;
        var deliveryNumber = await _numberGenerator.NextAsync(numberType, cancellationToken: cancellationToken);

        var delivery = new Delivery(
            deliveryNumber, request.DeliveryDate, request.CustomerId, _currentUser.UserName, request.Notes,
            primaryDeliveryId: request.PrimaryDeliveryId, primaryCustomerId: primaryCustomerId);

        foreach (var (line, index) in request.Lines.Select((l, i) => (l, i)))
            delivery.AddLine(
                line.ProductionOrderId, line.ItemId, lineColors[index], line.QuantityKg, line.QuantityMeter,
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
    private async Task<Delivery?> ResolvePrimaryPermitAsync(CreateDeliveryCommand request, CancellationToken cancellationToken)
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

        return primary;
    }

    /// <summary>
    /// Bounds a sub-customer permit by the primary permit it is issued against (spec section 31): the
    /// quantities a sub-permit claims, per (Job Order, Item), may never exceed what the primary permit
    /// delivered for that same Job Order, and the total already claimed by earlier sub-permits counts
    /// against the same allowance. A line naming the SUB-customer's own Job Order has no primary line to
    /// measure against, so it is not bounded here - only the primary customer's own job is.
    ///
    /// This is a DOCUMENT rule: it posts no stock and changes no balance, it only stops a sub-permit
    /// from promising goods the primary permit never delivered.
    /// </summary>
    private static async Task EnsureSubPermitWithinPrimaryAsync(
        IApplicationDbContext db, CreateDeliveryCommand request, Delivery? primary, CancellationToken cancellationToken)
    {
        if (primary is null) return;

        var primaryLines = await db.DeliveryLines.AsNoTracking()
            .Where(l => l.DeliveryId == primary.Id)
            .Select(l => new { l.ProductionOrderId, l.ItemId, l.QuantityKg, l.QuantityMeter })
            .ToListAsync(cancellationToken);

        if (primaryLines.Count == 0) return;

        // Every other NON-cancelled sub-permit already issued against this primary has already claimed
        // its share, so it is subtracted from what is still available.
        var siblingSubPermitIds = await db.Deliveries.AsNoTracking()
            .Where(d => d.PrimaryDeliveryId == primary.Id && d.Status != DeliveryStatus.Cancelled)
            .Select(d => d.Id)
            .ToListAsync(cancellationToken);

        var siblingSubPermitLines = siblingSubPermitIds.Count == 0
            ? new List<SubPermitClaim>()
            : (await db.DeliveryLines.AsNoTracking()
                .Where(l => siblingSubPermitIds.Contains(l.DeliveryId))
                .Select(l => new { l.ProductionOrderId, l.ItemId, l.QuantityKg, l.QuantityMeter })
                .ToListAsync(cancellationToken))
            .Select(l => new SubPermitClaim(l.ProductionOrderId, l.ItemId, l.QuantityKg ?? 0m, l.QuantityMeter ?? 0m))
            .ToList();

        const decimal tolerance = 0.001m;

        foreach (var group in request.Lines
            .Where(l => primaryLines.Any(p => p.ProductionOrderId == l.ProductionOrderId))
            .GroupBy(l => new { l.ProductionOrderId, l.ItemId }))
        {
            var allowedKg = primaryLines
                .Where(p => p.ProductionOrderId == group.Key.ProductionOrderId && p.ItemId == group.Key.ItemId)
                .Sum(p => p.QuantityKg ?? 0m);
            var allowedMeter = primaryLines
                .Where(p => p.ProductionOrderId == group.Key.ProductionOrderId && p.ItemId == group.Key.ItemId)
                .Sum(p => p.QuantityMeter ?? 0m);

            var claimedKg = siblingSubPermitLines
                .Where(c => c.ProductionOrderId == group.Key.ProductionOrderId && c.ItemId == group.Key.ItemId)
                .Sum(c => c.QuantityKg);
            var claimedMeter = siblingSubPermitLines
                .Where(c => c.ProductionOrderId == group.Key.ProductionOrderId && c.ItemId == group.Key.ItemId)
                .Sum(c => c.QuantityMeter);

            var requestedKg = group.Sum(l => l.QuantityKg ?? 0m);
            var requestedMeter = group.Sum(l => l.QuantityMeter ?? 0m);

            if (requestedKg > 0 && claimedKg + requestedKg > allowedKg + tolerance)
                throw new DomainException(
                    $"This sub-customer permit exceeds the primary permit {primary.DeliveryNumber}: it delivered " +
                    $"{allowedKg} KG for this Job Order, {claimedKg} KG is already covered by other sub-customer permits " +
                    $"and {requestedKg} KG more was requested.");
            if (requestedMeter > 0 && claimedMeter + requestedMeter > allowedMeter + tolerance)
                throw new DomainException(
                    $"This sub-customer permit exceeds the primary permit {primary.DeliveryNumber}: it delivered " +
                    $"{allowedMeter} Meter for this Job Order, {claimedMeter} Meter is already covered by other " +
                    $"sub-customer permits and {requestedMeter} Meter more was requested.");
        }
    }

    /// <summary>One quantity a sub-customer permit already claims against its primary permit.</summary>
    private sealed record SubPermitClaim(Guid ProductionOrderId, Guid ItemId, decimal QuantityKg, decimal QuantityMeter);

    /// <summary>
    /// Works out the ITEM and the COLOUR of every line of a sub-customer permit from the primary permit
    /// it is issued against (spec section 31), and returns the colour each line must be stored with.
    ///
    /// The rule the caller relies on: a sub-permit never introduces item or colour master data. Its
    /// item must be one the primary permit actually carries for that Job Order, and its colour must be
    /// one the primary permit recorded for that same item - stored in the primary permit's own spelling,
    /// never a differently-cased or re-typed variant. A line naming a Job Order the primary permit never
    /// delivered (the sub-customer's own job) has no primary row to take an item from, so the item must
    /// be that Job Order's own item: the item is never an independent free choice either way.
    ///
    /// An ordinary permit is untouched (it already reads its item from the ready lot it delivers).
    /// </summary>
    private static async Task<IReadOnlyList<string?>> ResolveSubPermitLineSourcesAsync(
        IApplicationDbContext db, CreateDeliveryCommand request, Delivery? primary, CancellationToken cancellationToken)
    {
        var resolvedColors = request.Lines.Select(_ => (string?)null).ToList();
        if (primary is null) return resolvedColors;

        var primaryLines = await db.DeliveryLines.AsNoTracking()
            .Where(l => l.DeliveryId == primary.Id)
            .Select(l => new { l.ProductionOrderId, l.ItemId, l.Color })
            .ToListAsync(cancellationToken);

        var orderIds = request.Lines.Select(l => l.ProductionOrderId).Distinct().ToList();
        var ordersById = await db.ProductionOrders.AsNoTracking()
            .Where(o => orderIds.Contains(o.Id))
            .ToDictionaryAsync(o => o.Id, cancellationToken);

        for (var index = 0; index < request.Lines.Count; index++)
        {
            var line = request.Lines[index];
            var linesOfThisOrder = primaryLines.Where(p => p.ProductionOrderId == line.ProductionOrderId).ToList();

            if (linesOfThisOrder.Count == 0)
            {
                // The sub-customer's OWN job order: the primary permit never delivered it, so there is
                // no primary item to inherit. The item is still not free input - it must be the item
                // that Job Order was raised for, which is the item of every ready lot of that job.
                if (!ordersById.TryGetValue(line.ProductionOrderId, out var ownOrder))
                    throw new NotFoundException("ProductionOrder", line.ProductionOrderId);

                if (ownOrder.ItemId != line.ItemId)
                    throw new DomainException(
                        $"Item on the sub-customer permit line does not belong to Job Order '{ownOrder.OrderNumber}': " +
                        "the item of a sub-customer permit is taken from the Job Order it slices, never chosen on its own.");

                continue;
            }

            if (!linesOfThisOrder.Any(p => p.ItemId == line.ItemId))
                throw new DomainException(
                    $"This item is not on the primary permit {primary.DeliveryNumber}, so a sub-customer permit " +
                    "cannot carry it: its item must be one of the items that permit delivered for this Job Order.");

            // No colour named: nothing is invented, so the line simply carries none.
            if (string.IsNullOrWhiteSpace(line.Color)) continue;

            var primaryColors = linesOfThisOrder
                .Where(p => p.ItemId == line.ItemId && !string.IsNullOrWhiteSpace(p.Color))
                .Select(p => p.Color!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var matchingColor = primaryColors
                .FirstOrDefault(c => string.Equals(c, line.Color!.Trim(), StringComparison.OrdinalIgnoreCase));

            if (matchingColor is null)
                throw new DomainException(
                    $"Colour '{line.Color}' is not on the primary permit {primary.DeliveryNumber} for this item, " +
                    (primaryColors.Count > 0
                        ? $"whose colours are: {string.Join(", ", primaryColors)}. "
                        : "which recorded no colour for this item. ") +
                    "A sub-customer permit takes its colour from the primary permit and never introduces a new one.");

            // Stored exactly as the primary permit spells it, so one colour never becomes two.
            resolvedColors[index] = matchingColor;
        }

        return resolvedColors;
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
        // A SUB-customer permit is a document link: it owns no physical movement, so it settles NONE of
        // the order's original raw material. Its figures are a share of the PRIMARY permit's, so they may
        // neither be validated against the order's fixed original quantity nor reduce what the primary
        // permit may still settle (that check belongs to the permit that actually delivered the goods).
        if (request.PrimaryDeliveryId.HasValue) return;

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

        // Only a permit that actually delivered stock settles the order's original raw. Sub-customer
        // permits are excluded so their figures cannot be counted a second time against the primary's.
        var subCustomerPermitIds = await db.Deliveries.AsNoTracking()
            .Where(d => d.PrimaryDeliveryId != null)
            .Select(d => d.Id)
            .ToListAsync(cancellationToken);

        var settled = await db.DeliveryLines.AsNoTracking()
            .Where(l => orderIds.Contains(l.ProductionOrderId)
                     && !cancelledDeliveryIds.Contains(l.DeliveryId)
                     && !subCustomerPermitIds.Contains(l.DeliveryId))
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
