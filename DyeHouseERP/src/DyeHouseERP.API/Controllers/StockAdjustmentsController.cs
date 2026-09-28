using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.StockAdjustments.Commands;
using DyeHouseERP.Application.StockAdjustments.DTOs;
using DyeHouseERP.Application.StockAdjustments.Queries;
using DyeHouseERP.API.Authorization;
using DyeHouseERP.API.Common;
using DyeHouseERP.Domain.Common;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DyeHouseERP.API.Controllers;

[ApiController]
[Route("api/stock-adjustments")]
[Authorize]
public class StockAdjustmentsController : ControllerBase
{
    private readonly ISender _mediator;
    public StockAdjustmentsController(ISender mediator) => _mediator = mediator;

    /// <summary>Stock adjustments as Excel or PDF, with the before/adjustment/after quantities (spec section 16).</summary>
    [HttpGet("export")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ReportsExport)]
    public async Task<IActionResult> Export([FromQuery] Guid? customerId, [FromQuery] string format = "excel",
        [FromServices] IReportExportService export = null!)
    {
        var adjustments = await _mediator.Send(new GetStockAdjustmentsQuery(customerId));

        var headers = new[] { "Adjustment no.", "Date", "Customer", "Item", "Message", "Type", "Before KG", "Before M", "Adjust KG", "Adjust M", "After KG", "After M", "Reason", "Approved by" };
        var rows = adjustments.Select(a => new object?[]
        {
            a.AdjustmentNumber, a.AdjustmentDate.ToString("yyyy-MM-dd"), $"{a.CustomerCode} - {a.CustomerName}",
            $"{a.ItemCode} - {a.ItemName}", a.MessageNumber, a.Type.ToString(),
            a.QuantityBeforeKg, a.QuantityBeforeMeter, a.AdjustmentQuantityKg, a.AdjustmentQuantityMeter,
            a.QuantityAfterKg, a.QuantityAfterMeter, a.Reason, a.ApprovedBy
        }).ToList();

        return ExportFileHelper.ToFile(export, format, "Stock Adjustments", "DyeHouse ERP", headers, rows, "stock-adjustments", "Adjustments");
    }

    /// <summary>List stock adjustments, newest first.</summary>
    [HttpGet]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.InventoryView)]
    [ProducesResponseType(typeof(List<StockAdjustmentDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<StockAdjustmentDto>>> Get([FromQuery] Guid? customerId)
        => Ok(await _mediator.Send(new GetStockAdjustmentsQuery(customerId)));

    [HttpPost]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.InventoryAdjust)]
    [ProducesResponseType(typeof(StockAdjustmentDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<StockAdjustmentDto>> Create([FromBody] CreateStockAdjustmentCommand command)
    {
        var result = await _mediator.Send(command);
        return CreatedAtAction(nameof(Get), new { }, result);
    }
}
