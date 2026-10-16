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
    /// It renders the SAME read the screen renders (one query, the same rules), so nothing is re-summed
    /// or re-derived here and the file can never disagree with the screen: KG and Meter stay independent
    /// ("—" for a unit the lot never recorded), the rows are the lot's own ledger movements, and the
    /// closing balance is the same live total. Arabic renders through the embedded Noto Naskh Arabic
    /// font the report service already registers, so the document is readable on any host.
    ///
    /// READ-ONLY: generating the card posts nothing and moves no stock. Only the lot's IDENTITY is
    /// accepted - the date / kind / document-number filters are deliberately not, because they shorten
    /// the MOVEMENT LIST without changing the balance, and a card whose rows did not add up to its own
    /// closing balance would be a false document.
    /// </summary>
    [HttpGet("traceability/lot-card")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.InventoryView)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> ExportLotCard(
        [FromQuery] string scope = "raw",
        [FromQuery] Guid? rawMessageId = null,
        [FromQuery] Guid? productionOrderId = null,
        [FromQuery] Guid? materialId = null,
        [FromServices] IReportExportService export = null!)
    {
        // The same permission that guards the traceability screen guards this file: a user who cannot see
        // the lot's data cannot download it either.
        var data = await _mediator.Send(new GetStockTraceabilityQuery(
            Scope: scope, MaterialId: materialId, RawMessageId: rawMessageId,
            ProductionOrderId: productionOrderId, OnlyWithBalance: false, IncludeDelivered: true));

        var lot = data.Rows.FirstOrDefault();
        if (lot is null)
            return NotFound(new
            {
                error = "LOT_NOT_FOUND",
                message = "No lot matches this identifier, or you do not have access to its data."
            });

        var isMaterials = data.Scope == StockScopes.Materials;

        string Number(decimal? value) =>
            value is null ? "—" : value.Value.ToString("#,0.###", CultureInfo.InvariantCulture);

        string JoinUnits(string kg, string? meter) => meter is null ? kg : $"{kg} / {meter}";

        // Chronological (oldest first), the order a running-balance ledger is read in, so the cumulative
        // column accumulates down the page. The figures are the query's own - nothing is recomputed.
        var movements = data.Movements
            .OrderBy(m => m.Date).ThenBy(m => m.CreatedAtUtc).ThenBy(m => m.Id)
            .ToList();

        var hasMeter = movements.Any(m => m.QuantityMeter is not null || m.BalanceAfterMeter is not null)
            || data.Rows.Any(r => r.RemainingMeter is not null);

        // A unit is shown for a movement only when THAT movement recorded it ("—" otherwise), exactly as
        // the on-screen card does: printing a meter the row never had would read as a real measurement.
        string QuantityCell(StockTraceMovementDto m) => isMaterials
            ? $"{Number(m.Quantity)} {m.Unit ?? string.Empty}".Trim()
            : JoinUnits(Number(m.QuantityKg), hasMeter && m.QuantityMeter is not null ? Number(m.QuantityMeter) : null);

        string BalanceCell(StockTraceMovementDto m) => isMaterials
            ? Number(m.BalanceAfter)
            : JoinUnits(Number(m.BalanceAfterKg), hasMeter ? Number(m.BalanceAfterMeter) : null);

        var headers = new List<string>
        {
            "التاريخ", "نوع الحركة", "المستند", "الاتجاه",
            isMaterials ? "الكمية" : "الكمية (كجم / متر)",
            "الرصيد التراكمي", "المستخدم"
        };

        var rows = movements.Select(m => new object?[]
        {
            m.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            MovementKindLabels.TryGetValue(m.Kind, out var label) ? label : m.Kind,
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
        var closingBalance = isMaterials
            ? Number(data.TotalRemainingQuantity)
            : JoinUnits(Number(data.TotalRemainingKg), hasMeter ? Number(data.TotalRemainingMeter) : null);

        var subtitle = string.Join("   |   ", new[]
        {
            $"المخزن: {scopeLabel}",
            isMaterials ? $"المادة: {itemLabel}" : $"الصنف: {itemLabel}",
            isMaterials ? null : $"العميل: {lot.CustomerCode} - {lot.CustomerName}",
            $"اللوط: {lotNumber}",
            $"الحالة: {lot.OrderStatus ?? lot.MessageStatus ?? lot.MaterialKind ?? "—"}",
            $"الرصيد الختامي: {closingBalance}",
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
