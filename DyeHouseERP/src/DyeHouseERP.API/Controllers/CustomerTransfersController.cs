using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.CustomerTransfers.Commands;
using DyeHouseERP.Application.CustomerTransfers.DTOs;
using DyeHouseERP.Application.CustomerTransfers.Queries;
using DyeHouseERP.API.Authorization;
using DyeHouseERP.API.Common;
using DyeHouseERP.Domain.Common;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DyeHouseERP.API.Controllers;

/// <summary>Customer-to-customer raw material ownership transfer (spec section 15).</summary>
[ApiController]
[Route("api/customer-transfers")]
[Authorize]
public class CustomerTransfersController : ControllerBase
{
    private readonly ISender _mediator;
    public CustomerTransfersController(ISender mediator) => _mediator = mediator;

    /// <summary>Customer-to-customer raw material transfers as Excel or PDF (spec section 15).</summary>
    [HttpGet("export")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ReportsExport)]
    public async Task<IActionResult> Export([FromQuery] Guid? customerId, [FromQuery] string format = "excel",
        [FromServices] IReportExportService export = null!)
    {
        var transfers = await _mediator.Send(new GetCustomerTransfersQuery(customerId));

        var headers = new[] { "Transfer no.", "Date", "From customer", "To customer", "Message", "Item", "Qty KG", "Qty M", "Reason", "By" };
        var rows = transfers.Select(t => new object?[]
        {
            t.TransferNumber, t.TransferDate.ToString("yyyy-MM-dd"),
            $"{t.FromCustomerCode} - {t.FromCustomerName}", $"{t.ToCustomerCode} - {t.ToCustomerName}",
            t.MessageNumber, $"{t.ItemCode} - {t.ItemName}", t.QuantityKg, t.QuantityMeter, t.Reason, t.CreatedBy
        }).ToList();

        return ExportFileHelper.ToFile(export, format, "Customer Transfers", "DyeHouse ERP", headers, rows, "customer-transfers", "Transfers");
    }

    /// <summary>List customer-to-customer transfers, newest first.</summary>
    [HttpGet]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.RawTransfer)]
    [ProducesResponseType(typeof(List<CustomerTransferDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<CustomerTransferDto>>> Get([FromQuery] Guid? customerId)
        => Ok(await _mediator.Send(new GetCustomerTransfersQuery(customerId)));

    [HttpPost]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.RawTransfer)]
    [ProducesResponseType(typeof(CustomerTransferDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<CustomerTransferDto>> Create([FromBody] CreateCustomerTransferCommand command)
    {
        var result = await _mediator.Send(command);
        return CreatedAtAction(nameof(Get), new { }, result);
    }
}
