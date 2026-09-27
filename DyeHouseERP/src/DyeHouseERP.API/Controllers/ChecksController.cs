using DyeHouseERP.Application.Checks.Commands;
using DyeHouseERP.Application.Checks.DTOs;
using DyeHouseERP.Application.Checks.Queries;
using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.API.Authorization;
using DyeHouseERP.Domain.Common;
using DyeHouseERP.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DyeHouseERP.API.Controllers;

/// <summary>
/// Checks register (spec sections 37-40). Incoming customer checks and outgoing
/// supplier checks are the same instrument type; endorsing one onward to a
/// supplier moves the same check, it never creates cash or a second check.
/// Every status change is a recorded movement with user, date and reason.
/// </summary>
[ApiController]
[Route("api/checks")]
[Authorize]
public class ChecksController : ControllerBase
{
    private readonly ISender _mediator;
    public ChecksController(ISender mediator) => _mediator = mediator;

    [HttpGet]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ChecksView)]
    [ProducesResponseType(typeof(List<CheckDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<CheckDto>>> Get(
        [FromQuery] CheckDirection? direction, [FromQuery] CheckStatus? status,
        [FromQuery] Guid? customerId, [FromQuery] Guid? supplierId,
        [FromQuery] DateTime? from, [FromQuery] DateTime? to,
        [FromQuery] DateTime? dueBefore, [FromQuery] bool? overdueOnly, [FromQuery] string? search)
        => Ok(await _mediator.Send(new GetChecksQuery(direction, status, customerId, supplierId, from, to, dueBefore, overdueOnly, search)));

    [HttpGet("{id:guid}")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ChecksView)]
    public async Task<ActionResult<CheckDto>> GetById(Guid id)
        => Ok(await _mediator.Send(new GetCheckByIdQuery(id)));

    /// <summary>Checks received / issued / in hand / deposited / endorsed / cleared / bounced totals + due and overdue figures.</summary>
    [HttpGet("summary")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ChecksView)]
    public async Task<ActionResult<CheckRegisterSummaryDto>> GetSummary([FromQuery] DateTime? from, [FromQuery] DateTime? to)
        => Ok(await _mediator.Send(new GetCheckRegisterSummaryQuery(from, to)));

    [HttpPost("customer-checks")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ChecksCreate)]
    [ProducesResponseType(typeof(CheckDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<CheckDto>> RegisterCustomerCheck([FromBody] RegisterCustomerCheckCommand command)
    {
        var result = await _mediator.Send(command);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    [HttpPost("supplier-checks")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ChecksCreate)]
    [ProducesResponseType(typeof(CheckDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<CheckDto>> RegisterSupplierCheck([FromBody] RegisterSupplierCheckCommand command)
    {
        var result = await _mediator.Send(command);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    [HttpPost("{id:guid}/confirm-receipt")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ChecksEdit)]
    public async Task<ActionResult<CheckDto>> ConfirmReceipt(Guid id, [FromBody] MovementDateRequest? request)
        => Ok(await _mediator.Send(new ConfirmCheckReceiptCommand(id, request?.MovementDate)));

    /// <summary>Hands the SAME check to a supplier. The instrument stays one record from customer receipt to final status.</summary>
    [HttpPost("{id:guid}/endorse")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ChecksEndorse)]
    public async Task<ActionResult<CheckDto>> Endorse(Guid id, [FromBody] EndorseCheckRequest request)
        => Ok(await _mediator.Send(new EndorseCheckToSupplierCommand(id, request.SupplierId, request.MovementDate ?? DateTime.UtcNow, request.Reason)));

    [HttpPost("{id:guid}/deposit")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ChecksDeposit)]
    public async Task<ActionResult<CheckDto>> Deposit(Guid id, [FromBody] DepositCheckRequest request)
        => Ok(await _mediator.Send(new DepositCheckCommand(id, request.TreasuryAccountId, request.MovementDate, request.Notes)));

    [HttpPost("{id:guid}/clear")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ChecksClear)]
    public async Task<ActionResult<CheckDto>> Clear(Guid id, [FromBody] ClearCheckRequest? request)
        => Ok(await _mediator.Send(new ClearCheckCommand(id, request?.MovementDate, request?.Notes, request?.TreasuryAccountId)));

    [HttpPost("{id:guid}/bounce")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ChecksBounce)]
    public async Task<ActionResult<CheckDto>> Bounce(Guid id, [FromBody] ReasonRequest request)
        => Ok(await _mediator.Send(new BounceCheckCommand(id, request.Reason, request.MovementDate)));

    [HttpPost("{id:guid}/return")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ChecksEdit)]
    public async Task<ActionResult<CheckDto>> Return(Guid id, [FromBody] ReasonRequest? request)
        => Ok(await _mediator.Send(new ReturnCheckToCompanyCommand(id, request?.Reason, request?.MovementDate)));

    [HttpPost("{id:guid}/cancel")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ChecksCancel)]
    public async Task<ActionResult<CheckDto>> Cancel(Guid id, [FromBody] ReasonRequest request)
        => Ok(await _mediator.Send(new CancelCheckCommand(id, request.Reason, request.MovementDate)));

    /// <summary>Checks report export - respects the same filters as the list (spec sections 40 and 49).</summary>
    [HttpGet("export/excel")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ChecksExport)]
    public async Task<IActionResult> ExportExcel(
        [FromQuery] CheckDirection? direction, [FromQuery] CheckStatus? status,
        [FromQuery] Guid? customerId, [FromQuery] Guid? supplierId,
        [FromQuery] DateTime? from, [FromQuery] DateTime? to, [FromQuery] bool? overdueOnly,
        [FromServices] IReportExportService export)
    {
        var checks = await _mediator.Send(new GetChecksQuery(direction, status, customerId, supplierId, from, to, null, overdueOnly, null));
        var headers = new List<string>
        {
            "CheckNumber", "Direction", "Status", "Bank", "Amount", "Currency", "IssueDate", "DueDate",
            "Issuer", "OriginalHolder", "CurrentHolder", "Customer", "Supplier", "Movements"
        };

        var rows = checks.Select(c => new object?[]
        {
            c.CheckNumber, c.Direction.ToString(), c.Status.ToString(), c.BankName, c.Amount, c.Currency,
            c.IssueDate.ToString("yyyy-MM-dd"), c.DueDate.ToString("yyyy-MM-dd"), c.Issuer,
            c.OriginalHolder, c.CurrentHolder, c.CustomerCode, c.SupplierCode, c.Movements.Count
        }).ToList();

        var bytes = export.GenerateExcel("Checks", headers, rows);
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "checks.xlsx");
    }

    [HttpGet("export/pdf")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ChecksExport)]
    public async Task<IActionResult> ExportPdf(
        [FromQuery] CheckDirection? direction, [FromQuery] CheckStatus? status,
        [FromQuery] Guid? customerId, [FromQuery] Guid? supplierId,
        [FromQuery] DateTime? from, [FromQuery] DateTime? to, [FromQuery] bool? overdueOnly,
        [FromServices] IReportExportService export)
    {
        var checks = await _mediator.Send(new GetChecksQuery(direction, status, customerId, supplierId, from, to, null, overdueOnly, null));
        var headers = new List<string> { "CheckNumber", "Status", "Bank", "Amount", "DueDate", "CurrentHolder", "Direction" };
        var rows = checks.Select(c => new object?[]
        {
            c.CheckNumber, c.Status.ToString(), c.BankName, c.Amount,
            c.DueDate.ToString("yyyy-MM-dd"), c.CurrentHolder, c.Direction.ToString()
        }).ToList();

        var subtitle = $"{from?.ToString("yyyy-MM-dd") ?? "..."} - {to?.ToString("yyyy-MM-dd") ?? "..."}";
        var bytes = export.GeneratePdf("Checks Register", subtitle, headers, rows);
        return File(bytes, "application/pdf", "checks.pdf");
    }
}

public record ReasonRequest(string Reason, DateTime? MovementDate = null);
public record MovementDateRequest(DateTime? MovementDate = null);
public record EndorseCheckRequest(Guid SupplierId, DateTime? MovementDate = null, string? Reason = null);
public record DepositCheckRequest(Guid TreasuryAccountId, DateTime? MovementDate = null, string? Notes = null);
public record ClearCheckRequest(DateTime? MovementDate = null, string? Notes = null, Guid? TreasuryAccountId = null);
