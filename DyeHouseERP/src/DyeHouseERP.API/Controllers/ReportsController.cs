using DyeHouseERP.Application.Common.Interfaces;
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
    /// Warehouse balances report (spec sections 18 + 30) as Excel or PDF: the live
    /// remaining quantity per raw-receipt message line, plus the ready-goods balance
    /// per job order. Both are summed from the ledgers, never from a stored balance.
    /// </summary>
    [HttpGet("warehouse-balances/export")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ReportsExport)]
    public async Task<IActionResult> ExportWarehouseBalances([FromQuery] Guid? customerId,
        [FromQuery] string format = "excel")
    {
        var messages = await _mediator.Send(new GetRawMessagesQuery(customerId, null, true));
        var ready = await _mediator.Send(new GetReadyGoodsBalanceQuery(customerId));

        var headers = new[] { "Kind", "Reference", "Customer", "Item", "Warehouse / Job order", "Balance KG", "Balance M" };
        var rows = new List<object?[]>();

        rows.AddRange(messages.SelectMany(m => m.Lines.Select(l => new object?[]
        {
            "Raw material", m.MessageNumber, $"{m.CustomerCode} - {m.CustomerName}", $"{l.ItemCode} - {l.ItemName}",
            m.WarehouseName, l.RemainingKg, l.RemainingMeter
        })));

        rows.AddRange(ready.Select(r => new object?[]
        {
            "Ready goods", r.ProductionOrderNumber, $"{r.CustomerCode} - {r.CustomerName}", $"{r.ItemCode} - {r.ItemName}",
            r.ProductionOrderNumber, r.RemainingKg, r.RemainingMeter
        }));

        return ExportFileHelper.ToFile(_export, format, "Warehouse Balances", "DyeHouse ERP", headers, rows, "warehouse-balances", "Balances");
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
