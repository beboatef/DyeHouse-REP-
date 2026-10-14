using DyeHouseERP.Application.Common.Exceptions;
using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Domain.Enums;
using DyeHouseERP.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.Common.Services;

/// <summary>
/// The ONE gate an invoice line passes through before it is written (spec sections
/// 20, 31-32, 34).
///
/// It exists to keep three promises the finance side makes:
///
///   1. **The billing unit follows the SERVICE PRICE, not the production unit.** A price
///      is configured per stage + unit, so a job can be measured in KG on the floor and
///      billed per Meter. The line may therefore only be billed in a unit the order
///      actually has a configured price for - never an arbitrary unit the caller picked.
///
///   2. **The quantity is the APPROVED quantity IN THAT UNIT - never a conversion.**
///      The approved quantity per unit is the quantity the order's non-cancelled
///      deliveries record for that SAME unit (KG and Meter are independent columns, per
///      spec sections 5-6). The system never converts KG into Meter: if the order was
///      produced in KG but the service is priced per Meter and no Meter quantity was ever
///      approved, billing in Meter is refused rather than manufactured.
///
///   3. **Only a cost EXPLICITLY flagged chargeable can be billed, and only once.**
///      Reprocessing/separate costs are never auto-added; a line may bill a
///      <c>CostEntry</c> only when it carries <c>IsChargeableToCustomer</c>, and the same
///      cost entry may never be billed by two live invoice lines.
///
/// It is deliberately a server-side helper rather than a validator: every rule reads
/// persisted data (prices, deliveries, cost entries), which a validator that does not
/// touch the database cannot enforce.
/// </summary>
public static class InvoiceBillingGuard
{
    private const decimal Tolerance = 0.001m;

    /// <summary>
    /// Validates one prospective invoice line. <paramref name="excludeInvoiceLineId"/> is
    /// the line being EDITED (its own previous figures are excluded from the
    /// already-billed/already-used sums); pass null when appending a new line.
    /// </summary>
    public static async Task EnsureBillableAsync(
        IApplicationDbContext db, Guid? productionOrderId, Guid itemId, UnitOfMeasure unit,
        decimal quantity, Guid? costEntryId, Guid? excludeInvoiceLineId, CancellationToken cancellationToken)
    {
        if (quantity <= 0)
            throw new DomainException("Invoice line quantity must be greater than zero.");

        if (productionOrderId.HasValue)
            await EnsureServiceBillingAsync(db, productionOrderId.Value, unit, quantity, excludeInvoiceLineId, cancellationToken);

        if (costEntryId.HasValue)
            await EnsureChargeableCostAsync(db, costEntryId.Value, excludeInvoiceLineId, cancellationToken);

        _ = itemId; // kept in the signature so callers pass the full line context; no per-item rule applies here
    }

    private static async Task EnsureServiceBillingAsync(
        IApplicationDbContext db, Guid productionOrderId, UnitOfMeasure unit, decimal quantity,
        Guid? excludeInvoiceLineId, CancellationToken cancellationToken)
    {
        var order = await db.ProductionOrders.AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == productionOrderId, cancellationToken)
            ?? throw new NotFoundException("ProductionOrder", productionOrderId);

        // 1. Billing unit follows the configured service price.
        var pricedForUnit = await db.ProductionOrderServicePrices.AsNoTracking()
            .AnyAsync(s => s.ProductionOrderId == productionOrderId && s.Unit == unit, cancellationToken);

        if (!pricedForUnit)
            throw new DomainException(
                $"Job Order {order.OrderNumber} has no configured service price in {unit}, so it cannot be billed in that unit. " +
                "Set the price for the billed unit on the order (spec section 34) - the billing unit follows the service price.");

        // 2. The approved quantity IN THIS UNIT: what the order's APPROVED deliveries recorded for
        //    that same unit. A delivery is approved once it is Prepared or Delivered - a Draft permit
        //    is still working data and a Cancelled one never happened, so neither is billable. KG and
        //    Meter are separate columns and are never converted into one another.
        var approvedDeliveryIds = await db.Deliveries.AsNoTracking()
            .Where(d => d.Status == DeliveryStatus.Prepared || d.Status == DeliveryStatus.Delivered)
            .Select(d => d.Id)
            .ToListAsync(cancellationToken);

        var deliveredLines = await db.DeliveryLines.AsNoTracking()
            .Where(l => l.ProductionOrderId == productionOrderId && approvedDeliveryIds.Contains(l.DeliveryId))
            .Select(l => new { l.QuantityKg, l.QuantityMeter })
            .ToListAsync(cancellationToken);

        var approvedInUnit = unit == UnitOfMeasure.KG
            ? deliveredLines.Sum(l => l.QuantityKg ?? 0m)
            : deliveredLines.Sum(l => l.QuantityMeter ?? 0m);

        if (approvedInUnit <= 0m)
            throw new DomainException(
                $"Job Order {order.OrderNumber} has no approved {unit} quantity, so there is nothing to bill in {unit}. " +
                "The system never converts KG into Meter - record the approved quantity in that unit first.");

        // 3. What is already billed for this order in this unit, across every invoice that
        //    is not cancelled and is not the line being edited.
        var cancelledInvoiceIds = await db.Invoices.AsNoTracking()
            .Where(i => i.Status == InvoiceStatus.Cancelled)
            .Select(i => i.Id)
            .ToListAsync(cancellationToken);

        var alreadyBilled = await db.InvoiceLines.AsNoTracking()
            .Where(l => l.ProductionOrderId == productionOrderId && l.Unit == unit
                        && !cancelledInvoiceIds.Contains(l.InvoiceId)
                        && (excludeInvoiceLineId == null || l.Id != excludeInvoiceLineId.Value))
            .Select(l => (decimal?)l.Quantity)
            .SumAsync(cancellationToken) ?? 0m;

        if (alreadyBilled + quantity > approvedInUnit + Tolerance)
            throw new DomainException(
                $"Billing {quantity} {unit} for Job Order {order.OrderNumber} would exceed its approved {unit} quantity of " +
                $"{approvedInUnit} (already billed {alreadyBilled}). Bill the approved quantity in that unit - the system " +
                "never converts KG into Meter or bills the same quantity twice.");
    }

    private static async Task EnsureChargeableCostAsync(
        IApplicationDbContext db, Guid costEntryId, Guid? excludeInvoiceLineId, CancellationToken cancellationToken)
    {
        var costEntry = await db.CostEntries.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == costEntryId, cancellationToken)
            ?? throw new NotFoundException("CostEntry", costEntryId);

        if (!costEntry.IsChargeableToCustomer)
            throw new DomainException(
                "This cost is not flagged chargeable to the customer, so it cannot be billed. " +
                "Reprocessing and separate costs are company-borne unless they are explicitly marked chargeable (spec section 20).");

        var alreadyBilled = await db.InvoiceLines.AsNoTracking()
            .AnyAsync(l => l.CostEntryId == costEntryId
                           && (excludeInvoiceLineId == null || l.Id != excludeInvoiceLineId.Value), cancellationToken);

        if (alreadyBilled)
            throw new DomainException(
                "This cost has already been billed on another invoice line. A cost is billable once - it is never counted twice.");
    }
}
