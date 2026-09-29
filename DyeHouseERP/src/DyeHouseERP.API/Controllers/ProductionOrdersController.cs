using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.API.Common;
using DyeHouseERP.Application.ProductionOrders.Commands;
using DyeHouseERP.Application.ProductionOrders.DTOs;
using DyeHouseERP.Application.ProductionOrders.Queries;
using DyeHouseERP.Domain.Enums;
using DyeHouseERP.API.Authorization;
using DyeHouseERP.Domain.Common;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DyeHouseERP.API.Controllers;

[ApiController]
[Route("api/production-orders")]
[Authorize]
public class ProductionOrdersController : ControllerBase
{
    private readonly ISender _mediator;
    public ProductionOrdersController(ISender mediator) => _mediator = mediator;

    [HttpGet]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ProductionView)]
    [ProducesResponseType(typeof(List<ProductionOrderDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<ProductionOrderDto>>> Get(
        [FromQuery] Guid? customerId, [FromQuery] ProductionOrderStatus? status,
        [FromQuery] JobOrderType? jobOrderType, [FromQuery] Guid? formationRequestId)
        => Ok(await _mediator.Send(new GetProductionOrdersQuery(customerId, status, jobOrderType, formationRequestId)));

    [HttpGet("{id:guid}")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ProductionView)]
    [ProducesResponseType(typeof(ProductionOrderDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<ProductionOrderDto>> GetById(Guid id)
        => Ok(await _mediator.Send(new GetProductionOrderByIdQuery(id)));

    /// <summary>Printable production order PDF (spec section 39) - stage route + raw allocations, for the floor or a customer request.</summary>
    /// <summary>
    /// Job orders / production records as Excel or PDF (spec section 39), with the
    /// current stage and completed quantity per order so it doubles as the
    /// production-register report. Honours the same filters as the list.
    /// </summary>
    [HttpGet("export")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ReportsExport)]
    public async Task<IActionResult> Export([FromQuery] Guid? customerId, [FromQuery] ProductionOrderStatus? status,
        [FromQuery] JobOrderType? jobOrderType, [FromQuery] Guid? formationRequestId,
        [FromQuery] string format = "excel", [FromServices] IReportExportService export = null!)
    {
        var orders = await _mediator.Send(new GetProductionOrdersQuery(customerId, status, jobOrderType, formationRequestId));

        var headers = new[] { "Job order", "Date", "Customer", "Item", "Color", "Requested KG", "Requested M", "Stage", "Completed KG", "Status", "Type" };
        var rows = orders.Select(o =>
        {
            var completedKg = o.StageExecutions.Where(s => s.Status == StageExecutionStatus.Completed).Sum(s => s.OutputKg ?? 0);
            var current = o.StageExecutions.FirstOrDefault(s => s.Status == StageExecutionStatus.InProgress)
                ?? o.StageExecutions.FirstOrDefault(s => s.Status == StageExecutionStatus.Pending);
            return new object?[]
            {
                o.OrderNumber, o.OrderDate.ToString("yyyy-MM-dd"), $"{o.CustomerCode} - {o.CustomerName}",
                $"{o.ItemCode} - {o.ItemName}", o.Color, o.RequestedQuantityKg, o.RequestedQuantityMeter,
                current?.StageName, completedKg, o.Status.ToString(), o.JobOrderType.ToString()
            };
        }).ToList();

        return ExportFileHelper.ToFile(export, format, "Job Orders", "DyeHouse ERP", headers, rows, "job-orders", "JobOrders");
    }

    [HttpGet("{id:guid}/pdf")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ReportsExport)]
    public async Task<IActionResult> GetPdf(Guid id, [FromServices] DyeHouseERP.Application.Common.Interfaces.IReportExportService export)
    {
        var order = await _mediator.Send(new GetProductionOrderByIdQuery(id));

        var headers = new List<string> { "Stage", "Status", "Input", "Output", "Loss", "Separates" };
        var rows = order.StageExecutions.Select(s => new object?[]
        {
            s.StageName, s.Status, s.InputKg, s.OutputKg, s.LossKg, s.SeparatesKg
        }).ToList();

        var subtitle = $"{order.CustomerCode} - {order.CustomerName}  |  {order.ItemCode} - {order.ItemName}{(order.Color is null ? "" : $" ({order.Color})")}  |  {order.Status}";
        var bytes = export.GeneratePdf($"أمر تشغيل رقم {order.OrderNumber}", subtitle, headers, rows);
        return File(bytes, "application/pdf", $"production-order-{order.OrderNumber}.pdf");
    }

    /// <summary>Creates a Production Order and snapshots the currently-active stage route onto it (spec sections 12, 19).</summary>
    [HttpPost]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ProductionCreate)]
    [ProducesResponseType(typeof(ProductionOrderDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<ProductionOrderDto>> Create([FromBody] CreateProductionOrderCommand command)
    {
        var result = await _mediator.Send(command);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    /// <summary>
    /// Allocates raw material from a specific, user-chosen RawMessage
    /// (spec section 13 - manual selection, no FIFO). Call once per message
    /// being drawn from.
    /// </summary>
    [HttpPost("{id:guid}/raw-allocations")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.RawConsume)]
    [ProducesResponseType(typeof(ProductionOrderDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<ProductionOrderDto>> AllocateRaw(Guid id, [FromBody] AllocateRawRequest request)
    {
        var result = await _mediator.Send(new AllocateRawCommand(
            id, request.RawMessageId, request.ItemId, request.QuantityKg, request.QuantityMeter,
            request.OverrideNegativeStock, request.OverrideReason));
        return Ok(result);
    }

    [HttpPost("stage-executions/{stageExecutionId:guid}/start")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ProductionExecuteStage)]
    [ProducesResponseType(typeof(ProductionOrderDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<ProductionOrderDto>> StartStage(Guid stageExecutionId)
        => Ok(await _mediator.Send(new StartStageCommand(stageExecutionId)));

    [HttpPost("stage-executions/{stageExecutionId:guid}/complete")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ProductionExecuteStage)]
    [ProducesResponseType(typeof(ProductionOrderDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<ProductionOrderDto>> CompleteStage(Guid stageExecutionId, [FromBody] CompleteStageRequest request)
    {
        var result = await _mediator.Send(new CompleteStageCommand(
            stageExecutionId, request.InputKg, request.InputMeter, request.OutputKg, request.OutputMeter,
            request.LossKg, request.LossMeter, request.SeparatesKg, request.SeparatesMeter,
            request.Notes, request.ApprovedBy));
        return Ok(result);
    }

    [HttpPost("stage-executions/{stageExecutionId:guid}/skip")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ProductionExecuteStage)]
    [ProducesResponseType(typeof(ProductionOrderDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<ProductionOrderDto>> SkipStage(Guid stageExecutionId, [FromBody] SkipStageRequest request)
        => Ok(await _mediator.Send(new SkipStageCommand(stageExecutionId, request.Reason)));

    [HttpPost("{id:guid}/complete")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ProductionComplete)]
    [ProducesResponseType(typeof(ProductionOrderDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<ProductionOrderDto>> Complete(Guid id)
        => Ok(await _mediator.Send(new CompleteProductionOrderCommand(id)));
}

public record AllocateRawRequest(
    Guid RawMessageId, Guid ItemId, decimal? QuantityKg, decimal? QuantityMeter,
    bool OverrideNegativeStock, string? OverrideReason);

public record CompleteStageRequest(
    decimal? InputKg, decimal? InputMeter, decimal? OutputKg, decimal? OutputMeter,
    decimal? LossKg, decimal? LossMeter, decimal? SeparatesKg, decimal? SeparatesMeter,
    string? Notes, string? ApprovedBy);

public record SkipStageRequest(string Reason);
