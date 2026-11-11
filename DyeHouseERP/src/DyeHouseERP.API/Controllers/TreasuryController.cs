using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.Treasury.Commands;
using DyeHouseERP.Application.Treasury.DTOs;
using DyeHouseERP.Application.Treasury.Queries;
using DyeHouseERP.API.Authorization;
using DyeHouseERP.API.Common;
using DyeHouseERP.Domain.Common;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DyeHouseERP.API.Controllers;

/// <summary>Hosts the treasury accounts endpoints.</summary>
[ApiController]
[Route("api/treasury-accounts")]
[Authorize]
public class TreasuryAccountsController : ControllerBase
{
    private readonly ISender _mediator;
    /// <summary>Initializes a new instance of the TreasuryAccountsController class.</summary>
    public TreasuryAccountsController(ISender mediator) => _mediator = mediator;

    /// <summary>Lists treasury accounts.</summary>
    /// <remarks>Route: GET api/treasury-accounts. Requires the 'treasury.view' permission.</remarks>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.TreasuryView)]
    public async Task<ActionResult<List<TreasuryAccountDto>>> Get() => Ok(await _mediator.Send(new GetTreasuryAccountsQuery()));

    /// <summary>Creates treasury accounts.</summary>
    /// <remarks>Route: POST api/treasury-accounts. Requires the 'treasury.create' permission.</remarks>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.TreasuryCreate)]
    public async Task<ActionResult<TreasuryAccountDto>> Create([FromBody] CreateTreasuryAccountCommand command)
    {
        var result = await _mediator.Send(command);
        return CreatedAtAction(nameof(Get), new { }, result);
    }

    // ------------------------------------------------ account statement (spec section 41)

    /// <summary>
    /// Cash/bank account statement: opening balance, every movement with its source
    /// document, and a running balance - all summed from the append-only ledger.
    /// </summary>
    [HttpGet("{id:guid}/statement")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.TreasuryView)]
    [ProducesResponseType(typeof(TreasuryAccountStatementDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TreasuryAccountStatementDto>> GetStatement(
        Guid id, [FromQuery] DateTime? from, [FromQuery] DateTime? to)
        => Ok(await _mediator.Send(new GetTreasuryAccountStatementQuery(id, from, to)));

    /// <summary>Gets statement pdf.</summary>
    /// <remarks>Route: GET api/treasury-accounts/{id:guid}/statement/pdf. Requires the 'reports.export' permission.</remarks>
    [HttpGet("{id:guid}/statement/pdf")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ReportsExport)]
    public async Task<IActionResult> GetStatementPdf(Guid id, [FromQuery] DateTime? from, [FromQuery] DateTime? to,
        [FromServices] IReportExportService export)
    {
        var statement = await _mediator.Send(new GetTreasuryAccountStatementQuery(id, from, to));
        var (headers, rows, subtitle) = BuildStatement(statement, from, to);
        return ExportFileHelper.ToPdf(export, $"كشف حساب {statement.AccountName}", subtitle, headers, rows,
            $"treasury-statement-{statement.AccountCode}");
    }

    /// <summary>Gets statement excel.</summary>
    /// <remarks>Route: GET api/treasury-accounts/{id:guid}/statement/excel. Requires the 'reports.export' permission.</remarks>
    [HttpGet("{id:guid}/statement/excel")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ReportsExport)]
    public async Task<IActionResult> GetStatementExcel(Guid id, [FromQuery] DateTime? from, [FromQuery] DateTime? to,
        [FromServices] IReportExportService export)
    {
        var statement = await _mediator.Send(new GetTreasuryAccountStatementQuery(id, from, to));
        var (headers, rows, _) = BuildStatement(statement, from, to);
        return ExportFileHelper.ToFile(export, "excel", "Treasury Account Statement", $"{statement.AccountCode} - {statement.AccountName}",
            headers, rows, $"treasury-statement-{statement.AccountCode}", "Statement");
    }

    private static (List<string> Headers, List<object?[]> Rows, string Subtitle) BuildStatement(
        TreasuryAccountStatementDto statement, DateTime? from, DateTime? to)
    {
        var headers = new List<string> { "Date", "Document", "Type", "Description", "Debit (out)", "Credit (in)", "Balance", "User" };
        var rows = new List<object?[]>
        {
            new object?[] { "", "", "", "Opening balance", null, null, statement.OpeningBalance, "" }
        };
        rows.AddRange(statement.Lines.Select(l => new object?[]
        {
            l.TransactionDate.ToString("yyyy-MM-dd"), l.SourceDocumentNumber, l.SourceDocumentType.ToString(),
            l.Description, l.Debit == 0 ? null : l.Debit, l.Credit == 0 ? null : l.Credit, l.RunningBalance, l.CreatedBy
        }));
        rows.Add(new object?[] { "", "", "", "Closing balance", statement.TotalOut, statement.TotalIn, statement.ClosingBalance, "" });

        var range = from.HasValue || to.HasValue
            ? $"{from?.ToString("yyyy-MM-dd") ?? "..."} - {to?.ToString("yyyy-MM-dd") ?? "..."}"
            : "All movements";
        var subtitle = $"{statement.AccountCode} - {statement.AccountName} ({statement.Kind})  |  {range}";
        return (headers, rows, subtitle);
    }
}

/// <summary>Hosts the purchase receipts endpoints.</summary>
[ApiController]
[Route("api/receipts")]
[Authorize]
public class ReceiptsController : ControllerBase
{
    private readonly ISender _mediator;
    /// <summary>Initializes a new instance of the ReceiptsController class.</summary>
    public ReceiptsController(ISender mediator) => _mediator = mediator;

    /// <summary>Lists treasury accounts.</summary>
    /// <remarks>Route: GET api/treasury-accounts. Requires the 'treasury.view' permission.</remarks>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.TreasuryView)]
    public async Task<ActionResult<List<ReceiptDto>>> Get([FromQuery] Guid? customerId) => Ok(await _mediator.Send(new GetReceiptsQuery(customerId)));

    /// <summary>Creates treasury accounts.</summary>
    /// <remarks>Route: POST api/treasury-accounts. Requires the 'treasury.create' permission.</remarks>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.TreasuryCreate)]
    public async Task<ActionResult<ReceiptDto>> Create([FromBody] CreateReceiptCommand command)
    {
        var result = await _mediator.Send(command);
        return CreatedAtAction(nameof(Get), new { }, result);
    }

    /// <summary>Cancels a posted receipt and writes the reversal rows (B4). Reason required; double cancel rejected.</summary>
    [HttpPost("{id:guid}/cancel")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.TreasuryCancel)]
    [ProducesResponseType(typeof(ReceiptDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ReceiptDto>> Cancel(Guid id, [FromBody] CancelReceiptRequest request)
        => Ok(await _mediator.Send(new CancelReceiptCommand(id, request.Reason)));
}

/// <summary>Request payload to cancel receipt.</summary>
/// <param name="Reason">The reason.</param>
public record CancelReceiptRequest(string Reason);

/// <summary>Hosts the supplier payments endpoints.</summary>
[ApiController]
[Route("api/payments")]
[Authorize]
public class PaymentsController : ControllerBase
{
    private readonly ISender _mediator;
    /// <summary>Initializes a new instance of the PaymentsController class.</summary>
    public PaymentsController(ISender mediator) => _mediator = mediator;

    /// <summary>Lists treasury accounts.</summary>
    /// <remarks>Route: GET api/treasury-accounts. Requires the 'treasury.view' permission.</remarks>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.TreasuryView)]
    public async Task<ActionResult<List<PaymentDto>>> Get() => Ok(await _mediator.Send(new GetPaymentsQuery()));

    /// <summary>Creates treasury accounts.</summary>
    /// <remarks>Route: POST api/treasury-accounts. Requires the 'treasury.create' permission.</remarks>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.TreasuryCreate)]
    public async Task<ActionResult<PaymentDto>> Create([FromBody] CreatePaymentCommand command)
    {
        var result = await _mediator.Send(command);
        return CreatedAtAction(nameof(Get), new { }, result);
    }

    /// <summary>Cancels a posted payment and writes the reversal rows (B4). Reason required; double cancel rejected.</summary>
    [HttpPost("{id:guid}/cancel")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.TreasuryCancel)]
    [ProducesResponseType(typeof(PaymentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<PaymentDto>> Cancel(Guid id, [FromBody] CancelPaymentRequest request)
        => Ok(await _mediator.Send(new CancelPaymentCommand(id, request.Reason)));
}

/// <summary>Request payload to cancel payment.</summary>
/// <param name="Reason">The reason.</param>
public record CancelPaymentRequest(string Reason);

/// <summary>Hosts the treasury transfers endpoints.</summary>
[ApiController]
[Route("api/treasury-transfers")]
[Authorize]
public class TreasuryTransfersController : ControllerBase
{
    private readonly ISender _mediator;
    /// <summary>Initializes a new instance of the TreasuryTransfersController class.</summary>
    public TreasuryTransfersController(ISender mediator) => _mediator = mediator;

    /// <summary>Lists treasury accounts.</summary>
    /// <remarks>Route: GET api/treasury-accounts. Requires the 'treasury.view' permission.</remarks>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.TreasuryView)]
    public async Task<ActionResult<List<TreasuryTransferDto>>> Get() => Ok(await _mediator.Send(new GetTreasuryTransfersQuery()));

    /// <summary>Creates treasury accounts.</summary>
    /// <remarks>Route: POST api/treasury-accounts. Requires the 'treasury.create' permission.</remarks>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.TreasuryCreate)]
    public async Task<ActionResult<TreasuryTransferDto>> Create([FromBody] CreateTreasuryTransferCommand command)
    {
        var result = await _mediator.Send(command);
        return CreatedAtAction(nameof(Get), new { }, result);
    }

    /// <summary>Cancels a posted transfer and writes the two opposite ledger rows (B4). Reason required; double cancel rejected.</summary>
    [HttpPost("{id:guid}/cancel")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.TreasuryCancel)]
    [ProducesResponseType(typeof(TreasuryTransferDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<TreasuryTransferDto>> Cancel(Guid id, [FromBody] CancelTreasuryTransferRequest request)
        => Ok(await _mediator.Send(new CancelTreasuryTransferCommand(id, request.Reason)));
}

/// <summary>Request payload to cancel treasury transfer.</summary>
/// <param name="Reason">The reason.</param>
public record CancelTreasuryTransferRequest(string Reason);
