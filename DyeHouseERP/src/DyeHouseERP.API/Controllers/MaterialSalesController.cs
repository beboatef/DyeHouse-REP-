using DyeHouseERP.API.Authorization;
using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.API.Common;
using DyeHouseERP.Application.MaterialSales.Commands;
using DyeHouseERP.Application.MaterialSales.DTOs;
using DyeHouseERP.Application.MaterialSales.Queries;
using DyeHouseERP.Domain.Common;
using DyeHouseERP.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DyeHouseERP.API.Controllers;

/// <summary>
/// Sales of FACTORY-OWNED materials/chemicals to third parties (spec section
/// 26). Kept away from job-work invoicing on purpose: this document moves
/// factory stock and raises a receivable (or cash), while a processing invoice
/// bills work done on a customer's own raw material.
/// </summary>
[ApiController]
[Route("api/material-sales")]
[Authorize]
public class MaterialSalesController : ControllerBase
{
    private readonly ISender _mediator;
    public MaterialSalesController(ISender mediator) => _mediator = mediator;

    [HttpGet]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.MaterialSalesView)]
    [ProducesResponseType(typeof(List<MaterialSaleDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<MaterialSaleDto>>> Get(
        [FromQuery] MaterialSaleStatus? status, [FromQuery] Guid? customerId,
        [FromQuery] DateTime? from, [FromQuery] DateTime? to, [FromQuery] string? search)
        => Ok(await _mediator.Send(new GetMaterialSalesQuery(status, customerId, from, to, search)));

    [HttpGet("{id:guid}")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.MaterialSalesView)]
    [ProducesResponseType(typeof(MaterialSaleDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<MaterialSaleDto>> GetById(Guid id)
        => Ok(await _mediator.Send(new GetMaterialSaleByIdQuery(id)));

    /// <summary>Material sales as Excel or PDF, honouring the same filters as the list (spec section 26).</summary>
    [HttpGet("export")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ReportsExport)]
    public async Task<IActionResult> Export([FromQuery] MaterialSaleStatus? status, [FromQuery] Guid? customerId,
        [FromQuery] DateTime? from, [FromQuery] DateTime? to, [FromQuery] string format = "excel",
        [FromServices] IReportExportService export = null!)
    {
        var sales = await _mediator.Send(new GetMaterialSalesQuery(status, customerId, from, to, null));

        var headers = new[] { "Sale no.", "Date", "Warehouse", "Buyer", "Material", "Qty", "Unit", "Unit price", "Line total", "Total", "Status" };
        var rows = sales.SelectMany(s => s.Lines.Select(l => new object?[]
        {
            s.SaleNumber, s.SaleDate.ToString("yyyy-MM-dd"), s.WarehouseName,
            s.CustomerCode is null ? s.BuyerName : $"{s.CustomerCode} - {s.BuyerName}",
            $"{l.MaterialCode} - {l.MaterialName}", l.Quantity, l.Unit.ToString(), l.UnitPrice, l.LineTotal,
            s.Total, s.Status.ToString()
        })).ToList();

        return ExportFileHelper.ToFile(export, format, "Material Sales", "DyeHouse ERP", headers, rows, "material-sales", "MaterialSales");
    }

    /// <summary>One material sale as a printable invoice-style document (spec section 39).</summary>
    [HttpGet("{id:guid}/pdf")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ReportsExport)]
    public async Task<IActionResult> GetPdf(Guid id, [FromServices] IReportExportService export)
    {
        var sale = await _mediator.Send(new GetMaterialSaleByIdQuery(id));

        var headers = new[] { "Material", "Qty", "Unit", "Unit price", "Line total" };
        var rows = sale.Lines.Select(l => new object?[]
        {
            $"{l.MaterialCode} - {l.MaterialName}", l.Quantity, l.Unit.ToString(), l.UnitPrice, l.LineTotal
        }).ToList();
        rows.Add(new object?[] { "", "", "", "Subtotal", sale.SubTotal });
        rows.Add(new object?[] { "", "", "", "Discount", sale.Discount });
        rows.Add(new object?[] { "", "", "", "Tax", sale.Tax });
        rows.Add(new object?[] { "", "", "", "Total", sale.Total });

        var buyer = sale.CustomerCode is null ? sale.BuyerName : $"{sale.CustomerCode} - {sale.BuyerName}";
        var subtitle = $"{buyer}  |  {sale.SaleDate:yyyy-MM-dd}  |  {sale.WarehouseName}  |  {sale.Status}";
        return ExportFileHelper.ToPdf(export, $"بيع مواد رقم {sale.SaleNumber}", subtitle, headers, rows, $"material-sale-{sale.SaleNumber}");
    }

    [HttpPost]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.MaterialSalesCreate)]
    [ProducesResponseType(typeof(MaterialSaleDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<MaterialSaleDto>> Create([FromBody] CreateMaterialSaleCommand command)
    {
        var result = await _mediator.Send(command);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    [HttpPost("{id:guid}/lines")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.MaterialSalesCreate)]
    [ProducesResponseType(typeof(MaterialSaleDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<MaterialSaleDto>> AddLine(Guid id, [FromBody] AddMaterialSaleLineCommand command)
        => Ok(await _mediator.Send(command with { MaterialSaleId = id }));

    [HttpDelete("{id:guid}/lines/{lineId:guid}")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.MaterialSalesCreate)]
    [ProducesResponseType(typeof(MaterialSaleDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<MaterialSaleDto>> RemoveLine(Guid id, Guid lineId)
        => Ok(await _mediator.Send(new RemoveMaterialSaleLineCommand(id, lineId)));

    [HttpPut("{id:guid}/terms")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.MaterialSalesCreate)]
    [ProducesResponseType(typeof(MaterialSaleDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<MaterialSaleDto>> SetTerms(Guid id, [FromBody] SetMaterialSaleTermsCommand command)
        => Ok(await _mediator.Send(command with { Id = id }));

    [HttpPost("{id:guid}/post")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.MaterialSalesPost)]
    [ProducesResponseType(typeof(MaterialSaleDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<MaterialSaleDto>> Post(Guid id)
        => Ok(await _mediator.Send(new PostMaterialSaleCommand(id)));

    [HttpPost("{id:guid}/cancel")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.MaterialSalesCancel)]
    [ProducesResponseType(typeof(MaterialSaleDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<MaterialSaleDto>> Cancel(Guid id, [FromBody] CancelMaterialSaleBody body)
        => Ok(await _mediator.Send(new CancelMaterialSaleCommand(id, body.Reason)));
}

public record CancelMaterialSaleBody(string Reason);
