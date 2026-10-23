using DyeHouseERP.Application.Common.Exceptions;
using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.Inventory.DTOs;
using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.Inventory.Queries;

/// <summary>The three warehouses the same traceability principle is applied to.</summary>
public static class StockScopes
{
    public const string Raw = "raw";
    public const string Ready = "ready";
    public const string Materials = "materials";
    public const string Supplies = "supplies";
}

/// <summary>
/// BALANCES WITH THE MOVEMENTS BEHIND THEM - one query for all four stock classes.
///
/// Why it exists: a balance on its own is not traceable. This returns, for the SAME filter:
///   * one row per LOT - a raw lot is a receipt message, a ready lot is a job order, a materials or supplies lot
///     is a (material, warehouse) pair - with its received / issued / returned / produced / delivered /
///     under-production / remaining figures, each in its OWN unit;
///   * the ledger rows behind those figures, each with the balance AS IT STOOD after it;
///   * the totals of the visible rows, per unit (KG and Meter are never merged into one number).
///
/// Two rules make the figures trustworthy:
///   1. NOTHING IS STORED. Every balance is a live sum over the append-only ledger, and the rows and
///      the movements are built from the SAME loaded rows, so the total can never disagree with the
///      movements it is made of.
///   2. A BALANCE IS NEVER WINDOWED. Date / document-number / movement-kind filters narrow the
///      MOVEMENT LIST only; the balances stay complete, because a balance filtered to "last month"
///      would report a stock level that never existed. Only the identity filters (item, lot, order,
///      warehouse, customer, material) select which lots are shown.
/// </summary>
public record GetStockTraceabilityQuery(
    string Scope = StockScopes.Raw,
    Guid? ItemId = null,
    Guid? MaterialId = null,
    Guid? CustomerId = null,
    Guid? RawMessageId = null,
    Guid? ProductionOrderId = null,
    Guid? WarehouseId = null,
    string? DocumentNumber = null,
    string? Kind = null,
    DateTime? From = null,
    DateTime? To = null,
    /// <summary>Hide lots whose balance is already zero (the ordinary "what is in stock" view).</summary>
    bool OnlyWithBalance = false,
    /// <summary>Keep lots that were delivered and are now empty - they still carry the delivery history.</summary>
    bool IncludeDelivered = true) : IRequest<StockTraceabilityDto>;

public class GetStockTraceabilityQueryHandler : IRequestHandler<GetStockTraceabilityQuery, StockTraceabilityDto>
{
    private readonly IApplicationDbContext _db;
    public GetStockTraceabilityQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<StockTraceabilityDto> Handle(GetStockTraceabilityQuery request, CancellationToken cancellationToken)
    {
        var scope = (request.Scope ?? StockScopes.Raw).Trim().ToLowerInvariant();
        return scope switch
        {
            StockScopes.Ready => await ReadFabricAsync(request, StockScopes.Ready, cancellationToken),
            StockScopes.Materials => await ReadMaterialsAsync(request, cancellationToken, operatingSupplies: false),
            StockScopes.Supplies => await ReadMaterialsAsync(request, cancellationToken, operatingSupplies: true),
            _ => await ReadFabricAsync(request, StockScopes.Raw, cancellationToken),
        };
    }

    // ================================================================= raw material & ready goods
    // Both live in the SAME inventory ledger; they differ only in which dimension identifies the lot
    // (RawMessageId for raw, ProductionOrderId for ready) and in what the quantities mean.

    private async Task<StockTraceabilityDto> ReadFabricAsync(
        GetStockTraceabilityQuery request, string scope, CancellationToken cancellationToken)
    {
        var isRaw = scope == StockScopes.Raw;

        var query = _db.InventoryTransactions.AsNoTracking().AsQueryable();
        query = isRaw
            ? query.Where(t => t.RawMessageId != null)
            : query.Where(t => t.RawMessageId == null && t.ProductionOrderId != null);

        if (request.ItemId.HasValue) query = query.Where(t => t.ItemId == request.ItemId.Value);
        if (request.CustomerId.HasValue) query = query.Where(t => t.CustomerId == request.CustomerId.Value);
        if (request.WarehouseId.HasValue) query = query.Where(t => t.WarehouseId == request.WarehouseId.Value);
        if (isRaw && request.RawMessageId.HasValue) query = query.Where(t => t.RawMessageId == request.RawMessageId.Value);
        if (!isRaw && request.ProductionOrderId.HasValue) query = query.Where(t => t.ProductionOrderId == request.ProductionOrderId.Value);

        var rows = await query.ToListAsync(cancellationToken);
        if (rows.Count == 0) return Empty(scope);


        // From here everything is assembled in memory from these exact rows: the balances, the running
        // balances and the totals all come from one list, so they cannot drift apart.
        var itemIds = rows.Select(t => t.ItemId).Distinct().ToList();
        var customerIds = rows.Select(t => t.CustomerId).Distinct().ToList();
        var warehouseIds = rows.Select(t => t.WarehouseId).Distinct().ToList();
        var messageIds = rows.Where(t => t.RawMessageId.HasValue).Select(t => t.RawMessageId!.Value).Distinct().ToList();
        var orderIds = rows.Where(t => t.ProductionOrderId.HasValue).Select(t => t.ProductionOrderId!.Value).Distinct().ToList();
        var deliveryDocIds = rows.Where(t => t.SourceDocumentType == DocumentType.Delivery)
            .Select(t => t.SourceDocumentId).Distinct().ToList();

        var items = await _db.Items.AsNoTracking().Where(i => itemIds.Contains(i.Id))
            .ToDictionaryAsync(i => i.Id, cancellationToken);
        var customers = await _db.Customers.AsNoTracking().Where(c => customerIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => (c.Code, c.Name), cancellationToken);
        // Code AND name: the code is the stable identity a machine-readable label (such as a download
        // file name) needs, the name is what a user reads.
        var warehouses = await _db.Warehouses.AsNoTracking().Where(w => warehouseIds.Contains(w.Id))
            .ToDictionaryAsync(w => w.Id, w => (w.Code, w.Name), cancellationToken);
        var messages = messageIds.Count == 0 ? new Dictionary<Guid, RawMessage>()
            : await _db.RawMessages.AsNoTracking().Where(m => messageIds.Contains(m.Id)).ToDictionaryAsync(m => m.Id, cancellationToken);
        var orders = orderIds.Count == 0 ? new Dictionary<Guid, ProductionOrder>()
            : await _db.ProductionOrders.AsNoTracking().Where(o => orderIds.Contains(o.Id)).ToDictionaryAsync(o => o.Id, cancellationToken);

        // Delivery DOCUMENTS were posted against these lots: their numbers, dates and piece counts come
        // from the documents themselves (the ledger row carries the id, not the number).
        var deliveries = deliveryDocIds.Count == 0 ? new Dictionary<Guid, Delivery>()
            : await _db.Deliveries.AsNoTracking().Where(d => deliveryDocIds.Contains(d.Id)).ToDictionaryAsync(d => d.Id, cancellationToken);
        var deliveryLines = deliveryDocIds.Count == 0 ? new List<DeliveryLine>()
            : await _db.DeliveryLines.AsNoTracking().Where(l => deliveryDocIds.Contains(l.DeliveryId)).ToListAsync(cancellationToken);

        // Produced quantities per (job order, item): the ready-goods IN rows. Read for the same orders so
        // a raw lot can report how much of what it issued has already come back out as finished fabric.
        var producedByOrderItem = await _db.InventoryTransactions.AsNoTracking()
            .Where(t => t.RawMessageId == null && t.ProductionOrderId != null && orderIds.Contains(t.ProductionOrderId!.Value)
                        && t.SourceDocumentType == DocumentType.ReadyGoodsTransfer && t.Direction == TransactionDirection.In)
            .Select(t => new { OrderId = t.ProductionOrderId!.Value, t.ItemId, t.QuantityKg, t.QuantityMeter })
            .ToListAsync(cancellationToken);

        var readyTransfersByOrder = orderIds.Count == 0
            ? new List<ReadyGoodsTransfer>()
            : await _db.ReadyGoodsTransfers.AsNoTracking()
                .Where(rt => orderIds.Contains(rt.ProductionOrderId) && rt.Status != ReadyGoodsTransferStatus.Cancelled)
                .ToListAsync(cancellationToken);

        // When in raw scope, a receipt lot is linked to Job Orders that were subsequently delivered. When a
        // Job Order sourced its raw material from ONLY this lot (unambiguous 1:1 raw-message-to-order link),
        // the delivered permit lines of that order belong deterministically to this receipt message.
        var rawDeliveredLines = new List<DeliveryLine>();
        var rawDeliveriesById = new Dictionary<Guid, Delivery>();
        var singleMessageByOrderId = new Dictionary<Guid, Guid>();
        if (isRaw && orderIds.Count > 0)
        {
            var allocationsForOrders = await _db.InventoryTransactions.AsNoTracking()
                .Where(t => t.RawMessageId != null && t.ProductionOrderId != null && orderIds.Contains(t.ProductionOrderId!.Value))
                .Select(t => new { OrderId = t.ProductionOrderId!.Value, MessageId = t.RawMessageId!.Value })
                .Distinct()
                .ToListAsync(cancellationToken);

            foreach (var grp in allocationsForOrders.GroupBy(a => a.OrderId))
            {
                var distinctMessages = grp.Select(x => x.MessageId).Distinct().ToList();
                if (distinctMessages.Count == 1)
                    singleMessageByOrderId[grp.Key] = distinctMessages[0];
            }

            if (singleMessageByOrderId.Count > 0)
            {
                var singleOrderIds = singleMessageByOrderId.Keys.ToList();
                var rawLines = await _db.DeliveryLines.AsNoTracking()
                    .Where(l => singleOrderIds.Contains(l.ProductionOrderId))
                    .ToListAsync(cancellationToken);
                var rawDelIds = rawLines.Select(l => l.DeliveryId).Distinct().ToList();
                if (rawDelIds.Count > 0)
                {
                    rawDeliveriesById = await _db.Deliveries.AsNoTracking()
                        .Where(d => rawDelIds.Contains(d.Id) && d.Status == DeliveryStatus.Delivered && d.PrimaryDeliveryId == null)
                        .ToDictionaryAsync(d => d.Id, cancellationToken);
                    rawDeliveredLines = rawLines.Where(l => rawDeliveriesById.ContainsKey(l.DeliveryId)).ToList();
                }
            }
        }

        // ---- classify every row, then build the movement list with its running balance ----------
        // The identity of a lot is computed in exactly ONE place and stamped on both the balance row and
        // every movement of it, so the two can never disagree about which lot a movement belongs to.
        string LotKeyOf(InventoryTransaction t) => isRaw
            ? $"R|{t.RawMessageId}|{t.ItemId}|{t.CustomerId}"
            : $"D|{t.ProductionOrderId}|{t.ItemId}";

        var ordered = rows.OrderBy(t => t.TransactionDate).ThenBy(t => t.CreatedAtUtc).ThenBy(t => t.Id).ToList();
        var running = new Dictionary<string, (decimal Kg, decimal Meter)>();
        var movements = new List<(StockTraceMovementDto Dto, string LotKey)>();

        foreach (var t in ordered)
        {
            var lotKey = LotKeyOf(t);

            running.TryGetValue(lotKey, out var sofar);
            var sign = (int)t.Direction;
            var kg = (t.QuantityKg ?? 0m) * sign;
            var meter = (t.QuantityMeter ?? 0m) * sign;
            var after = (Kg: sofar.Kg + kg, Meter: sofar.Meter + meter);
            running[lotKey] = after;

            var kind = isRaw ? ClassifyRaw(t) : ClassifyReady(t);
            var isReversal = t.ReversesTransactionId.HasValue;

            // Hidden-outbound filter by movement class.
            var requestedKind = (request.Kind ?? string.Empty).Trim().ToLowerInvariant();
            var kindMatches = requestedKind.Length == 0
                || kind.ToLowerInvariant() == requestedKind
                || (requestedKind == "issued" && kind is "Issued" or "IssuedExternal")
                || (requestedKind == "returned" && kind == "Returned")
                || (requestedKind == "reversal" && isReversal);

            var documentNumberMatches = string.IsNullOrWhiteSpace(request.DocumentNumber)
                || t.SourceDocumentNumber.Contains(request.DocumentNumber.Trim(), StringComparison.OrdinalIgnoreCase);

            var withinWindow = (!request.From.HasValue || t.TransactionDate >= request.From.Value.Date)
                && (!request.To.HasValue || t.TransactionDate < request.To.Value.Date.AddDays(1));

            if (!kindMatches || !documentNumberMatches || !withinWindow) continue;

            items.TryGetValue(t.ItemId, out var item);
            var message = t.RawMessageId.HasValue ? messages.GetValueOrDefault(t.RawMessageId.Value) : null;
            var order = t.ProductionOrderId.HasValue ? orders.GetValueOrDefault(t.ProductionOrderId.Value) : null;
            var customer = customers.TryGetValue(t.CustomerId, out var c) ? c : (Code: "", Name: "");

            movements.Add((new StockTraceMovementDto
            {
                Id = t.Id,
                Scope = scope,
                LotKey = lotKey,
                Date = t.TransactionDate,
                DocumentType = t.SourceDocumentType.ToString(),
                DocumentNumber = t.SourceDocumentNumber,
                DocumentId = t.SourceDocumentId,
                Kind = kind,
                Direction = t.Direction.ToString(),
                IsReversal = isReversal,
                RawMessageId = t.RawMessageId,
                MessageNumber = message?.MessageNumber,
                ProductionOrderId = t.ProductionOrderId,
                OrderNumber = order?.OrderNumber,
                ItemId = t.ItemId,
                ItemCode = item?.Code,
                ItemName = item?.Name,
                WarehouseId = t.WarehouseId,
                WarehouseName = warehouses.GetValueOrDefault(t.WarehouseId).Name,
                CustomerId = t.CustomerId,
                CustomerCode = customer.Code,
                DeliveryId = t.SourceDocumentType == DocumentType.Delivery ? t.SourceDocumentId.ToString() : null,
                QuantityKg = t.QuantityKg,
                QuantityMeter = t.QuantityMeter,
                Unit = item is null ? null : DescribeUnit(item),
                BalanceAfterKg = item?.BaseUnit == UnitOfMeasure.KG || item?.SecondaryUnit == UnitOfMeasure.KG
                    ? after.Kg : (decimal?)null,
                BalanceAfterMeter = item?.BaseUnit == UnitOfMeasure.Meter || item?.SecondaryUnit == UnitOfMeasure.Meter
                    ? after.Meter : (decimal?)null,
                CreatedBy = t.CreatedBy,
                CreatedAtUtc = t.CreatedAtUtc,
                SupplierName = null,
                PurchaseOrderNumber = null
            }, lotKey));
        }

        // ---- one balance row per lot, aggregated from the SAME ledger rows ----------------------
        var rowList = new List<StockTraceRowDto>();

        foreach (var lot in ordered.GroupBy(LotKeyOf))
        {
            var any = lot.First();
            items.TryGetValue(any.ItemId, out var item);
            var message = any.RawMessageId.HasValue ? messages.GetValueOrDefault(any.RawMessageId.Value) : null;
            var order = any.ProductionOrderId.HasValue ? orders.GetValueOrDefault(any.ProductionOrderId.Value) : null;
            var customer = customers.TryGetValue(any.CustomerId, out var c) ? c : (Code: "", Name: "");

            decimal? receivedKg = null, receivedMeter = null;
            decimal? issuedKg = null, issuedMeter = null;
            decimal? returnedKg = null, returnedMeter = null;
            decimal? producedKg = null, producedMeter = null;
            decimal? deliveredKg = null, deliveredMeter = null;
            decimal? remainingKg = null, remainingMeter = null;

            static void Add(ref decimal? acc, decimal? value)
            {
                if (value is null) return;
                acc = (acc ?? 0m) + value.Value;
            }

            var lotDeliveryIds = new HashSet<Guid>();

            foreach (var t in lot)
            {
                var signedKg = (t.QuantityKg ?? 0m) * (int)t.Direction;
                var signedMeter = (t.QuantityMeter ?? 0m) * (int)t.Direction;
                remainingKg = (remainingKg ?? 0m) + signedKg;
                remainingMeter = (remainingMeter ?? 0m) + signedMeter;

                switch (isRaw ? ClassifyRaw(t) : ClassifyReady(t))
                {
                    case "Received":
                        Add(ref receivedKg, t.QuantityKg);
                        Add(ref receivedMeter, t.QuantityMeter);
                        break;
                    case "Issued":
                    case "Produced":
                        Add(ref issuedKg, t.QuantityKg);
                        Add(ref issuedMeter, t.QuantityMeter);
                        break;
                    case "Returned":
                        Add(ref returnedKg, t.QuantityKg);
                        Add(ref returnedMeter, t.QuantityMeter);
                        break;
                    case "Delivered":
                        // On the ready ledger, deliveries are OUT (-1) and their reversals are IN (+1):
                        // the net delivered figure is a positive hand-over amount, so OUT adds and IN subtracts.
                        Add(ref deliveredKg, -signedKg);
                        Add(ref deliveredMeter, -signedMeter);
                        if (t.SourceDocumentType == DocumentType.Delivery && t.Direction == TransactionDirection.Out)
                            lotDeliveryIds.Add(t.SourceDocumentId);
                        break;
                    case "Reversal":
                        if (t.SourceDocumentType == DocumentType.Delivery)
                        {
                            Add(ref deliveredKg, -signedKg);
                            Add(ref deliveredMeter, -signedMeter);
                        }
                        break;
                }
            }

            int? producedPieces = null;
            if (!isRaw && order is not null)
            {
                var produced = producedByOrderItem.Where(p => p.OrderId == order.Id && p.ItemId == any.ItemId).ToList();
                if (produced.Count > 0)
                {
                    if (produced.Any(p => p.QuantityKg.HasValue))
                        producedKg = produced.Sum(p => p.QuantityKg ?? 0m);
                    if (produced.Any(p => p.QuantityMeter.HasValue))
                        producedMeter = produced.Sum(p => p.QuantityMeter ?? 0m);
                }

                var transfersHere = readyTransfersByOrder.Where(rt => rt.ProductionOrderId == order.Id && rt.ItemId == any.ItemId && rt.PieceCount.HasValue).ToList();
                if (transfersHere.Count > 0)
                    producedPieces = transfersHere.Sum(rt => rt.PieceCount!.Value);
            }

            // Under production (raw only): material issued to job orders that are STILL OPEN and has not
            // come back. A completed or cancelled order no longer holds material, so it leaves this figure.
            decimal? underKg = null, underMeter = null;
            if (isRaw)
            {
                foreach (var orderLot in lot.Where(t => t.ProductionOrderId.HasValue)
                             .GroupBy(t => t.ProductionOrderId!.Value))
                {
                    var linked = orders.GetValueOrDefault(orderLot.Key);
                    if (linked is null ||
                        linked.Status is ProductionOrderStatus.Completed or ProductionOrderStatus.Cancelled or ProductionOrderStatus.Draft)
                        continue;

                    var issuedHere = orderLot.Where(t => ClassifyRaw(t) is "Issued")
                        .Sum(t => t.QuantityKg ?? 0m);
                    var returnedHere = orderLot.Where(t => ClassifyRaw(t) is "Returned")
                        .Sum(t => t.QuantityKg ?? 0m);
                    var issuedMeterHere = orderLot.Where(t => ClassifyRaw(t) is "Issued")
                        .Sum(t => t.QuantityMeter ?? 0m);
                    var returnedMeterHere = orderLot.Where(t => ClassifyRaw(t) is "Returned")
                        .Sum(t => t.QuantityMeter ?? 0m);

                    var netKg = issuedHere - returnedHere;
                    var netMeter = issuedMeterHere - returnedMeterHere;
                    // Only a genuinely positive remainder counts: a job that returned everything it was
                    // issued holds nothing, and a negative figure would be a data problem, not stock.
                    if (netKg > 0) Add(ref underKg, netKg);
                    if (netMeter > 0) Add(ref underMeter, netMeter);
                }
            }

            var lotOrderNumbers = lot.Where(t => t.ProductionOrderId.HasValue)
                .Select(t => orders.GetValueOrDefault(t.ProductionOrderId!.Value)?.OrderNumber)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct()
                .ToList();

            var deliveryDocs = lotDeliveryIds
                .Select(id => deliveries.GetValueOrDefault(id))
                .Where(d => d is not null && d.Status != DeliveryStatus.Cancelled)
                .ToList();
            var activeDeliveryDocIds = deliveryDocs.Select(d => d!.Id).ToHashSet();
            var pieceCount = deliveryLines
                .Where(l => activeDeliveryDocIds.Contains(l.DeliveryId) && l.ItemId == any.ItemId && (!any.ProductionOrderId.HasValue || l.ProductionOrderId == any.ProductionOrderId.Value) && l.PieceCount.HasValue)
                .Select(l => l.PieceCount!.Value)
                .DefaultIfEmpty()
                .Sum();
            int? finalPieceCount = deliveryLines.Any(l => activeDeliveryDocIds.Contains(l.DeliveryId) && l.ItemId == any.ItemId && l.PieceCount.HasValue)
                ? pieceCount : null;

            // For a raw receipt message, populate delivered quantities/permits from the orders that sourced
            // exclusively from this receipt message (never guessing across multi-message orders).
            if (isRaw && any.RawMessageId.HasValue)
            {
                var deterministicallyLinkedOrders = lot.Where(t => t.ProductionOrderId.HasValue)
                    .Select(t => t.ProductionOrderId!.Value)
                    .Distinct()
                    .Where(oid => singleMessageByOrderId.TryGetValue(oid, out var mid) && mid == any.RawMessageId.Value)
                    .ToHashSet();

                if (deterministicallyLinkedOrders.Count > 0)
                {
                    var matchingLines = rawDeliveredLines
                        .Where(l => deterministicallyLinkedOrders.Contains(l.ProductionOrderId) && l.ItemId == any.ItemId)
                        .ToList();
                    if (matchingLines.Count > 0)
                    {
                        if (matchingLines.Any(l => l.QuantityKg.HasValue))
                            deliveredKg = matchingLines.Sum(l => l.QuantityKg ?? 0m);
                        if (matchingLines.Any(l => l.QuantityMeter.HasValue))
                            deliveredMeter = matchingLines.Sum(l => l.QuantityMeter ?? 0m);
                        if (matchingLines.Any(l => l.PieceCount.HasValue))
                            finalPieceCount = matchingLines.Sum(l => l.PieceCount ?? 0);

                        var rawDocs = matchingLines.Select(l => rawDeliveriesById.GetValueOrDefault(l.DeliveryId))
                            .Where(d => d is not null)
                            .DistinctBy(d => d!.Id)
                            .ToList();
                        deliveryDocs = rawDocs!;
                    }
                }
            }

            if (deliveredKg is <= 0m) deliveredKg = null;
            if (deliveredMeter is <= 0m) deliveredMeter = null;

            rowList.Add(new StockTraceRowDto
            {
                Scope = scope,
                LotKey = LotKeyOf(any),
                ItemId = any.ItemId,
                ItemCode = item?.Code ?? string.Empty,
                ItemName = item?.Name ?? string.Empty,
                BaseUnit = item?.BaseUnit.ToString() ?? string.Empty,
                SecondaryUnit = item?.SecondaryUnit?.ToString(),
                RawMessageId = any.RawMessageId,
                MessageNumber = message?.MessageNumber,
                MessageDate = message?.ReceiptDate,
                MessageStatus = message?.Status.ToString(),
                ProductionOrderId = any.ProductionOrderId,
                OrderNumber = order?.OrderNumber ?? (lotOrderNumbers.Count == 1 ? lotOrderNumbers[0] : null),
                OrderNumbers = lotOrderNumbers.Count == 0 ? null : string.Join(", ", lotOrderNumbers),
                OrderDate = order?.OrderDate,
                OrderStatus = order?.Status.ToString(),
                Color = order?.Color,
                ProducedPieceCount = producedPieces,
                WarehouseId = any.WarehouseId,
                WarehouseName = warehouses.GetValueOrDefault(any.WarehouseId).Name,
                WarehouseCode = warehouses.GetValueOrDefault(any.WarehouseId).Code,
                CustomerId = any.CustomerId,
                CustomerCode = customer.Code,
                CustomerName = customer.Name,
                ReceivedKg = receivedKg,
                ReceivedMeter = receivedMeter,
                IssuedToProductionKg = issuedKg,
                IssuedToProductionMeter = issuedMeter,
                UnderProductionKg = underKg,
                UnderProductionMeter = underMeter,
                ReturnedKg = returnedKg,
                ReturnedMeter = returnedMeter,
                ProducedKg = producedKg,
                ProducedMeter = producedMeter,
                DeliveredKg = deliveredKg,
                DeliveredMeter = deliveredMeter,
                DeliveredPieceCount = finalPieceCount,
                DeliveryNumbers = deliveryDocs.Count == 0 ? null
                    : string.Join(", ", deliveryDocs.Select(d => d!.DeliveryNumber).Distinct()),
                LastDeliveryDate = deliveryDocs.Count == 0 ? null : deliveryDocs.Max(d => d!.DeliveryDate),
                RemainingKg = item?.BaseUnit == UnitOfMeasure.KG || item?.SecondaryUnit == UnitOfMeasure.KG ? remainingKg : null,
                RemainingMeter = item?.BaseUnit == UnitOfMeasure.Meter || item?.SecondaryUnit == UnitOfMeasure.Meter ? remainingMeter : null,
                MovementCount = lot.Count()
            });
        }

        // Which lots are SHOWN. Only an identity filter or "balance only" removes a lot; the balances
        // themselves are never narrowed by the movement-side filters.
        if (request.OnlyWithBalance || !request.IncludeDelivered)
            rowList = rowList.Where(r => (r.RemainingKg ?? 0m) != 0m || (r.RemainingMeter ?? 0m) != 0m).ToList();

        rowList = rowList
            .OrderByDescending(r => r.MessageDate ?? r.OrderDate ?? DateTime.MinValue)
            .ThenBy(r => r.ItemCode)
            .ToList();

        var visibleLotKeys = rowList.Select(r => r.LotKey).ToHashSet();

        var visibleMovements = movements
            .Where(m => visibleLotKeys.Contains(m.LotKey))
            .Select(m => m.Dto)
            .OrderByDescending(m => m.Date)
            .ThenByDescending(m => m.CreatedAtUtc)
            .ToList();

        return new StockTraceabilityDto
        {
            Scope = scope,
            Rows = rowList,
            Movements = visibleMovements,
            TotalRemainingKg = rowList.Any(r => r.RemainingKg.HasValue) ? rowList.Sum(r => r.RemainingKg ?? 0m) : null,
            TotalRemainingMeter = rowList.Any(r => r.RemainingMeter.HasValue) ? rowList.Sum(r => r.RemainingMeter ?? 0m) : null,
            TotalUnderProductionKg = rowList.Any(r => r.UnderProductionKg.HasValue) ? rowList.Sum(r => r.UnderProductionKg ?? 0m) : null,
            TotalUnderProductionMeter = rowList.Any(r => r.UnderProductionMeter.HasValue) ? rowList.Sum(r => r.UnderProductionMeter ?? 0m) : null,
            TotalReceivedKg = rowList.Any(r => r.ReceivedKg.HasValue) ? rowList.Sum(r => r.ReceivedKg ?? 0m) : null,
            TotalReceivedMeter = rowList.Any(r => r.ReceivedMeter.HasValue) ? rowList.Sum(r => r.ReceivedMeter ?? 0m) : null,
            TotalIssuedKg = rowList.Any(r => r.IssuedToProductionKg.HasValue) ? rowList.Sum(r => r.IssuedToProductionKg ?? 0m) : null,
            TotalIssuedMeter = rowList.Any(r => r.IssuedToProductionMeter.HasValue) ? rowList.Sum(r => r.IssuedToProductionMeter ?? 0m) : null,
            TotalReturnedKg = rowList.Any(r => r.ReturnedKg.HasValue) ? rowList.Sum(r => r.ReturnedKg ?? 0m) : null,
            TotalReturnedMeter = rowList.Any(r => r.ReturnedMeter.HasValue) ? rowList.Sum(r => r.ReturnedMeter ?? 0m) : null,
            TotalProducedKg = rowList.Any(r => r.ProducedKg.HasValue) ? rowList.Sum(r => r.ProducedKg ?? 0m) : null,
            TotalProducedMeter = rowList.Any(r => r.ProducedMeter.HasValue) ? rowList.Sum(r => r.ProducedMeter ?? 0m) : null,
            TotalDeliveredKg = rowList.Any(r => r.DeliveredKg.HasValue) ? rowList.Sum(r => r.DeliveredKg ?? 0m) : null,
            TotalDeliveredMeter = rowList.Any(r => r.DeliveredMeter.HasValue) ? rowList.Sum(r => r.DeliveredMeter ?? 0m) : null,
            MovementCount = visibleMovements.Count,
            GeneratedAtUtc = DateTime.UtcNow
        };
    }

    // ================================================================= materials / chemicals
    // Materials keep their OWN append-only ledger (factory-owned stock, no customer dimension), and
    // their OWN unit (KG / Gram / Liter) - never forced into the fabric KG/Meter pair.

    private async Task<StockTraceabilityDto> ReadMaterialsAsync(
        GetStockTraceabilityQuery request, CancellationToken cancellationToken, bool operatingSupplies)
    {
        var scope = operatingSupplies ? StockScopes.Supplies : StockScopes.Materials;
        var query = _db.MaterialTransactions.AsNoTracking().AsQueryable();
        // Keep factory-owned stock classes in separate traceability scopes. Filter by the material's
        // explicit kind (not its name or warehouse label), so historic transactions remain visible
        // under the correct class without moving or rewriting any ledger rows.
        query = operatingSupplies
            ? query.Where(t => _db.Materials.Any(m => m.Id == t.MaterialId && m.Kind == MaterialKind.OperatingSupply))
            : query.Where(t => _db.Materials.Any(m => m.Id == t.MaterialId && m.Kind == MaterialKind.Chemical));
        if (request.MaterialId.HasValue) query = query.Where(t => t.MaterialId == request.MaterialId.Value);
        if (request.WarehouseId.HasValue) query = query.Where(t => t.WarehouseId == request.WarehouseId.Value);
        if (request.ProductionOrderId.HasValue) query = query.Where(t => t.ProductionOrderId == request.ProductionOrderId.Value);

        var rows = await query.ToListAsync(cancellationToken);
        if (rows.Count == 0) return Empty(scope);

        var materialIds = rows.Select(t => t.MaterialId).Distinct().ToList();
        var warehouseIds = rows.Select(t => t.WarehouseId).Distinct().ToList();
        var orderIds = rows.Where(t => t.ProductionOrderId.HasValue).Select(t => t.ProductionOrderId!.Value).Distinct().ToList();
        var receiptIds = rows.Where(t => t.SourceDocumentType == DocumentType.PurchaseReceipt)
            .Select(t => t.SourceDocumentId).Distinct().ToList();

        var materials = await _db.Materials.AsNoTracking().Where(m => materialIds.Contains(m.Id))
            .ToDictionaryAsync(m => m.Id, cancellationToken);
        var warehouses = await _db.Warehouses.AsNoTracking().Where(w => warehouseIds.Contains(w.Id))
            .ToDictionaryAsync(w => w.Id, w => (w.Code, w.Name), cancellationToken);
        var orders = orderIds.Count == 0 ? new Dictionary<Guid, ProductionOrder>()
            : await _db.ProductionOrders.AsNoTracking().Where(o => orderIds.Contains(o.Id)).ToDictionaryAsync(o => o.Id, cancellationToken);

        // Purchase documents: the supplier and the purchase order a receipt was booked against, so the
        // materials screen can answer "where did this stock come from?" without a second screen.
        var receipts = receiptIds.Count == 0 ? new List<PurchaseReceipt>()
            : await _db.PurchaseReceipts.AsNoTracking().Where(r => receiptIds.Contains(r.Id)).ToListAsync(cancellationToken);
        var supplierIds = receipts.Select(r => r.SupplierId).Distinct().ToList();
        var suppliers = supplierIds.Count == 0 ? new Dictionary<Guid, string>()
            : await _db.Suppliers.AsNoTracking().Where(s => supplierIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.Name, cancellationToken);
        var purchaseOrderIds = receipts.Where(r => r.PurchaseOrderId.HasValue).Select(r => r.PurchaseOrderId!.Value).Distinct().ToList();
        var purchaseOrders = purchaseOrderIds.Count == 0 ? new Dictionary<Guid, string>()
            : await _db.PurchaseOrders.AsNoTracking().Where(p => purchaseOrderIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.OrderNumber, cancellationToken);

        // A materials lot is a (material, warehouse) pair, computed in ONE place and stamped on the row
        // and on every movement of it - so two warehouses holding the same material are two lots whose
        // movements are never mixed.
        string LotKeyOfMaterial(MaterialTransaction t) => $"M|{t.MaterialId}|{t.WarehouseId}";

        var ordered = rows.OrderBy(t => t.TransactionDate).ThenBy(t => t.CreatedAtUtc).ThenBy(t => t.Id).ToList();
        var running = new Dictionary<string, decimal>();
        var movements = new List<(StockTraceMovementDto Dto, string LotKey)>();

        var requestedKind = (request.Kind ?? string.Empty).Trim().ToLowerInvariant();

        foreach (var t in ordered)
        {
            var lotKey = LotKeyOfMaterial(t);
            running.TryGetValue(lotKey, out var sofar);
            var signed = t.Quantity * (int)t.Direction;
            var after = sofar + signed;
            running[lotKey] = after;

            var kind = ClassifyMaterial(t);
            var isReversal = t.SourceDocumentType is DocumentType.MaterialIssue or DocumentType.SupplyIssue && t.Direction == MaterialTransactionDirection.In;

            var kindMatches = requestedKind.Length == 0
                || kind.ToLowerInvariant() == requestedKind
                || (requestedKind == "issued" && kind == "Issued");

            var documentNumberMatches = string.IsNullOrWhiteSpace(request.DocumentNumber)
                || t.SourceDocumentNumber.Contains(request.DocumentNumber.Trim(), StringComparison.OrdinalIgnoreCase);

            var withinWindow = (!request.From.HasValue || t.TransactionDate >= request.From.Value.Date)
                && (!request.To.HasValue || t.TransactionDate < request.To.Value.Date.AddDays(1));

            if (!kindMatches || !documentNumberMatches || !withinWindow) continue;

            var material = materials.GetValueOrDefault(t.MaterialId);
            var order = t.ProductionOrderId.HasValue ? orders.GetValueOrDefault(t.ProductionOrderId.Value) : null;
            var receipt = t.SourceDocumentType == DocumentType.PurchaseReceipt
                ? receipts.FirstOrDefault(r => r.Id == t.SourceDocumentId) : null;

            movements.Add((new StockTraceMovementDto
            {
                Id = t.Id,
                Scope = scope,
                LotKey = lotKey,
                Date = t.TransactionDate,
                DocumentType = t.SourceDocumentType.ToString(),
                DocumentNumber = t.SourceDocumentNumber,
                DocumentId = t.SourceDocumentId,
                Kind = kind,
                Direction = t.Direction.ToString(),
                IsReversal = isReversal,
                MaterialId = t.MaterialId,
                MaterialCode = material?.Code,
                ProductionOrderId = t.ProductionOrderId,
                OrderNumber = order?.OrderNumber,
                WarehouseId = t.WarehouseId,
                WarehouseName = warehouses.GetValueOrDefault(t.WarehouseId).Name,
                Quantity = t.Quantity,
                Unit = material?.Unit.ToString(),
                BalanceAfter = after,
                CreatedBy = t.CreatedBy,
                CreatedAtUtc = t.CreatedAtUtc,
                SupplierName = receipt is null ? null : suppliers.GetValueOrDefault(receipt.SupplierId),
                PurchaseOrderNumber = receipt?.PurchaseOrderId is null ? null
                    : purchaseOrders.GetValueOrDefault(receipt.PurchaseOrderId.Value)
            }, lotKey));
        }

        var rowList = new List<StockTraceRowDto>();
        foreach (var lot in ordered.GroupBy(LotKeyOfMaterial))
        {
            var any = lot.First();
            var material = materials.GetValueOrDefault(any.MaterialId);
            var lastReceipt = lot.Where(t => t.SourceDocumentType == DocumentType.PurchaseReceipt)
                .OrderByDescending(t => t.TransactionDate).FirstOrDefault();
            var lastReceiptDoc = lastReceipt is null ? null : receipts.FirstOrDefault(r => r.Id == lastReceipt.SourceDocumentId);

            decimal? received = null, issued = null, returned = null;
            void Add(ref decimal? acc, decimal value) => acc = (acc ?? 0m) + value;

            foreach (var t in lot)
            {
                switch (ClassifyMaterial(t))
                {
                    case "Received": Add(ref received, t.Quantity); break;
                    case "Issued": Add(ref issued, t.Quantity); break;
                    case "Returned": Add(ref returned, t.Quantity); break;
                }
            }

            var balance = lot.Sum(t => t.Quantity * (int)t.Direction);
            var materialOrderNumbers = lot.Where(t => t.ProductionOrderId.HasValue)
                .Select(t => orders.GetValueOrDefault(t.ProductionOrderId!.Value)?.OrderNumber)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct()
                .ToList();

            rowList.Add(new StockTraceRowDto
            {
                Scope = scope,
                LotKey = LotKeyOfMaterial(any),
                ItemId = Guid.Empty, // materials are not fabric items - the material identity below is theirs
                MaterialId = any.MaterialId,
                MaterialCode = material?.Code,
                MaterialName = material?.Name,
                MaterialUnit = material?.Unit.ToString(),
                MaterialKind = material?.Kind.ToString(),
                OrderNumber = materialOrderNumbers.Count == 1 ? materialOrderNumbers[0] : null,
                OrderNumbers = materialOrderNumbers.Count == 0 ? null : string.Join(", ", materialOrderNumbers),
                ReceivedQuantity = received,
                IssuedToProductionQuantity = issued,
                ReturnedQuantity = returned,
                RemainingQuantity = balance,
                WarehouseId = any.WarehouseId,
                WarehouseName = warehouses.GetValueOrDefault(any.WarehouseId).Name,
                WarehouseCode = warehouses.GetValueOrDefault(any.WarehouseId).Code,
                SupplierName = lastReceiptDoc is null ? null : suppliers.GetValueOrDefault(lastReceiptDoc.SupplierId),
                PurchaseOrderNumber = lastReceiptDoc?.PurchaseOrderId is null ? null
                    : purchaseOrders.GetValueOrDefault(lastReceiptDoc.PurchaseOrderId.Value),
                MovementCount = lot.Count()
            });
        }

        if (request.OnlyWithBalance)
            rowList = rowList.Where(r => (r.RemainingQuantity ?? 0m) != 0m).ToList();

        rowList = rowList.OrderBy(r => r.MaterialCode).ThenBy(r => r.WarehouseName).ToList();
        var visibleLotKeys = rowList.Select(r => r.LotKey).ToHashSet();
        var visibleMovements = movements.Where(m => visibleLotKeys.Contains(m.LotKey)).Select(m => m.Dto)
            .OrderByDescending(m => m.Date).ThenByDescending(m => m.CreatedAtUtc).ToList();

        return new StockTraceabilityDto
        {
            Scope = scope,
            Rows = rowList,
            Movements = visibleMovements,
            TotalRemainingQuantity = rowList.Any(r => r.RemainingQuantity.HasValue) ? rowList.Sum(r => r.RemainingQuantity ?? 0m) : null,
            TotalReceivedQuantity = rowList.Any(r => r.ReceivedQuantity.HasValue) ? rowList.Sum(r => r.ReceivedQuantity ?? 0m) : null,
            TotalIssuedQuantity = rowList.Any(r => r.IssuedToProductionQuantity.HasValue) ? rowList.Sum(r => r.IssuedToProductionQuantity ?? 0m) : null,
            TotalReturnedQuantity = rowList.Any(r => r.ReturnedQuantity.HasValue) ? rowList.Sum(r => r.ReturnedQuantity ?? 0m) : null,
            MovementCount = visibleMovements.Count,
            GeneratedAtUtc = DateTime.UtcNow
        };
    }

    // ================================================================= helpers

    /// <summary>
    /// What a raw-material ledger row MEANS. The classification is the movement's own document type and
    /// direction - never a screen's assumption - and a compensating row stays a reversal of its original.
    /// </summary>
    private static string ClassifyRaw(InventoryTransaction t) => t.SourceDocumentType switch
    {
        DocumentType.RawReceiptMessage => t.Direction == TransactionDirection.In ? "Received" : "Returned",
        DocumentType.ProductionOrder => t.Direction == TransactionDirection.Out ? "Issued" : "Returned",
        DocumentType.RawIssueExternalRelease => t.Direction == TransactionDirection.Out ? "IssuedExternal" : "Returned",
        DocumentType.CustomerReturn => "Returned",
        DocumentType.ReadyGoodsRawReturn => "Returned",
        DocumentType.ProductionOrderPause => "Returned",
        DocumentType.CustomerTransfer => t.Direction == TransactionDirection.In ? "TransferredIn" : "TransferredOut",
        DocumentType.StockAdjustment => t.Direction == TransactionDirection.In ? "AdjustedIn" : "AdjustedOut",
        _ => t.Direction == TransactionDirection.In ? "In" : "Out"
    };

    private static string ClassifyReady(InventoryTransaction t) => t.SourceDocumentType switch
    {
        DocumentType.ReadyGoodsTransfer => t.Direction == TransactionDirection.In ? "Produced" : "TransferredOut",
        DocumentType.Delivery => t.Direction == TransactionDirection.Out ? "Delivered" : "Reversal",
        DocumentType.ReadyGoodsRawReturn => "Returned",
        DocumentType.StockAdjustment => t.Direction == TransactionDirection.In ? "AdjustedIn" : "AdjustedOut",
        _ => t.Direction == TransactionDirection.In ? "In" : "Out"
    };

    private static string ClassifyMaterial(MaterialTransaction t) => t.SourceDocumentType switch
    {
        DocumentType.PurchaseReceipt => "Received",
        DocumentType.MaterialIssue => t.Direction == MaterialTransactionDirection.Out ? "Issued" : "Returned",
        DocumentType.SupplyIssue => t.Direction == MaterialTransactionDirection.Out ? "Issued" : "Returned",
        DocumentType.MaterialSale => t.Direction == MaterialTransactionDirection.Out ? "Issued" : "Returned",
        DocumentType.MaterialTransfer => t.Direction == MaterialTransactionDirection.Out ? "TransferredOut" : "TransferredIn",
        DocumentType.PreparationDilution => t.Direction == MaterialTransactionDirection.Out ? "Issued" : "Received",
        _ => t.Direction == MaterialTransactionDirection.In ? "Received" : "Issued"
    };

    /// <summary>
    /// The units an item is ACTUALLY configured for, as one label (e.g. "KG" or "KG/Meter"). Never a
    /// unit the item does not have, because a unit that was never configured cannot be recorded at all.
    /// </summary>
    private static string DescribeUnit(Item item) =>
        item.SecondaryUnit is null ? item.BaseUnit.ToString() : $"{item.BaseUnit}/{item.SecondaryUnit}";

    private static StockTraceabilityDto Empty(string scope) => new()
    {
        Scope = scope,
        GeneratedAtUtc = DateTime.UtcNow
    };
}
