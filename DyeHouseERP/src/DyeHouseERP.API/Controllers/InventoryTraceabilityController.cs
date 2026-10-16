using System.Globalization;
using System.Text.RegularExpressions;
using DyeHouseERP.API.Authorization;
using DyeHouseERP.API.Common;
using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.Inventory.DTOs;
using DyeHouseERP.Application.Inventory.Queries;
using DyeHouseERP.Domain.Common;
using DyeHouseERP.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DyeHouseERP.API.Controllers;

/// <summary>
/// BALANCES WITH THEIR MOVEMENTS - one endpoint for the raw-material warehouse, the ready-goods
/// warehouse and the materials/chemicals warehouse (spec sections 10, 14, 18, 25, 30, 35, 48).
///
/// Balances are never stored anywhere in the system: they are summed live from the append-only ledger
/// at the moment the screen asks, which is what makes this response current by construction - there is
/// no cached figure that can go stale, and no bookkeeping row that a second user's posting could leave
/// behind. The filters are read-only: this endpoint never posts, corrects or reverses anything.
/// </summary>
[ApiController]
[Route("api/inventory")]
[Authorize]
public class InventoryTraceabilityController : ControllerBase
{
    private readonly ISender _mediator;
    public InventoryTraceabilityController(ISender mediator) => _mediator = mediator;

    /// <summary>
    /// <paramref name="scope"/> is raw | ready | materials. Identity filters (item, rawMessageId,
    /// productionOrderId, materialId, warehouseId, customerId) choose WHICH lots are shown;
    /// <paramref name="from"/>, <paramref name="to"/>, <paramref name="documentNumber"/> and
    /// <paramref name="kind"/> narrow only the MOVEMENT LIST, so a filtered screen can never report a
    /// balance that never existed.
    /// </summary>
    [HttpGet("traceability")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.InventoryView)]
    [ProducesResponseType(typeof(StockTraceabilityDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<StockTraceabilityDto>> GetTraceability(
        [FromQuery] string scope = "raw",
        [FromQuery] Guid? itemId = null,
        [FromQuery] Guid? materialId = null,
        [FromQuery] Guid? customerId = null,
        [FromQuery] Guid? rawMessageId = null,
        [FromQuery] Guid? productionOrderId = null,
        [FromQuery] Guid? warehouseId = null,
        [FromQuery] string? documentNumber = null,
        [FromQuery] string? kind = null,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] bool onlyWithBalance = false,
        [FromQuery] bool includeDelivered = true)
        => Ok(await _mediator.Send(new GetStockTraceabilityQuery(
            scope, itemId, materialId, customerId, rawMessageId, productionOrderId, warehouseId,
            documentNumber, kind, from, to, onlyWithBalance, includeDelivered)));

    /// <summary>
    /// The same balances and movements as an Excel file or a PDF, honouring the same filters - so what
    /// the screen shows is exactly what the report contains (spec section 39).
    /// </summary>
    [HttpGet("traceability/export")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ReportsExport)]
    public async Task<IActionResult> ExportTraceability(
        [FromQuery] string scope = "raw",
        [FromQuery] Guid? itemId = null,
        [FromQuery] Guid? materialId = null,
        [FromQuery] Guid? customerId = null,
        [FromQuery] Guid? rawMessageId = null,
        [FromQuery] Guid? productionOrderId = null,
        [FromQuery] Guid? warehouseId = null,
        [FromQuery] string? documentNumber = null,
        [FromQuery] string? kind = null,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] bool onlyWithBalance = false,
        [FromQuery] bool includeDelivered = true,
        [FromQuery] string format = "excel",
        [FromServices] IReportExportService export = null!)
    {
        var data = await _mediator.Send(new GetStockTraceabilityQuery(
            scope, itemId, materialId, customerId, rawMessageId, productionOrderId, warehouseId,
            documentNumber, kind, from, to, onlyWithBalance, includeDelivered));

        var isMaterials = data.Scope == StockScopes.Materials;

        var headers = isMaterials
            ? new List<string>
            {
                "Material", "Unit", "Kind", "Warehouse", "Supplier", "Purchase order",
                "Received", "Issued", "Returned", "Balance", "Movements"
            }
            : new List<string>
            {
                "Item", "Lot", "Job order", "Customer", "Warehouse",
                "Received KG", "Received M", "Issued KG", "Issued M", "Under production KG", "Under production M",
                "Returned KG", "Returned M", "Produced KG", "Produced M",
                "Delivered KG", "Delivered M", "Pieces", "Deliveries",
                "Balance KG", "Balance M", "Movements"
            };

        var rows = data.Rows.Select(r => isMaterials
            ? new object?[]
            {
                $"{r.MaterialCode} - {r.MaterialName}", r.MaterialUnit, r.MaterialKind, r.WarehouseName,
                r.SupplierName, r.PurchaseOrderNumber,
                r.ReceivedQuantity, r.IssuedToProductionQuantity, r.ReturnedQuantity, r.RemainingQuantity,
                r.MovementCount
            }
            : new object?[]
            {
                $"{r.ItemCode} - {r.ItemName}", r.MessageNumber ?? r.OrderNumber, r.OrderNumber,
                r.CustomerCode, r.WarehouseName,
                r.ReceivedKg, r.ReceivedMeter, r.IssuedToProductionKg, r.IssuedToProductionMeter,
                r.UnderProductionKg, r.UnderProductionMeter, r.ReturnedKg, r.ReturnedMeter,
                r.ProducedKg, r.ProducedMeter, r.DeliveredKg, r.DeliveredMeter,
                r.DeliveredPieceCount, r.DeliveryNumbers, r.RemainingKg, r.RemainingMeter, r.MovementCount
            }).ToList();

        var title = data.Scope switch
        {
            StockScopes.Ready => "أرصدة مخزن الجاهز",
            StockScopes.Materials => "أرصدة المواد والكيماويات",
            _ => "أرصدة مخزن الخامات"
        };

        return ExportFileHelper.ToFile(export, format, title, null, headers, rows, $"stock-{data.Scope}", "Stock");
    }

    /// <summary>
    /// The LOT TRACEABILITY CARD (بطاقة تتبع اللوط) as a PDF - the official document for ONE lot: its
    /// identity, its ledger movements and the CUMULATIVE balance the lot reached after each one.
    ///
    /// ONE LOT, NAMED BY THE CALLER. A lot is the unit its warehouse balances per - a raw receipt
    /// message TOGETHER WITH the item it carried, the job order AND item in the ready warehouse, a
    /// material IN a warehouse - so a request that does not resolve to exactly one of those is REFUSED
    /// rather than guessed at:
    ///   * no identifier for the scope, or an identifier that belongs to another warehouse's lot (a
    ///     material on a raw card, a job order on a receipt card) -> 400, and the identifier is never
    ///     silently dropped, because a dropped filter is how a caller downloads a card for a lot it
    ///     never asked for;
    ///   * an identifier that still matches several independent balances (one receipt message carrying
    ///     two items, one material stocked in two warehouses) -> 400, listing what is on file so the
    ///     caller can say WHICH one: the item (or the warehouse, for materials) is what makes the
    ///     identity unique;
    ///   * an identifier that matches nothing the caller may see -> 404.
    ///
    /// Picking the first row of whatever the filter happened to return - and then printing the whole
    /// result's balance next to that one row's item name - is exactly what this closes: a card could
    /// name item A while reporting the combined stock of items A and B, with no row on the page saying
    /// which movement belonged to which item.
    ///
    /// It renders the SAME read the screen renders (one query, the same rules) and then keeps ONLY the
    /// resolved lot's own movements and its OWN closing balance, so nothing is re-summed, re-derived or
    /// borrowed from a sibling lot: KG and Meter stay independent ("—" for a unit the lot never
    /// recorded), every movement row repeats the lot's item so no row can be read against another
    /// article, and the closing balance is that lot's live ledger total. Arabic renders through the
    /// embedded Noto Naskh Arabic font the report service already registers.
    ///
    /// READ-ONLY: generating the card posts nothing and moves no stock. Only the lot's IDENTITY is
    /// accepted - the date / kind / document-number filters are deliberately not, because they shorten
    /// the MOVEMENT LIST without changing the balance, and a card whose rows did not add up to its own
    /// closing balance would be a false document.
    /// </summary>
    [HttpGet("traceability/lot-card")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.InventoryView)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ExportLotCard(
        [FromQuery] string scope = StockScopes.Raw,
        [FromQuery] Guid? rawMessageId = null,
        [FromQuery] Guid? productionOrderId = null,
        [FromQuery] Guid? materialId = null,
        [FromQuery] Guid? itemId = null,
        [FromQuery] Guid? warehouseId = null,
        [FromQuery] Guid? customerId = null,
        [FromServices] IReportExportService export = null!)
    {
        var resolvedScope = (scope ?? string.Empty).Trim().ToLowerInvariant();
        if (resolvedScope is not (StockScopes.Raw or StockScopes.Ready or StockScopes.Materials))
            return BadRequest(new
            {
                error = "INVALID_SCOPE",
                message = $"A lot lives in one of three warehouses: {StockScopes.Raw}, {StockScopes.Ready} or {StockScopes.Materials}."
            });

        // Which identifiers NAME a lot in this warehouse, and which ones are meaningless for it. An
        // identifier the scope does not use is refused instead of ignored: the query would drop it and
        // answer about some other lot entirely.
        var identifies = resolvedScope switch
        {
            StockScopes.Raw => new[] { "rawMessageId", "itemId", "customerId" },
            StockScopes.Ready => new[] { "productionOrderId", "itemId" },
            _ => new[] { "materialId", "warehouseId" }
        };
        var supplied = new (string Name, Guid? Value)[]
        {
            ("rawMessageId", rawMessageId), ("productionOrderId", productionOrderId), ("materialId", materialId),
            ("itemId", itemId), ("warehouseId", warehouseId), ("customerId", customerId)
        };
        var conflicting = supplied.Where(x => x.Value.HasValue && !identifies.Contains(x.Name)).Select(x => x.Name).ToList();
        if (conflicting.Count > 0)
            return BadRequest(new
            {
                error = "IDENTIFIER_SCOPE_MISMATCH",
                message = $"A {resolvedScope} lot is not identified by: {string.Join(", ", conflicting)}.",
                allowed = identifies
            });

        var requiredName = resolvedScope switch
        {
            StockScopes.Raw => "rawMessageId",
            StockScopes.Ready => "productionOrderId",
            _ => "materialId"
        };
        if (supplied.Single(x => x.Name == requiredName).Value is null)
            return BadRequest(new
            {
                error = "LOT_IDENTIFIER_REQUIRED",
                message = $"A {resolvedScope} lot is named by {requiredName}; without it there is no lot to print.",
                allowed = identifies
            });

        // The same permission that guards the traceability screen guards this file: a user who cannot see
        // the lot's data cannot download it either. A lot the caller may not see simply does not exist
        // for them, which is why an out-of-reach lot answers 404 rather than disclosing that it exists.
        var data = await _mediator.Send(new GetStockTraceabilityQuery(
            Scope: resolvedScope, ItemId: itemId, MaterialId: materialId, CustomerId: customerId,
            RawMessageId: rawMessageId, ProductionOrderId: productionOrderId, WarehouseId: warehouseId,
            OnlyWithBalance: false, IncludeDelivered: true));

        if (data.Rows.Count == 0)
            return NotFound(new
            {
                error = "LOT_NOT_FOUND",
                message = "No lot matches this identifier, or you do not have access to its data."
            });

        // More than one independent balance still answers to this identifier. Printing the first one
        // would be a card for a lot the caller did not name, so the request is refused with the list of
        // lots on file - each with the disambiguator that picks it.
        if (data.Rows.Count > 1)
            return BadRequest(new
            {
                error = "AMBIGUOUS_LOT",
                message = "This identifier matches more than one independent lot; name the one you want.",
                disambiguateBy = resolvedScope == StockScopes.Materials ? "warehouseId" : "itemId",
                lots = data.Rows.Select(r => new
                {
                    lotKey = r.LotKey,
                    itemCode = r.ItemCode,
                    itemName = r.ItemName,
                    materialCode = r.MaterialCode,
                    materialName = r.MaterialName,
                    warehouseId = r.WarehouseId,
                    warehouseName = r.WarehouseName,
                    remainingKg = r.RemainingKg,
                    remainingMeter = r.RemainingMeter,
                    remainingQuantity = r.RemainingQuantity
                })
            });

        // ONE lot from here on: its own identity, its own movements, its own closing balance.
        var lot = data.Rows[0];
        var isMaterials = data.Scope == StockScopes.Materials;

        string Number(decimal? value) =>
            value is null ? "—" : value.Value.ToString("#,0.###", CultureInfo.InvariantCulture);

        string JoinUnits(string kg, string? meter) => meter is null ? kg : $"{kg} / {meter}";

        // Chronological (oldest first), the order a running-balance ledger is read in, so the cumulative
        // column accumulates down the page. The figures are the query's own - nothing is recomputed. The
        // movements are matched to THIS lot by the lot key the query stamps on the row and on every one
        // of its movements, so a sibling lot's rows - another item of the same receipt message - can
        // never appear on this card, and the running balance printed on a row is this lot's own.
        var movements = data.Movements
            .Where(m => m.LotKey == lot.LotKey)
            .OrderBy(m => m.Date).ThenBy(m => m.CreatedAtUtc).ThenBy(m => m.Id)
            .ToList();

        var hasMeter = movements.Any(m => m.QuantityMeter is not null || m.BalanceAfterMeter is not null)
            || lot.RemainingMeter is not null;

        // A unit is shown for a movement only when THAT movement recorded it ("—" otherwise), exactly as
        // the on-screen card does: printing a meter the row never had would read as a real measurement.
        string QuantityCell(StockTraceMovementDto m) => isMaterials
            ? $"{Number(m.Quantity)} {m.Unit ?? string.Empty}".Trim()
            : JoinUnits(Number(m.QuantityKg), hasMeter && m.QuantityMeter is not null ? Number(m.QuantityMeter) : null);

        string BalanceCell(StockTraceMovementDto m) => isMaterials
            ? Number(m.BalanceAfter)
            : JoinUnits(Number(m.BalanceAfterKg), hasMeter ? Number(m.BalanceAfterMeter) : null);

        // Every movement row repeats the lot's own item/material, so a row printed on paper cannot be
        // read against a different article than the one on the card's header.
        string ItemCell(StockTraceMovementDto m) => isMaterials
            ? (m.MaterialCode ?? lot.MaterialCode ?? "—")
            : $"{m.ItemCode ?? lot.ItemCode} - {m.ItemName ?? lot.ItemName}".Trim(' ', '-');

        var headers = new List<string>
        {
            "التاريخ", "نوع الحركة", isMaterials ? "المادة" : "الصنف", "المستند", "الاتجاه",
            isMaterials ? "الكمية" : "الكمية (كجم / متر)",
            "الرصيد التراكمي", "المستخدم"
        };

        var rows = movements.Select(m => new object?[]
        {
            m.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            MovementKindLabels.TryGetValue(m.Kind, out var label) ? label : m.Kind,
            ItemCell(m),
            m.DocumentNumber,
            m.Direction == "Out" ? "صادر" : "وارد",
            QuantityCell(m),
            BalanceCell(m),
            m.CreatedBy
        }).ToList();

        var lotNumber = lot.MessageNumber ?? lot.OrderNumber ?? lot.MaterialCode ?? "—";
        var scopeLabel = data.Scope switch
        {
            StockScopes.Ready => "مخزن الجاهز",
            StockScopes.Materials => "مخزن المواد والكيماويات",
            _ => "مخزن الخامات"
        };
        var itemLabel = isMaterials
            ? $"{lot.MaterialCode} - {lot.MaterialName}"
            : $"{lot.ItemCode} - {lot.ItemName}";

        // The closing balance is THIS lot's own live ledger balance - the same figure the movements above
        // add up to - never a total of everything the filter happened to return.
        var closingBalance = isMaterials
            ? Number(lot.RemainingQuantity)
            : JoinUnits(Number(lot.RemainingKg), hasMeter ? Number(lot.RemainingMeter) : null);

        var subtitle = string.Join("   |   ", new[]
        {
            $"المخزن: {scopeLabel}",
            isMaterials ? $"المادة: {itemLabel}" : $"الصنف: {itemLabel}",
            isMaterials ? null : $"العميل: {lot.CustomerCode} - {lot.CustomerName}",
            $"اللوط: {lotNumber}",
            string.IsNullOrWhiteSpace(lot.WarehouseName) ? null : $"مخزن الرصيد: {lot.WarehouseName}",
            $"الحالة: {lot.OrderStatus ?? lot.MessageStatus ?? lot.MaterialKind ?? "—"}",
            $"الرصيد الختامي: {closingBalance}",
            $"عدد الحركات: {movements.Count.ToString(CultureInfo.InvariantCulture)}",
            $"تاريخ الطباعة: {DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)}"
        }.Where(part => !string.IsNullOrWhiteSpace(part)));

        return ExportFileHelper.ToPdf(export, "بطاقة تتبع اللوط", subtitle, headers, rows,
            $"lot-card-{SafeFileStem(lotNumber)}");
    }

    /// <summary>
    /// Arabic labels for the ledger's fixed movement vocabulary, so the printed card reads in Arabic.
    /// Presentation only: the value stored on the movement is never translated, and an unknown kind
    /// falls back to its own name rather than being hidden.
    /// </summary>
    private static readonly Dictionary<string, string> MovementKindLabels = new(StringComparer.Ordinal)
    {
        ["Received"] = "استلام",
        ["Issued"] = "صرف للتشغيل",
        ["IssuedExternal"] = "صرف خارجي / إفراج",
        ["Returned"] = "مرتجع",
        ["TransferredIn"] = "تحويل وارد",
        ["TransferredOut"] = "تحويل صادر",
        ["AdjustedIn"] = "تسوية بالزيادة",
        ["AdjustedOut"] = "تسوية بالنقص",
        ["Produced"] = "إنتاج",
        ["Delivered"] = "تسليم للعميل",
        ["Reversal"] = "عكس حركة",
        ["In"] = "وارد",
        ["Out"] = "صادر"
    };

    /// <summary>
    /// A file-name-safe stem for the lot number: only ASCII letters, digits, '.', '-' and '_' survive
    /// (anything else - an Arabic character or a path separator - becomes '-'), runs collapse and the
    /// ends are trimmed, so the download name can never break a response header or the filesystem. The
    /// lot number itself is untouched and is printed inside the document.
    /// </summary>
    private static string SafeFileStem(string value)
    {
        var cleaned = new string(value.Select(ch =>
            char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_' or '.' ? ch : '-').ToArray());
        cleaned = Regex.Replace(cleaned, "-{2,}", "-").Trim('-', '.', ' ');
        return cleaned.Length == 0 ? "lot" : cleaned;
    }
}
