using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.Invoices.Commands;
using DyeHouseERP.Application.Invoices.DTOs;
using DyeHouseERP.Application.Invoices.Queries;
using DyeHouseERP.Domain.Enums;
using DyeHouseERP.API.Authorization;
using DyeHouseERP.Domain.Common;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DyeHouseERP.API.Controllers;

[ApiController]
[Route("api/invoices")]
[Authorize]
public class InvoicesController : ControllerBase
{
    private readonly ISender _mediator;
    public InvoicesController(ISender mediator) => _mediator = mediator;

    [HttpGet]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.InvoicesView)]
    public async Task<ActionResult<List<InvoiceDto>>> Get([FromQuery] Guid? customerId, [FromQuery] InvoiceStatus? status)
        => Ok(await _mediator.Send(new GetInvoicesQuery(customerId, status)));

    [HttpGet("{id:guid}")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.InvoicesView)]
    public async Task<ActionResult<InvoiceDto>> GetById(Guid id) => Ok(await _mediator.Send(new GetInvoiceByIdQuery(id)));

    /// <summary>Printable single-invoice PDF (spec section 39) - customer, date, line items, totals, ready for print/download.</summary>
    [HttpGet("{id:guid}/pdf")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ReportsExport)]
    public async Task<IActionResult> GetInvoicePdf(Guid id, [FromServices] IReportExportService export)
    {
        var invoice = await _mediator.Send(new GetInvoiceByIdQuery(id));

        var headers = new List<string> { "Item", "Color", "Quantity", "Price", "Value" };
        var rows = invoice.Lines.Select(l => new object?[] { l.ItemCode, l.Color, l.Quantity, l.ProcessingPrice, l.Value }).ToList();
        rows.Add(new object?[] { "", "", "", "Subtotal", invoice.SubTotal });
        rows.Add(new object?[] { "", "", "", "Discount", invoice.Discount });
        rows.Add(new object?[] { "", "", "", "Tax", invoice.Tax });
        rows.Add(new object?[] { "", "", "", "Total", invoice.Total });

        var subtitle = $"{invoice.CustomerCode} - {invoice.CustomerName}  |  {invoice.InvoiceDate:yyyy-MM-dd}  |  {invoice.Status}";
        var bytes = export.GeneratePdf($"فاتورة رقم {invoice.InvoiceNumber}", subtitle, headers, rows);
        return File(bytes, "application/pdf", $"invoice-{invoice.InvoiceNumber}.pdf");
    }

    [HttpPost]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.InvoicesCreate)]
    public async Task<ActionResult<InvoiceDto>> Create([FromBody] CreateInvoiceCommand command)
    {
        var result = await _mediator.Send(command);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    [HttpPost("{id:guid}/issue")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.InvoicesIssue)]
    public async Task<ActionResult<InvoiceDto>> Issue(Guid id) => Ok(await _mediator.Send(new IssueInvoiceCommand(id)));

    [HttpPost("{id:guid}/cancel")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.InvoicesCancel)]
    public async Task<ActionResult<InvoiceDto>> Cancel(Guid id, [FromBody] CancelInvoiceRequest request)
        => Ok(await _mediator.Send(new CancelInvoiceCommand(id, request.Reason)));
}

public record CancelInvoiceRequest(string Reason);

[ApiController]
[Route("api/customers/{customerId:guid}/statement")]
[Authorize]
public class CustomerStatementController : ControllerBase
{
    private readonly ISender _mediator;
    private readonly IApplicationDbContext _db;
    private readonly IReportExportService _export;

    public CustomerStatementController(ISender mediator, IApplicationDbContext db, IReportExportService export)
    {
        _mediator = mediator;
        _db = db;
        _export = export;
    }

    [HttpGet]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.InvoicesView)]
    public async Task<ActionResult<List<CustomerStatementLineDto>>> Get(Guid customerId, [FromQuery] DateTime? from, [FromQuery] DateTime? to)
        => Ok(await _mediator.Send(new GetCustomerStatementQuery(customerId, from, to)));

    /// <summary>Customer statement as a downloadable PDF (spec section 33: "Must support: date filtering, PDF, Excel, printing").</summary>
    [HttpGet("pdf")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ReportsExport)]
    public async Task<IActionResult> GetPdf(Guid customerId, [FromQuery] DateTime? from, [FromQuery] DateTime? to)
    {
        var (headers, rows, customerLabel) = await BuildReportDataAsync(customerId, from, to);
        var bytes = _export.GeneratePdf($"كشف حساب العميل - {customerLabel}", DateRangeLabel(from, to), headers, rows);
        return File(bytes, "application/pdf", $"customer-statement-{customerId}.pdf");
    }

    /// <summary>Customer statement as a downloadable Excel workbook (spec section 33).</summary>
    [HttpGet("excel")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ReportsExport)]
    public async Task<IActionResult> GetExcel(Guid customerId, [FromQuery] DateTime? from, [FromQuery] DateTime? to)
    {
        var (headers, rows, customerLabel) = await BuildReportDataAsync(customerId, from, to);
        var bytes = _export.GenerateExcel("Statement", headers, rows);
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"customer-statement-{customerId}.xlsx");
    }

    private async Task<(List<string> Headers, List<object?[]> Rows, string CustomerLabel)> BuildReportDataAsync(Guid customerId, DateTime? from, DateTime? to)
    {
        var lines = await _mediator.Send(new GetCustomerStatementQuery(customerId, from, to));
        var customer = await _db.Customers.FindAsync(customerId);

        var headers = new List<string> { "Date", "Description", "Document", "Debit", "Credit", "Balance" };
        var rows = lines.Select(l => new object?[]
        {
            l.Date.ToString("yyyy-MM-dd"), l.Description, l.DocumentNumber, l.Debit, l.Credit, l.RunningBalance
        }).ToList();

        return (headers, rows, customer?.Code ?? customerId.ToString());
    }

    private static string DateRangeLabel(DateTime? from, DateTime? to) =>
        from.HasValue || to.HasValue ? $"{from?.ToString("yyyy-MM-dd") ?? "..."} - {to?.ToString("yyyy-MM-dd") ?? "..."}" : "";
}
