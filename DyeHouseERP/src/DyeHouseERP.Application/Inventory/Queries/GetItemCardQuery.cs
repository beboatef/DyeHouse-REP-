using DyeHouseERP.Application.Common.Exceptions;
using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.Inventory.DTOs;
using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Enums;
using DyeHouseERP.Domain.Exceptions;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.Inventory.Queries;

/// <summary>
/// كارت الصنف لمخزن واحد (BE-3): opening balance, the period's movements, closing balance - read from
/// ONE warehouse's ledger.
///
/// The warehouse is a REQUIRED identity, not a filter applied on screen: every ledger row is selected
/// with that warehouse in the WHERE clause, so a card issued for WH-RAW can never contain a movement
/// that happened in another warehouse. The item (or the material, for the factory-owned ledger) is
/// equally required - a "card" without an item is a warehouse listing, which the balances screen
/// already provides.
///
/// Nothing is stored and no rule is re-derived: the opening balance is the signed sum of the rows
/// BEFORE the period, the closing balance is the opening plus the period's in/out, and every movement
/// carries the balance as it stood after it. KG and Meter are summed in separate columns and are never
/// converted into one another.
/// </summary>
public record GetItemCardQuery(
    Guid WarehouseId,
    Guid? ItemId = null,
    Guid? MaterialId = null,
    Guid? CustomerId = null,
    Guid? RawMessageId = null,
    Guid? ProductionOrderId = null,
    DateTime? From = null,
    DateTime? To = null) : IRequest<ItemCardDto>;

public class GetItemCardQueryValidator : AbstractValidator<GetItemCardQuery>
{
    public GetItemCardQueryValidator()
    {
        RuleFor(x => x.WarehouseId).NotEmpty();
        RuleFor(x => x).Must(x => !x.From.HasValue || !x.To.HasValue || x.From.Value.Date <= x.To.Value.Date)
            .WithMessage("The card's 'from' date cannot be after its 'to' date.");
    }
}

public class GetItemCardQueryHandler : IRequestHandler<GetItemCardQuery, ItemCardDto>
{
    private readonly IApplicationDbContext _db;
    public GetItemCardQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<ItemCardDto> Handle(GetItemCardQuery request, CancellationToken cancellationToken)
    {
        var warehouse = await _db.Warehouses.AsNoTracking()
            .FirstOrDefaultAsync(w => w.Id == request.WarehouseId, cancellationToken)
            ?? throw new NotFoundException("Warehouse", request.WarehouseId);

        var isMaterialsWarehouse = warehouse.Kind is WarehouseKind.Materials or WarehouseKind.OperatingSupplies;
        var isRaw = warehouse.Kind == WarehouseKind.RawMaterial;
        var isReady = warehouse.Kind == WarehouseKind.ReadyGoods;
        if (!isMaterialsWarehouse && !isRaw && !isReady)
            throw new DomainException(
                $"Warehouse '{warehouse.Code}' is not a stock warehouse: a card is issued only for raw material, ready goods, materials or operating supplies.");

        // Which identifiers name the card, and which ones are meaningless for this warehouse. A foreign
        // identifier is REFUSED rather than ignored: ignoring it would answer about something else.
        if (isMaterialsWarehouse)
        {
            if (request.MaterialId is null)
                throw new DomainException("A materials card names its material: materialId is required.");
            if (request.ItemId.HasValue || request.CustomerId.HasValue || request.RawMessageId.HasValue || request.ProductionOrderId.HasValue)
                throw new DomainException(
                    $"Warehouse '{warehouse.Code}' holds factory-owned materials: a card there is identified by (materialId, warehouseId), and fabric identifiers do not apply to it.");
        }
        else
        {
            if (request.ItemId is null)
                throw new DomainException("A fabric card names its item: itemId is required.");
            if (request.MaterialId.HasValue)
                throw new DomainException(
                    $"Warehouse '{warehouse.Code}' holds fabric, not materials: materialId does not apply to this card.");
        }

        // The period is half-open on purpose: [from 00:00, to+1 00:00), so every movement of the last
        // selected day is included exactly once and no row can fall in two periods.
        var fromStart = request.From?.Date;
        var toEnd = request.To?.Date.AddDays(1);

        return isMaterialsWarehouse
            ? await ReadMaterialsAsync(request, warehouse, fromStart, toEnd, cancellationToken)
            : await ReadFabricAsync(request, warehouse, isRaw, fromStart, toEnd, cancellationToken);
    }

    // ==================================================================== fabric ledger
    // Raw material and ready goods share the customer-owned fabric ledger; the warehouse kind decides
    // which half of it the card reads, exactly as the balances screen does.

    private async Task<ItemCardDto> ReadFabricAsync(
        GetItemCardQuery request, Warehouse warehouse, bool isRaw,
        DateTime? fromStart, DateTime? toEnd, CancellationToken cancellationToken)
    {
        var query = _db.InventoryTransactions.AsNoTracking().Where(t => t.WarehouseId == warehouse.Id);
        query = isRaw
            ? query.Where(t => t.RawMessageId != null)
            : query.Where(t => t.RawMessageId == null && t.ProductionOrderId != null);
        if (request.ItemId.HasValue) query = query.Where(t => t.ItemId == request.ItemId.Value);
        if (request.CustomerId.HasValue) query = query.Where(t => t.CustomerId == request.CustomerId.Value);
        if (request.RawMessageId.HasValue) query = query.Where(t => t.RawMessageId == request.RawMessageId.Value);
        if (request.ProductionOrderId.HasValue) query = query.Where(t => t.ProductionOrderId == request.ProductionOrderId.Value);

        var rows = await query.ToListAsync(cancellationToken);
        var item = await _db.Items.AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == request.ItemId!.Value, cancellationToken);

        // A unit the item is NOT configured for is never printed: a fabric item can be KG-only,
        // Meter-only or both, and a zero in a unit it does not have would read as a measurement.
        var hasKg = item is null || item.BaseUnit == UnitOfMeasure.KG || item.SecondaryUnit == UnitOfMeasure.KG;
        var hasMeter = item is not null &&
                       (item.BaseUnit == UnitOfMeasure.Meter || item.SecondaryUnit == UnitOfMeasure.Meter);
        var customer = request.CustomerId.HasValue
            ? await _db.Customers.AsNoTracking().Where(c => c.Id == request.CustomerId.Value)
                .Select(c => new { c.Code, c.Name }).FirstOrDefaultAsync(cancellationToken)
            : null;

        var orderIds = rows.Where(t => t.ProductionOrderId.HasValue).Select(t => t.ProductionOrderId!.Value).Distinct().ToList();
        var orders = orderIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _db.ProductionOrders.AsNoTracking().Where(o => orderIds.Contains(o.Id))
                .ToDictionaryAsync(o => o.Id, o => o.OrderNumber, cancellationToken);
        var messageIds = rows.Where(t => t.RawMessageId.HasValue).Select(t => t.RawMessageId!.Value).Distinct().ToList();
        var messages = messageIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _db.RawMessages.AsNoTracking().Where(m => messageIds.Contains(m.Id))
                .ToDictionaryAsync(m => m.Id, m => m.MessageNumber, cancellationToken);

        var ordered = rows.OrderBy(t => t.TransactionDate).ThenBy(t => t.CreatedAtUtc).ThenBy(t => t.Id).ToList();

        decimal openingKg = 0m, openingMeter = 0m;
        decimal inKg = 0m, inMeter = 0m, outKg = 0m, outMeter = 0m;
        // The running balance accumulates over EVERY row of this warehouse's card, including the rows
        // before the period: that is what makes the balance printed on a period row the true balance as
        // it stood then, not a balance that starts from zero on the first day of the report.
        decimal runningKg = 0m, runningMeter = 0m;
        var movements = new List<StockTraceMovementDto>();

        foreach (var t in ordered)
        {
            var direction = (int)t.Direction;
            var signedKg = (t.QuantityKg ?? 0m) * direction;
            var signedMeter = (t.QuantityMeter ?? 0m) * direction;

            var beforePeriod = fromStart.HasValue && t.TransactionDate < fromStart.Value;
            var afterPeriod = toEnd.HasValue && t.TransactionDate >= toEnd.Value;

            if (beforePeriod)
            {
                openingKg += signedKg;
                openingMeter += signedMeter;
            }

            runningKg += signedKg;
            runningMeter += signedMeter;

            if (beforePeriod || afterPeriod) continue;

            if (direction == (int)TransactionDirection.In)
            {
                inKg += signedKg;
                inMeter += signedMeter;
            }
            else
            {
                outKg += -signedKg;
                outMeter += -signedMeter;
            }

            movements.Add(new StockTraceMovementDto
            {
                Id = t.Id,
                Scope = isRaw ? StockScopes.Raw : StockScopes.Ready,
                // The card is (warehouse, item[, customer]) - its own key, built the same way the balance
                // screens build theirs, so a row of this card can never be matched to another warehouse.
                LotKey = $"C|{warehouse.Id}|{t.ItemId}|{t.CustomerId}",
                Date = t.TransactionDate,
                DocumentType = t.SourceDocumentType.ToString(),
                DocumentNumber = t.SourceDocumentNumber,
                DocumentId = t.SourceDocumentId,
                Kind = isRaw ? StockMovementKinds.Raw(t) : StockMovementKinds.Ready(t),
                Direction = t.Direction.ToString(),
                IsReversal = t.ReversesTransactionId.HasValue,
                RawMessageId = t.RawMessageId,
                MessageNumber = t.RawMessageId.HasValue ? messages.GetValueOrDefault(t.RawMessageId.Value) : null,
                ProductionOrderId = t.ProductionOrderId,
                OrderNumber = t.ProductionOrderId.HasValue ? orders.GetValueOrDefault(t.ProductionOrderId.Value) : null,
                ItemId = t.ItemId,
                ItemCode = item?.Code,
                ItemName = item?.Name,
                WarehouseId = warehouse.Id,
                WarehouseName = warehouse.Name,
                CustomerId = t.CustomerId,
                CustomerCode = customer?.Code,
                QuantityKg = t.QuantityKg,
                QuantityMeter = t.QuantityMeter,
                Unit = item is null ? null : DescribeUnit(item),
                BalanceAfterKg = hasKg ? runningKg : null,
                BalanceAfterMeter = hasMeter ? runningMeter : null,
                CreatedBy = t.CreatedBy,
                CreatedAtUtc = t.CreatedAtUtc
            });
        }

        // The closing balance is the opening plus this period's in and out. Rows AFTER the period are
        // deliberately excluded: a card for a closed period reports the period's own closing balance,
        // not a figure that keeps moving as new stock is posted afterwards.
        return new ItemCardDto
        {
            WarehouseId = warehouse.Id,
            WarehouseCode = warehouse.Code,
            WarehouseName = warehouse.Name,
            WarehouseKind = warehouse.Kind.ToString(),
            Scope = isRaw ? StockScopes.Raw : StockScopes.Ready,
            IsMaterials = false,
            ItemId = request.ItemId,
            ItemCode = item?.Code,
            ItemName = item?.Name,
            BaseUnit = item?.BaseUnit.ToString(),
            SecondaryUnit = item?.SecondaryUnit?.ToString(),
            CustomerId = request.CustomerId,
            CustomerCode = customer?.Code,
            CustomerName = customer?.Name,
            RawMessageId = request.RawMessageId,
            ProductionOrderId = request.ProductionOrderId,
            From = request.From,
            To = request.To,
            OpeningKg = hasKg ? openingKg : null,
            OpeningMeter = hasMeter ? openingMeter : null,
            InKg = hasKg ? inKg : null,
            InMeter = hasMeter ? inMeter : null,
            OutKg = hasKg ? outKg : null,
            OutMeter = hasMeter ? outMeter : null,
            ClosingKg = hasKg ? openingKg + inKg - outKg : null,
            ClosingMeter = hasMeter ? openingMeter + inMeter - outMeter : null,
            Movements = movements,
            GeneratedAtUtc = DateTime.UtcNow
        };
    }

    // ==================================================================== material ledger
    // Factory-owned stock keeps its OWN ledger and its OWN unit (KG / Gram / Liter) - never the fabric
    // KG/Meter pair, and never summed into another material's figure.

    private async Task<ItemCardDto> ReadMaterialsAsync(
        GetItemCardQuery request, Warehouse warehouse,
        DateTime? fromStart, DateTime? toEnd, CancellationToken cancellationToken)
    {
        var rows = await _db.MaterialTransactions.AsNoTracking()
            .Where(t => t.WarehouseId == warehouse.Id && t.MaterialId == request.MaterialId!.Value)
            .ToListAsync(cancellationToken);

        var material = await _db.Materials.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == request.MaterialId!.Value, cancellationToken);
        var orderIds = rows.Where(t => t.ProductionOrderId.HasValue).Select(t => t.ProductionOrderId!.Value).Distinct().ToList();
        var orders = orderIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _db.ProductionOrders.AsNoTracking().Where(o => orderIds.Contains(o.Id))
                .ToDictionaryAsync(o => o.Id, o => o.OrderNumber, cancellationToken);

        var ordered = rows.OrderBy(t => t.TransactionDate).ThenBy(t => t.CreatedAtUtc).ThenBy(t => t.Id).ToList();

        decimal opening = 0m, inQty = 0m, outQty = 0m, running = 0m;
        var movements = new List<StockTraceMovementDto>();

        foreach (var t in ordered)
        {
            var direction = (int)t.Direction;
            var signed = t.Quantity * direction;

            var beforePeriod = fromStart.HasValue && t.TransactionDate < fromStart.Value;
            var afterPeriod = toEnd.HasValue && t.TransactionDate >= toEnd.Value;

            if (beforePeriod) opening += signed;
            running += signed;

            if (beforePeriod || afterPeriod) continue;

            if (direction == (int)MaterialTransactionDirection.In) inQty += signed;
            else outQty += -signed;

            movements.Add(new StockTraceMovementDto
            {
                Id = t.Id,
                Scope = warehouse.Kind == WarehouseKind.OperatingSupplies ? StockScopes.Supplies : StockScopes.Materials,
                LotKey = $"C|{warehouse.Id}|{t.MaterialId}",
                Date = t.TransactionDate,
                DocumentType = t.SourceDocumentType.ToString(),
                DocumentNumber = t.SourceDocumentNumber,
                DocumentId = t.SourceDocumentId,
                Kind = StockMovementKinds.Material(t),
                Direction = t.Direction.ToString(),
                IsReversal = t.SourceDocumentType is DocumentType.MaterialIssue or DocumentType.SupplyIssue
                    && t.Direction == MaterialTransactionDirection.In,
                MaterialId = t.MaterialId,
                MaterialCode = material?.Code,
                ProductionOrderId = t.ProductionOrderId,
                OrderNumber = t.ProductionOrderId.HasValue ? orders.GetValueOrDefault(t.ProductionOrderId.Value) : null,
                WarehouseId = warehouse.Id,
                WarehouseName = warehouse.Name,
                Quantity = t.Quantity,
                Unit = material?.Unit.ToString(),
                BalanceAfter = running,
                CreatedBy = t.CreatedBy,
                CreatedAtUtc = t.CreatedAtUtc
            });
        }

        return new ItemCardDto
        {
            WarehouseId = warehouse.Id,
            WarehouseCode = warehouse.Code,
            WarehouseName = warehouse.Name,
            WarehouseKind = warehouse.Kind.ToString(),
            Scope = warehouse.Kind == WarehouseKind.OperatingSupplies ? StockScopes.Supplies : StockScopes.Materials,
            IsMaterials = true,
            MaterialId = request.MaterialId,
            MaterialCode = material?.Code,
            MaterialName = material?.Name,
            MaterialUnit = material?.Unit.ToString(),
            From = request.From,
            To = request.To,
            OpeningQuantity = opening,
            InQuantity = inQty,
            OutQuantity = outQty,
            ClosingQuantity = opening + inQty - outQty,
            Movements = movements,
            GeneratedAtUtc = DateTime.UtcNow
        };
    }

    /// <summary>
    /// The units an item is ACTUALLY configured for, as one label (e.g. "KG" or "KG/Meter"). Never a unit
    /// the item does not have, because a unit that was never configured cannot be recorded at all.
    /// </summary>
    private static string DescribeUnit(Item item) =>
        item.SecondaryUnit is null ? item.BaseUnit.ToString() : $"{item.BaseUnit}/{item.SecondaryUnit}";
}
