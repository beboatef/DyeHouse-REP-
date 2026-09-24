using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.Reports.DTOs;
using DyeHouseERP.Application.Reports.Queries;
using DyeHouseERP.API.Authorization;
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
