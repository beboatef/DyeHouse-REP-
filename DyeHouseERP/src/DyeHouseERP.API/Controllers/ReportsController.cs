using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.Inventory.Queries;
using DyeHouseERP.Application.Reports.DTOs;
using DyeHouseERP.Application.ReadyGoods.Queries;
using DyeHouseERP.Application.RawReceipts.Queries;
using DyeHouseERP.Application.Reports.Queries;
using DyeHouseERP.API.Authorization;
using DyeHouseERP.API.Common;
using DyeHouseERP.Domain.Common;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DyeHouseERP.API.Controllers;

/// <summary>Standalone factory-wide reports that don't belong to one specific document type.</summary>
[ApiController]
[Route("api/reports")]
[Authorize]
public class ReportsController : ControllerBase
{
    private readonly ISender _mediator;
    private readonly IReportExportService _export;

    public ReportsController(ISender mediator, IReportExportService export)
    {
        _mediator = mediator;
        _export = export;
    }

    /// <summary>
    /// The inventory ledger: every stock movement ever posted, with its source document, user, date,
    /// warehouse, item, customer, raw material message and job order (spec sections 10, 14, 48).
    /// Balances are never stored - they are summed from exactly these rows.
    /// </summary>
    [HttpGet("inventory-movements")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.InventoryView)]
    [ProducesResponseType(typeof(List<InventoryMovementDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<InventoryMovementDto>>> GetInventoryMovements(
        [FromQuery] Guid? customerId, [FromQuery] Guid? itemId, [FromQuery] Guid? warehouseId,
        [FromQuery] Guid? rawMessageId, [FromQuery] Guid? productionOrderId,
        [FromQuery] DateTime? from, [FromQuery] DateTime? to, [FromQuery] int limit = 500)
        => Ok(await _mediator.Send(new GetInventoryMovementsQuery(
            customerId, itemId, warehouseId, rawMessageId, productionOrderId, from, to, limit)));

    [HttpGet("inventory-movements/excel")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ReportsExport)]
    public async Task<IActionResult> GetInventoryMovementsExcel(
        [FromQuery] Guid? customerId, [FromQuery] Guid? itemId, [FromQuery] Guid? warehouseId,
        [FromQuery] Guid? rawMessageId, [FromQuery] DateTime? from, [FromQuery] DateTime? to,
        [FromServices] IReportExportService export)
    {
        var movements = await _mediator.Send(new GetInventoryMovementsQuery(
            customerId, itemId, warehouseId, rawMessageId, null, from, to, 5000));

        var headers = new List<string>
        {
            "Date", "SourceType", "SourceDocument", "Warehouse", "Customer", "Item",
            "Message", "JobOrder", "QuantityKg", "QuantityMeter", "Direction", "User"
        };

        var rows = movements.Select(m => new object?[]
        {
            m.TransactionDate.ToString("yyyy-MM-dd"), m.SourceDocumentType, m.SourceDocumentNumber, m.WarehouseName,
            m.CustomerCode, m.ItemCode, m.MessageNumber, m.OrderNumber,
            m.QuantityKg, m.QuantityMeter, m.Direction.ToString(), m.CreatedBy
        }).ToList();

        var bytes = export.GenerateExcel("InventoryMovements", headers, rows);
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "inventory-movements.xlsx");
    }

    /// <summary>Same inventory ledger, as a printable PDF (spec section 39) - filters and date range included in the header.</summary>
    [HttpGet("inventory-movements/pdf")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ReportsExport)]
    public async Task<IActionResult> GetInventoryMovementsPdf(
        [FromQuery] Guid? customerId, [FromQuery] Guid? itemId, [FromQuery] Guid? warehouseId,
        [FromQuery] Guid? rawMessageId, [FromQuery] Guid? productionOrderId,
        [FromQuery] DateTime? from, [FromQuery] DateTime? to)
    {
        var movements = await _mediator.Send(new GetInventoryMovementsQuery(
            customerId, itemId, warehouseId, rawMessageId, productionOrderId, from, to, 5000));

        var headers = new List<string>
        {
            "Date", "SourceType", "SourceDocument", "Warehouse", "Customer", "Item",
            "Message", "JobOrder", "QuantityKg", "QuantityMeter", "Direction", "User"
        };

        var rows = movements.Select(m => new object?[]
        {
            m.TransactionDate.ToString("yyyy-MM-dd"), m.SourceDocumentType, m.SourceDocumentNumber, m.WarehouseName,
            m.CustomerCode, m.ItemCode, m.MessageNumber, m.OrderNumber,
            m.QuantityKg, m.QuantityMeter, m.Direction.ToString(), m.CreatedBy
        }).ToList();

        return ExportFileHelper.ToPdf(_export, "حركات المخزون", DateRangeLabel(from, to), headers, rows, "inventory-movements");
    }

    /// <summary>
    /// Warehouse balances report (spec sections 18 + 30 + 48) as Excel or PDF: EVERY stock warehouse
    /// separately, each read from its OWN ledger, followed by a total for that warehouse and a grand
    /// total that never mixes two ledgers into one figure.
    ///
    /// The rows come from the same traceability read the balances screen uses - one row per balance,
    /// with its warehouse - so the report cannot disagree with the screen, and no figure is stored or
    /// cached. A warehouse holding stock of one receipt message or job order keeps its own row: the
    /// warehouse is part of the balance's identity, never a label printed next to a combined total.
    /// KG and Meter stay in their own columns, and materials (which have their own unit) are totalled per
    /// unit rather than added into the fabric figures.
    /// </summary>
    [HttpGet("warehouse-balances/export")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ReportsExport)]
    public async Task<IActionResult> ExportWarehouseBalances([FromQuery] Guid? customerId,
        [FromQuery] Guid? warehouseId, [FromQuery] string format = "excel")
    {
        var headers = new[]
        {
            "Warehouse", "Kind", "Reference", "Customer / supplier", "Item / material", "Unit",
            "Balance KG", "Balance M", "Balance"
        };
        var rows = new List<object?[]>();

        string ScopeLabel(string scope) => scope switch
        {
            StockScopes.Ready => "أرصدة المخزن الجاهز",
            StockScopes.Materials => "أرصدة المواد والكيماويات",
            StockScopes.Supplies => "أرصدة مستلزمات التشغيل",
            _ => "أرصدة مخزن الخامات"
        };

        // Grand totals, kept per ledger and per unit: adding a material's KG to a fabric's KG, or a
        // material's litres to its kilograms, would be a number with no physical meaning.
        decimal fabricKg = 0m, fabricMeter = 0m;
        var hasFabricKg = false; var hasFabricMeter = false;
        var materialTotals = new Dictionary<string, decimal>(StringComparer.Ordinal);

        foreach (var scope in new[] { StockScopes.Raw, StockScopes.Ready, StockScopes.Materials, StockScopes.Supplies })
        {
            var data = await _mediator.Send(new GetStockTraceabilityQuery(
                Scope: scope, CustomerId: customerId, WarehouseId: warehouseId, OnlyWithBalance: true));
            if (data.Rows.Count == 0) continue;

            var isMaterials = data.Scope is StockScopes.Materials or StockScopes.Supplies;

            foreach (var r in data.Rows)
            {
                rows.Add(isMaterials
                    ? new object?[]
                    {
                        r.WarehouseName, ScopeLabel(scope), r.MaterialCode, r.SupplierName,
                        $"{r.MaterialCode} - {r.MaterialName}", r.MaterialUnit, null, null, r.RemainingQuantity
                    }
                    : new object?[]
                    {
                        r.WarehouseName, ScopeLabel(scope), r.MessageNumber ?? r.OrderNumber,
                        $"{r.CustomerCode} - {r.CustomerName}", $"{r.ItemCode} - {r.ItemName}",
                        r.SecondaryUnit is null ? r.BaseUnit : $"{r.BaseUnit} / {r.SecondaryUnit}",
                        r.RemainingKg, r.RemainingMeter, null
                    });
            }

            // A total for EACH warehouse of this ledger - so "the warehouse total" is a figure that
            // exists in that warehouse, not a share of a combined stock level.
            foreach (var grp in data.Rows.GroupBy(r => new
                     {
                         r.WarehouseId, r.WarehouseName,
                         Unit = isMaterials ? r.MaterialUnit : null
                     }))
            {
                if (isMaterials)
                {
                    var sum = grp.Sum(r => r.RemainingQuantity ?? 0m);
                    var unit = grp.Key.Unit ?? "—";
                    rows.Add(new object?[]
                    {
                        grp.Key.WarehouseName, $"إجمالي — {ScopeLabel(scope)}", null, null,
                        $"إجمالي المخزن ({unit})", unit, null, null, sum
                    });
                    materialTotals[unit] = materialTotals.GetValueOrDefault(unit) + sum;
                }
                else
                {
                    var kg = grp.Sum(r => r.RemainingKg ?? 0m);
                    var meter = grp.Sum(r => r.RemainingMeter ?? 0m);
                    var kgSeen = grp.Any(r => r.RemainingKg.HasValue);
                    var meterSeen = grp.Any(r => r.RemainingMeter.HasValue);
                    rows.Add(new object?[]
                    {
                        grp.Key.WarehouseName, $"إجمالي — {ScopeLabel(scope)}", null, null,
                        "إجمالي المخزن", "KG / M", kgSeen ? kg : null, meterSeen ? meter : null, null
                    });
                    if (kgSeen) { fabricKg += kg; hasFabricKg = true; }
                    if (meterSeen) { fabricMeter += meter; hasFabricMeter = true; }
                }
            }
        }

        if (hasFabricKg || hasFabricMeter)
            rows.Add(new object?[]
            {
                "—", "إجمالي عام", null, null, "إجمالي عام — الأقمشة (كل مخازن القماش)", "KG / M",
                hasFabricKg ? fabricKg : null, hasFabricMeter ? fabricMeter : null, null
            });

        foreach (var unit in materialTotals.Keys.OrderBy(k => k, StringComparer.Ordinal))
            rows.Add(new object?[]
            {
                "—", "إجمالي عام", null, null, $"إجمالي عام — المواد ({unit})", unit, null, null,
                materialTotals[unit]
            });

        return ExportFileHelper.ToFile(_export, format, "أرصدة المخازن", null, headers, rows, "warehouse-balances", "Balances");
    }

    /// <summary>Negative Stock / Balance Override Report (spec section 17).</summary>
    [HttpGet("negative-stock-overrides")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ReportsView)]
    public async Task<ActionResult<List<NegativeStockOverrideDto>>> GetNegativeStockOverrides([FromQuery] DateTime? from, [FromQuery] DateTime? to)
        => Ok(await _mediator.Send(new GetNegativeStockOverridesQuery(from, to)));

    [HttpGet("negative-stock-overrides/pdf")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ReportsExport)]
    public async Task<IActionResult> GetNegativeStockOverridesPdf([FromQuery] DateTime? from, [FromQuery] DateTime? to)
    {
        var (headers, rows) = await BuildNegativeStockReportDataAsync(from, to);
        var bytes = _export.GeneratePdf("تقرير تجاوز الرصيد السالب", DateRangeLabel(from, to), headers, rows);
        return File(bytes, "application/pdf", "negative-stock-overrides.pdf");
    }

    [HttpGet("negative-stock-overrides/excel")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ReportsExport)]
    public async Task<IActionResult> GetNegativeStockOverridesExcel([FromQuery] DateTime? from, [FromQuery] DateTime? to)
    {
        var (headers, rows) = await BuildNegativeStockReportDataAsync(from, to);
        var bytes = _export.GenerateExcel("Overrides", headers, rows);
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "negative-stock-overrides.xlsx");
    }

    /// <summary>
    /// الهالك report (approved policy): waste is a COMPUTED LOSS, never a posted balance, so this endpoint
    /// reads only - it creates no ledger row and no warehouse balance. Each order's waste is derived from
    /// its original raw weight minus first grade, second grade and separates, using the same definition
    /// the delivery statement prints.
    /// </summary>
    [HttpGet("waste")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ReportsView)]
    [ProducesResponseType(typeof(WasteReportDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<WasteReportDto>> GetWasteReport(
        [FromQuery] Guid? customerId, [FromQuery] Guid? productionOrderId,
        [FromQuery] DateTime? from, [FromQuery] DateTime? to)
        => Ok(await _mediator.Send(new GetWasteReportQuery(customerId, productionOrderId, from, to)));

    private async Task<(List<string> Headers, List<object?[]> Rows)> BuildNegativeStockReportDataAsync(DateTime? from, DateTime? to)
    {
        var items = await _mediator.Send(new GetNegativeStockOverridesQuery(from, to));
        var headers = new List<string> { "Date", "Message", "Customer", "Item", "Requested", "Before", "After", "Reason", "Requested By", "Approved By" };
        var rows = items.Select(i => new object?[]
        {
            i.ApprovedAtUtc.ToString("yyyy-MM-dd HH:mm"), i.MessageNumber, i.CustomerCode, i.ItemCode,
            i.RequestedQuantity, i.BalanceBefore, i.ResultingBalance, i.Reason, i.RequestedBy, i.ApprovedBy
        }).ToList();
        return (headers, rows);
    }

    private static string DateRangeLabel(DateTime? from, DateTime? to) =>
        from.HasValue || to.HasValue ? $"{from?.ToString("yyyy-MM-dd") ?? "..."} - {to?.ToString("yyyy-MM-dd") ?? "..."}" : "";
}
