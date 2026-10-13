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
        [FromQuery] JobOrderType? jobOrderType)
        => Ok(await _mediator.Send(new GetProductionOrdersQuery(customerId, status, jobOrderType)));

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
        [FromQuery] JobOrderType? jobOrderType,
        [FromQuery] string format = "excel", [FromServices] IReportExportService export = null!)
    {
        var orders = await _mediator.Send(new GetProductionOrdersQuery(customerId, status, jobOrderType));

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

        var headers = new List<string> { "Stage", "Status", "Input", "Output", "Loss", "Separates", "Drawing number / رقم الرسمة" };
        var rows = order.StageExecutions.Select(s => new object?[]
        {
            s.StageName, s.Status, s.InputKg, s.OutputKg, s.LossKg, s.SeparatesKg, s.DrawingNumber
        }).ToList();

        var subtitle = $"{order.CustomerCode} - {order.CustomerName}  |  {order.ItemCode} - {order.ItemName}{(order.Color is null ? "" : $" ({order.Color})")}  |  {order.Status}";
        var bytes = export.GeneratePdf($"أمر تشغيل رقم {order.OrderNumber}", subtitle, headers, rows);
        return File(bytes, "application/pdf", $"production-order-{order.OrderNumber}.pdf");
    }

    /// <summary>
    /// THE canonical workflow (spec section 5): issuing raw material creates the Job Order.
    /// One atomic step posts the inventory OUT movement, creates the Production Order from the
    /// ACTUAL issued quantities, links the order to that issue transaction, and links the optional
    /// planning Formation Request/basin. There is deliberately no endpoint that creates an ordinary
    /// Job Order WITHOUT an issue: the old create-then-allocate route is gone, so a second stock
    /// OUT for the same material is not reachable. (Reprocessing a Separate is a separate,
    /// explicit workflow that draws from separates rather than a customer lot.)
    /// </summary>
    [HttpPost("issue")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.RawConsume)]
    [ProducesResponseType(typeof(ProductionOrderDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<ProductionOrderDto>> IssueRawMaterial([FromBody] IssueRawMaterialCommand command)
    {
        var result = await _mediator.Send(command);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    /// <summary>
    /// Edits an OPEN order's descriptive and scheduling fields (colour, raw origin, customer reference,
    /// notes, priority, order date and the closed/open line type) - spec sections 12 + 16. The order's
    /// IDENTITY (customer, item) and its REQUESTED QUANTITY are not editable: the quantity is the formation
    /// stage's immutable baseline and the denominator of the 100% allocation rule, and the identity is already
    /// referenced by allocations, ready goods, deliveries and invoices. A wrong customer/item/quantity is
    /// corrected by cancelling the order (POST {id}/cancel) and creating a new one. A completed or cancelled
    /// order is locked. Every edit is captured by the audit log with its before/after value.
    /// </summary>
    [HttpPut("{id:guid}")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ProductionEdit)]
    [ProducesResponseType(typeof(ProductionOrderDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<ProductionOrderDto>> Update(Guid id, [FromBody] UpdateProductionOrderRequest request)
        => Ok(await _mediator.Send(new UpdateProductionOrderCommand(
            id, request.OrderDate, request.Color, request.RawOrigin, request.CustomerReference,
            request.Notes, request.Priority, request.JobOrderType)));

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
            request.Notes));
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
    public async Task<ActionResult<ProductionOrderDto>> Complete(Guid id, [FromBody] CompleteProductionOrderRequest request)
        => Ok(await _mediator.Send(new CompleteProductionOrderCommand(id, request.Reason)));

    /// <summary>
    /// Cancels a Job Order and reverses the raw material it still holds as a NEW compensating IN row per lot
    /// through the inventory ledger (spec section 18) - nothing posted is edited or deleted, and the reason is
    /// mandatory. A completed order cannot be cancelled (its output has already left the raw dimension).
    /// </summary>
    [HttpPost("{id:guid}/cancel")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ProductionEdit)]
    [ProducesResponseType(typeof(ProductionOrderDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<ProductionOrderDto>> Cancel(Guid id, [FromBody] CancelProductionOrderRequest request)
        => Ok(await _mediator.Send(new CancelProductionOrderCommand(id, request.Reason)));

    /// <summary>
    /// Pauses the order (موقوف مؤقتًا) and releases the user's chosen quantity of
    /// unused raw material back to the customer's own raw stock (spec section 19).
    /// The release is read from the ledger and bounded by what the NAMED lot still
    /// holds for this order; it is posted as a new compensating IN movement, while
    /// nothing already posted is edited or deleted.
    /// </summary>
    /// <summary>
    /// Moves the Job Order to the next stage the USER selects (spec sections 13-17).
    /// One call closes the current stage (recording output, loss and loss %),
    /// locks it, and activates only the chosen stage with the previous output as
    /// its baseline. There is no separate "End Stage" button. Selecting the stage
    /// marked IsReadyGoodsStage instead moves the output to the Ready Goods
    /// warehouse and completes the order - there is no separate "End Job Order"
    /// button either.
    /// </summary>
    [HttpPost("stage-executions/{stageExecutionId:guid}/transfer")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ProductionExecuteStage)]
    [ProducesResponseType(typeof(ProductionOrderDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<ProductionOrderDto>> TransferStage(Guid stageExecutionId, [FromBody] TransferStageRequest request)
        => Ok(await _mediator.Send(new TransferToNextStageCommand(
            stageExecutionId, request.NextStageDefinitionId,
            request.OutputKg, request.OutputMeter, request.SeparatesKg, request.SeparatesMeter, request.Notes, request.DrawingNumber)));

    /// <summary>
    /// Edits the OPEN stage's output weight (spec section 16). No approval needed,
    /// but the previous and new figures are audited, and the stage's baseline is
    /// never touched - so the loss is always measured from the original weight.
    /// </summary>
    [HttpPost("stage-executions/{stageExecutionId:guid}/output")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ProductionExecuteStage)]
    [ProducesResponseType(typeof(ProductionOrderDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<ProductionOrderDto>> UpdateStageOutput(Guid stageExecutionId, [FromBody] UpdateStageOutputRequest request)
        => Ok(await _mediator.Send(new UpdateStageOutputCommand(stageExecutionId, request.OutputKg, request.OutputMeter, request.Reason)));

    [HttpPost("stage-executions/{stageExecutionId:guid}/drawing")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ProductionExecuteStage)]
    public async Task<ActionResult<ProductionOrderDto>> UpdateDrawing(Guid stageExecutionId, [FromBody] UpdateStageDrawingRequest request)
        => Ok(await _mediator.Send(new UpdateStageDrawingCommand(stageExecutionId, request.DrawingNumber, request.Reason)));

    [HttpPost("{id:guid}/pause")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ProductionEdit)]
    [ProducesResponseType(typeof(ProductionOrderDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<ProductionOrderDto>> Pause(Guid id, [FromBody] PauseProductionOrderRequest request)
        => Ok(await _mediator.Send(new PauseProductionOrderCommand(
            id, request.Reason, request.RawMessageId, request.ItemId, request.ReleaseKg, request.ReleaseMeter)));

    /// <summary>
    /// Resumes a paused order with a NEW raw material issue (spec section 19). The
    /// SAME order continues; the response carries the previous cycle's loss
    /// percentage and the expected output it implies as the basis for this cycle.
    /// </summary>
    [HttpPost("{id:guid}/resume")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.RawConsume)]
    [ProducesResponseType(typeof(ProductionOrderDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<ProductionOrderDto>> Resume(Guid id, [FromBody] ResumeProductionOrderRequest request)
        => Ok(await _mediator.Send(new ResumeProductionOrderCommand(
            id, request.RawMessageId, request.ItemId, request.QuantityKg, request.QuantityMeter,
            request.OverrideNegativeStock, request.OverrideReason)));
}

public record TransferStageRequest(
    Guid NextStageDefinitionId,
    decimal? OutputKg, decimal? OutputMeter,
    decimal? SeparatesKg, decimal? SeparatesMeter, string? Notes, string? DrawingNumber = null);

public record UpdateStageDrawingRequest(string DrawingNumber, string Reason);

public record UpdateStageOutputRequest(decimal? OutputKg, decimal? OutputMeter, string? Reason);

public record PauseProductionOrderRequest(
    string Reason, Guid? RawMessageId = null, Guid? ItemId = null,
    decimal? ReleaseKg = null, decimal? ReleaseMeter = null);

public record CompleteProductionOrderRequest(string Reason);

public record CancelProductionOrderRequest(string Reason);

/// <summary>
/// Body for editing an open Job Order. Customer, item and requested quantity are intentionally absent:
/// they are corrected by cancelling the order and re-creating it, never by editing it in place.
/// </summary>
public record UpdateProductionOrderRequest(
    DateTime OrderDate,
    string? Color = null,
    string? RawOrigin = null,
    string? CustomerReference = null,
    string? Notes = null,
    ProductionPriority Priority = ProductionPriority.Normal,
    JobOrderType JobOrderType = JobOrderType.ClosedLine);

public record ResumeProductionOrderRequest(
    Guid RawMessageId, Guid ItemId, decimal? QuantityKg, decimal? QuantityMeter,
    bool OverrideNegativeStock = false, string? OverrideReason = null);

public record AllocateRawRequest(
    Guid RawMessageId, Guid ItemId, decimal? QuantityKg, decimal? QuantityMeter,
    bool OverrideNegativeStock, string? OverrideReason);

public record CompleteStageRequest(
    decimal? InputKg, decimal? InputMeter, decimal? OutputKg, decimal? OutputMeter,
    decimal? LossKg, decimal? LossMeter, decimal? SeparatesKg, decimal? SeparatesMeter,
    string? Notes);

public record SkipStageRequest(string Reason);
