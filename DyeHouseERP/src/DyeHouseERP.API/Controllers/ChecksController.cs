using DyeHouseERP.Application.Checks.Commands;
using DyeHouseERP.Application.Checks.DTOs;
using DyeHouseERP.Application.Checks.Queries;
using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.Purchases.Commands;
using DyeHouseERP.API.Authorization;
using DyeHouseERP.API.Common;
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
    /// <summary>Initializes a new instance of the ChecksController class.</summary>
    public ChecksController(ISender mediator) => _mediator = mediator;

    /// <summary>Lists cheques.</summary>
    /// <remarks>Route: GET api/checks. Requires the 'checks.view' permission.</remarks>
    [HttpGet]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ChecksView)]
    [ProducesResponseType(typeof(List<CheckDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<List<CheckDto>>> Get(
        [FromQuery] CheckDirection? direction, [FromQuery] CheckStatus? status,
        [FromQuery] Guid? customerId, [FromQuery] Guid? supplierId,
        [FromQuery] DateTime? from, [FromQuery] DateTime? to,
        [FromQuery] DateTime? dueBefore, [FromQuery] bool? overdueOnly, [FromQuery] string? search)
        => Ok(await _mediator.Send(new GetChecksQuery(direction, status, customerId, supplierId, from, to, dueBefore, overdueOnly, search)));

    /// <summary>Gets by id.</summary>
    /// <remarks>Route: GET api/checks/{id:guid}. Requires the 'checks.view' permission.</remarks>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ChecksView)]
    public async Task<ActionResult<CheckDto>> GetById(Guid id)
        => Ok(await _mediator.Send(new GetCheckByIdQuery(id)));

    /// <summary>Checks received / issued / in hand / deposited / endorsed / cleared / bounced totals + due and overdue figures.</summary>
    [HttpGet("summary")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ChecksView)]
    public async Task<ActionResult<CheckRegisterSummaryDto>> GetSummary([FromQuery] DateTime? from, [FromQuery] DateTime? to)
        => Ok(await _mediator.Send(new GetCheckRegisterSummaryQuery(from, to)));

    /// <summary>Runs the register customer check operation.</summary>
    /// <remarks>Route: POST api/checks/customer-checks. Requires the 'checks.create' permission.</remarks>
    [HttpPost("customer-checks")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ChecksCreate)]
    [ProducesResponseType(typeof(CheckDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<CheckDto>> RegisterCustomerCheck([FromBody] RegisterCustomerCheckCommand command)
    {
        var result = await _mediator.Send(command);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    /// <summary>Runs the register supplier check operation.</summary>
    /// <remarks>Route: POST api/checks/supplier-checks. Requires the 'checks.create' permission.</remarks>
    [HttpPost("supplier-checks")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ChecksCreate)]
    [ProducesResponseType(typeof(CheckDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<CheckDto>> RegisterSupplierCheck([FromBody] RegisterSupplierCheckCommand command)
    {
        var result = await _mediator.Send(command);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    /// <summary>Confirms receipt.</summary>
    /// <remarks>Route: POST api/checks/{id:guid}/confirm-receipt. Requires the 'checks.edit' permission.</remarks>
    [HttpPost("{id:guid}/confirm-receipt")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ChecksEdit)]
    public async Task<ActionResult<CheckDto>> ConfirmReceipt(Guid id, [FromBody] MovementDateRequest? request)
        => Ok(await _mediator.Send(new ConfirmCheckReceiptCommand(id, request?.MovementDate)));

    /// <summary>Hands the SAME check to a supplier. The instrument stays one record from customer receipt to final status.</summary>
    [HttpPost("{id:guid}/endorse")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ChecksEndorse)]
    public async Task<ActionResult<CheckDto>> Endorse(Guid id, [FromBody] EndorseCheckRequest request)
        => Ok(await _mediator.Send(new EndorseCheckToSupplierCommand(id, request.SupplierId, request.MovementDate ?? DateTime.UtcNow, request.Reason)));

    /// <summary>
    /// Endorses an incoming customer check to a supplier AND settles that supplier's payable with it
    /// (spec section 39). This is the financial half of the endorsement: the supplier's payable and the
    /// customer's own account both move, and - because the instrument never passed through one of our
    /// accounts - NO cash/bank movement is posted.
    /// </summary>
    [HttpPost("{id:guid}/settle-supplier")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ChecksEndorse)]
    public async Task<ActionResult<SupplierPaymentDto>> SettleSupplier(Guid id, [FromBody] SettleSupplierRequest request)
        => Ok(await _mediator.Send(new SettleSupplierPayableWithEndorsedCheckCommand(
            id, request.SupplierId, request.SettlementDate ?? DateTime.UtcNow,
            request.SupplierInvoiceId, request.Reason, request.PaymentMethod)));

    /// <summary>Deposits cheques.</summary>
    /// <remarks>Route: POST api/checks/{id:guid}/deposit. Requires the 'checks.deposit' permission.</remarks>
    [HttpPost("{id:guid}/deposit")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ChecksDeposit)]
    public async Task<ActionResult<CheckDto>> Deposit(Guid id, [FromBody] DepositCheckRequest request)
        => Ok(await _mediator.Send(new DepositCheckCommand(id, request.TreasuryAccountId, request.MovementDate, request.Notes)));

    /// <summary>Clears cheques.</summary>
    /// <remarks>Route: POST api/checks/{id:guid}/clear. Requires the 'checks.clear' permission.</remarks>
    [HttpPost("{id:guid}/clear")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ChecksClear)]
    public async Task<ActionResult<CheckDto>> Clear(Guid id, [FromBody] ClearCheckRequest? request)
        => Ok(await _mediator.Send(new ClearCheckCommand(id, request?.MovementDate, request?.Notes, request?.TreasuryAccountId)));

    /// <summary>Bounces cheques.</summary>
    /// <remarks>Route: POST api/checks/{id:guid}/bounce. Requires the 'checks.bounce' permission.</remarks>
    [HttpPost("{id:guid}/bounce")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ChecksBounce)]
    public async Task<ActionResult<CheckDto>> Bounce(Guid id, [FromBody] ReasonRequest request)
        => Ok(await _mediator.Send(new BounceCheckCommand(id, request.Reason, request.MovementDate)));

    /// <summary>Returns cheques.</summary>
    /// <remarks>Route: POST api/checks/{id:guid}/return. Requires the 'checks.edit' permission.</remarks>
    [HttpPost("{id:guid}/return")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ChecksEdit)]
    public async Task<ActionResult<CheckDto>> Return(Guid id, [FromBody] ReasonRequest? request)
        => Ok(await _mediator.Send(new ReturnCheckToCompanyCommand(id, request?.Reason, request?.MovementDate)));

    /// <summary>Cancels cheques.</summary>
    /// <remarks>Route: POST api/checks/{id:guid}/cancel. Requires the 'checks.cancel' permission.</remarks>
    [HttpPost("{id:guid}/cancel")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ChecksCancel)]
    public async Task<ActionResult<CheckDto>> Cancel(Guid id, [FromBody] ReasonRequest request)
        => Ok(await _mediator.Send(new CancelCheckCommand(id, request.Reason, request.MovementDate)));

    /// <summary>Checks report export - respects the same filters as the list (spec sections 40 and 49).</summary>
    [HttpGet("export/excel")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
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

    /// <summary>Exports pdf.</summary>
    /// <remarks>Route: GET api/checks/export/pdf. Requires the 'checks.export' permission.</remarks>
    [HttpGet("export/pdf")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
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

    /// <summary>
    /// One cheque as a printable voucher, including its full movement history
    /// (received / endorsed / deposited / cleared / bounced / returned) so the
    /// status audit travels with the printed document (spec sections 40 + 49).
    /// </summary>
    [HttpGet("{id:guid}/pdf")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ChecksExport)]
    public async Task<IActionResult> GetPdf(Guid id, [FromServices] IReportExportService export)
    {
        var check = await _mediator.Send(new GetCheckByIdQuery(id));

        var headers = new List<string> { "Movement", "Date", "From", "To", "To holder type", "Reason" };
        var rows = check.Movements.Select(m => new object?[]
        {
            m.MovementType.ToString(), m.MovementDate.ToString("yyyy-MM-dd"), m.FromHolder, m.ToHolder,
            m.ToHolderType.ToString(), m.Reason
        }).ToList();

        var party = check.Direction == CheckDirection.CustomerCheck
            ? $"Customer: {check.CustomerCode} {check.CustomerName}"
            : $"Supplier: {check.SupplierCode} {check.SupplierName}";

        var subtitle = $"{check.CheckNumber}  |  {check.BankName} {check.BranchName}  |  {check.Amount} {check.Currency}"
            + $"  |  due {check.DueDate:yyyy-MM-dd}  |  {check.Status}  |  holder: {check.CurrentHolder} ({check.CurrentHolderType})  |  {party}";

        return ExportFileHelper.ToPdf(export, $"شيك رقم {check.CheckNumber}", subtitle, headers, rows, $"check-{check.CheckNumber}");
    }
}

/// <summary>Request payload to reason.</summary>
/// <param name="Reason">The reason.</param>
/// <param name="MovementDate">The movement date.</param>
public record ReasonRequest(string Reason, DateTime? MovementDate = null);
/// <summary>Request payload to movement date.</summary>
/// <param name="MovementDate">The movement date.</param>
public record MovementDateRequest(DateTime? MovementDate = null);
/// <summary>Request payload to endorse check.</summary>
/// <param name="SupplierId">The supplier id.</param>
/// <param name="MovementDate">The movement date.</param>
/// <param name="Reason">The reason.</param>
public record EndorseCheckRequest(Guid SupplierId, DateTime? MovementDate = null, string? Reason = null);

/// <summary>Request payload to settle supplier.</summary>
/// <param name="SupplierId">The supplier id.</param>
/// <param name="SettlementDate">The settlement date.</param>
/// <param name="SupplierInvoiceId">The supplier invoice id.</param>
/// <param name="Reason">The reason.</param>
/// <param name="PaymentMethod">The payment method.</param>
public record SettleSupplierRequest(
    Guid SupplierId, DateTime? SettlementDate = null, Guid? SupplierInvoiceId = null,
    string? Reason = null, string? PaymentMethod = null);
/// <summary>Request payload to deposit check.</summary>
/// <param name="TreasuryAccountId">The treasury account id.</param>
/// <param name="MovementDate">The movement date.</param>
/// <param name="Notes">The notes.</param>
public record DepositCheckRequest(Guid TreasuryAccountId, DateTime? MovementDate = null, string? Notes = null);
/// <summary>Request payload to clear check.</summary>
/// <param name="MovementDate">The movement date.</param>
/// <param name="Notes">The notes.</param>
/// <param name="TreasuryAccountId">The treasury account id.</param>
public record ClearCheckRequest(DateTime? MovementDate = null, string? Notes = null, Guid? TreasuryAccountId = null);
