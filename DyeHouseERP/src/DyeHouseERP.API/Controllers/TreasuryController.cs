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

[ApiController]
[Route("api/treasury-accounts")]
[Authorize]
public class TreasuryAccountsController : ControllerBase
{
    private readonly ISender _mediator;
    public TreasuryAccountsController(ISender mediator) => _mediator = mediator;

    [HttpGet]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.TreasuryView)]
    public async Task<ActionResult<List<TreasuryAccountDto>>> Get() => Ok(await _mediator.Send(new GetTreasuryAccountsQuery()));

    [HttpPost]
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
    public async Task<ActionResult<TreasuryAccountStatementDto>> GetStatement(
        Guid id, [FromQuery] DateTime? from, [FromQuery] DateTime? to)
        => Ok(await _mediator.Send(new GetTreasuryAccountStatementQuery(id, from, to)));

    [HttpGet("{id:guid}/statement/pdf")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ReportsExport)]
    public async Task<IActionResult> GetStatementPdf(Guid id, [FromQuery] DateTime? from, [FromQuery] DateTime? to,
        [FromServices] IReportExportService export)
    {
        var statement = await _mediator.Send(new GetTreasuryAccountStatementQuery(id, from, to));
        var (headers, rows, subtitle) = BuildStatement(statement, from, to);
        return ExportFileHelper.ToPdf(export, $"كشف حساب {statement.AccountName}", subtitle, headers, rows,
            $"treasury-statement-{statement.AccountCode}");
    }

    [HttpGet("{id:guid}/statement/excel")]
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

[ApiController]
[Route("api/receipts")]
[Authorize]
public class ReceiptsController : ControllerBase
{
    private readonly ISender _mediator;
    public ReceiptsController(ISender mediator) => _mediator = mediator;

    [HttpGet]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.TreasuryView)]
    public async Task<ActionResult<List<ReceiptDto>>> Get([FromQuery] Guid? customerId) => Ok(await _mediator.Send(new GetReceiptsQuery(customerId)));

    [HttpPost]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.TreasuryCreate)]
    public async Task<ActionResult<ReceiptDto>> Create([FromBody] CreateReceiptCommand command)
    {
        var result = await _mediator.Send(command);
        return CreatedAtAction(nameof(Get), new { }, result);
    }
}

[ApiController]
[Route("api/payments")]
[Authorize]
public class PaymentsController : ControllerBase
{
    private readonly ISender _mediator;
    public PaymentsController(ISender mediator) => _mediator = mediator;

    [HttpGet]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.TreasuryView)]
    public async Task<ActionResult<List<PaymentDto>>> Get() => Ok(await _mediator.Send(new GetPaymentsQuery()));

    [HttpPost]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.TreasuryCreate)]
    public async Task<ActionResult<PaymentDto>> Create([FromBody] CreatePaymentCommand command)
    {
        var result = await _mediator.Send(command);
        return CreatedAtAction(nameof(Get), new { }, result);
    }
}

[ApiController]
[Route("api/treasury-transfers")]
[Authorize]
public class TreasuryTransfersController : ControllerBase
{
    private readonly ISender _mediator;
    public TreasuryTransfersController(ISender mediator) => _mediator = mediator;

    [HttpGet]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.TreasuryView)]
    public async Task<ActionResult<List<TreasuryTransferDto>>> Get() => Ok(await _mediator.Send(new GetTreasuryTransfersQuery()));

    [HttpPost]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.TreasuryCreate)]
    public async Task<ActionResult<TreasuryTransferDto>> Create([FromBody] CreateTreasuryTransferCommand command)
    {
        var result = await _mediator.Send(command);
        return CreatedAtAction(nameof(Get), new { }, result);
    }
}
