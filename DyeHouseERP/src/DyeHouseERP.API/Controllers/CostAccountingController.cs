using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.CostAccounting.Commands;
using DyeHouseERP.Application.CostAccounting.DTOs;
using DyeHouseERP.Application.CostAccounting.Queries;
using DyeHouseERP.Application.PriceLists.Commands;
using DyeHouseERP.Application.PriceLists.DTOs;
using DyeHouseERP.Application.PriceLists.Queries;
using DyeHouseERP.API.Authorization;
using DyeHouseERP.Domain.Common;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DyeHouseERP.API.Controllers;

[ApiController]
[Route("api/production-orders/{productionOrderId:guid}/cost")]
[Authorize]
public class CostAccountingController : ControllerBase
{
    private readonly ISender _mediator;
    public CostAccountingController(ISender mediator) => _mediator = mediator;

    [HttpGet]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ProductionView)]
    public async Task<ActionResult<ProductionOrderCostDto>> Get(Guid productionOrderId)
        => Ok(await _mediator.Send(new GetProductionOrderCostQuery(productionOrderId)));

    [HttpPost("entries")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ProductionEdit)]
    public async Task<ActionResult<CostEntryDto>> AddEntry(Guid productionOrderId, [FromBody] AddCostEntryRequest request)
        => Ok(await _mediator.Send(new CreateCostEntryCommand(productionOrderId, request.Category, request.Amount, request.EntryDate, request.Description)));

    /// <summary>Sets/updates the ESTIMATED cost of the Job Order (spec section 34).</summary>
    [HttpPut("estimate")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.CostingEditEstimate)]
    [ProducesResponseType(typeof(ProductionOrderCostDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<ProductionOrderCostDto>> SetEstimate(Guid productionOrderId, [FromBody] SetEstimateRequest request)
        => Ok(await _mediator.Send(new SetProductionOrderEstimateCommand(productionOrderId, request.EstimatedCost, request.CostingNotes)));

    /// <summary>Signs off the APPROVED cost of a completed Job Order (spec section 34).</summary>
    [HttpPost("approve")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.CostingApprove)]
    [ProducesResponseType(typeof(ProductionOrderCostDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<ProductionOrderCostDto>> ApproveCost(Guid productionOrderId, [FromBody] ApproveCostRequest request)
        => Ok(await _mediator.Send(new ApproveProductionOrderCostCommand(productionOrderId, request.ApprovedCost, request.CostingNotes)));

    /// <summary>
    /// Snapshots the price this Job Order is charged (spec section 34) - one row per
    /// unit. Supply <c>pricePerUnit</c> for a deliberate manual override (a reason is
    /// then required); omit it to resolve from the customer's price, else the general
    /// default. Later price-list edits never change an already-snapshotted price.
    /// </summary>
    [HttpPost("service-price")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.PricingManage)]
    [ProducesResponseType(typeof(ProductionOrderServicePriceDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<ProductionOrderServicePriceDto>> ApplyServicePrice(
        Guid productionOrderId, [FromBody] ApplyServicePriceRequest request)
        => Ok(await _mediator.Send(new ApplyServicePriceToJobOrderCommand(
            productionOrderId, request.StageDefinitionId, request.Unit, request.PricePerUnit, request.OverrideReason)));

    /// <summary>Job Order profitability (spec section 37): revenue from the snapshotted price, once for the order.</summary>
    [HttpGet("profitability")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.CostingView)]
    [ProducesResponseType(typeof(ProductionOrderProfitabilityDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<ProductionOrderProfitabilityDto>> GetProfitability(Guid productionOrderId)
        => Ok(await _mediator.Send(new GetProductionOrderProfitabilityQuery(productionOrderId)));

    /// <summary>Costing sheet as Excel/PDF for a comptroller review (spec sections 34 + 49).</summary>
    [HttpGet("export")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.CostingView)]
    public async Task<IActionResult> Export(Guid productionOrderId, [FromQuery] string format = "excel",
        [FromServices] IReportExportService export = null!)
    {
        var cost = await _mediator.Send(new GetProductionOrderCostQuery(productionOrderId));

        var headers = new[] { "Component", "Amount" };
        var rows = new List<object?[]>
        {
            new object?[] { "Material", cost.MaterialCost },
            new object?[] { "Preparation / dilution", cost.PreparationCost },
            new object?[] { "External processing", cost.ExternalProcessingCost },
            new object?[] { "Labor", cost.LaborCost },
            new object?[] { "Electricity", cost.ElectricityCost },
            new object?[] { "Fuel", cost.FuelCost },
            new object?[] { "Maintenance", cost.MaintenanceCost },
            new object?[] { "Transport", cost.TransportCost },
            new object?[] { "Packaging", cost.PackagingCost },
            new object?[] { "Repair", cost.RepairCost },
            new object?[] { "Other", cost.OtherCost },
            new object?[] { "Actual total", cost.TotalCost },
            new object?[] { "Estimated", cost.EstimatedCost },
            new object?[] { "Approved", cost.ApprovedCost }
        };

        if (string.Equals(format, "pdf", StringComparison.OrdinalIgnoreCase))
            return File(export.GeneratePdf($"Job Order Costing - {cost.ProductionOrderNumber}", "DyeHouse ERP", headers, rows),
                "application/pdf", $"costing-{cost.ProductionOrderNumber}.pdf");

        return File(export.GenerateExcel("Costing", headers, rows),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"costing-{cost.ProductionOrderNumber}.xlsx");
    }
}

public record AddCostEntryRequest(Domain.Enums.CostCategory Category, decimal Amount, DateTime EntryDate, string? Description);
public record SetEstimateRequest(decimal? EstimatedCost, string? CostingNotes);
public record ApproveCostRequest(decimal ApprovedCost, string? CostingNotes);

public record ApplyServicePriceRequest(
    Guid StageDefinitionId, Domain.Enums.UnitOfMeasure Unit,
    decimal? PricePerUnit = null, string? OverrideReason = null);
