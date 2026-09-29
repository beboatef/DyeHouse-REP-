using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.Purchases.Commands;
using DyeHouseERP.Application.Purchases.DTOs;
using DyeHouseERP.Application.Purchases.Queries;
using DyeHouseERP.API.Authorization;
using DyeHouseERP.API.Common;
using DyeHouseERP.Domain.Common;
using DyeHouseERP.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DyeHouseERP.API.Controllers;

/// <summary>
/// Purchases (spec section 35): suppliers, purchase orders, goods receiving,
/// supplier invoices and supplier payments, plus the supplier account statement
/// and outstanding balances (spec sections 41 and 48).
///
/// Every action carries its own granular permission - approving an order,
/// receiving stock, recording an invoice and paying a supplier are four
/// different authorities, not one "purchases" switch.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class PurchasesController : ControllerBase
{
    private readonly ISender _mediator;
    public PurchasesController(ISender mediator) => _mediator = mediator;

    // ------------------------------------------------------------- orders

    [HttpGet("orders")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.PurchasesView)]
    [ProducesResponseType(typeof(List<PurchaseOrderDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<PurchaseOrderDto>>> GetOrders(
        [FromQuery] Guid? supplierId, [FromQuery] PurchaseOrderStatus? status,
        [FromQuery] DateTime? from, [FromQuery] DateTime? to)
        => Ok(await _mediator.Send(new GetPurchaseOrdersQuery(supplierId, status, from, to)));

    [HttpGet("orders/{id:guid}")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.PurchasesView)]
    [ProducesResponseType(typeof(PurchaseOrderDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<PurchaseOrderDto>> GetOrder(Guid id)
        => Ok(await _mediator.Send(new GetPurchaseOrderByIdQuery(id)));

    [HttpPost("orders")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.PurchasesCreate)]
    [ProducesResponseType(typeof(PurchaseOrderDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<PurchaseOrderDto>> CreateOrder([FromBody] CreatePurchaseOrderCommand command)
    {
        var result = await _mediator.Send(command);
        return CreatedAtAction(nameof(GetOrder), new { id = result.Id }, result);
    }

    [HttpPut("orders/{id:guid}")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.PurchasesEdit)]
    [ProducesResponseType(typeof(PurchaseOrderDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<PurchaseOrderDto>> UpdateOrder(Guid id, [FromBody] UpdatePurchaseOrderCommand command)
    {
        if (id != command.Id) command = command with { Id = id };
        return Ok(await _mediator.Send(command));
    }

    [HttpPost("orders/{id:guid}/lines")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.PurchasesEdit)]
    [ProducesResponseType(typeof(PurchaseOrderDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<PurchaseOrderDto>> AddOrderLine(Guid id, [FromBody] AddPurchaseOrderLineCommand command)
    {
        if (id != command.OrderId) command = command with { OrderId = id };
        return Ok(await _mediator.Send(command));
    }

    [HttpPut("orders/{id:guid}/lines/{lineId:guid}")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.PurchasesEdit)]
    [ProducesResponseType(typeof(PurchaseOrderDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<PurchaseOrderDto>> UpdateOrderLine(Guid id, Guid lineId, [FromBody] UpdatePurchaseOrderLineCommand command)
    {
        if (id != command.OrderId || lineId != command.LineId) command = command with { OrderId = id, LineId = lineId };
        return Ok(await _mediator.Send(command));
    }

    [HttpDelete("orders/{id:guid}/lines/{lineId:guid}")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.PurchasesEdit)]
    [ProducesResponseType(typeof(PurchaseOrderDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<PurchaseOrderDto>> RemoveOrderLine(Guid id, Guid lineId)
        => Ok(await _mediator.Send(new RemovePurchaseOrderLineCommand(id, lineId)));

    [HttpPost("orders/{id:guid}/submit")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.PurchasesSubmit)]
    [ProducesResponseType(typeof(PurchaseOrderDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<PurchaseOrderDto>> SubmitOrder(Guid id)
        => Ok(await _mediator.Send(new SubmitPurchaseOrderCommand(id)));

    [HttpPost("orders/{id:guid}/approve")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.PurchasesApprove)]
    [ProducesResponseType(typeof(PurchaseOrderDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<PurchaseOrderDto>> ApproveOrder(Guid id)
        => Ok(await _mediator.Send(new ApprovePurchaseOrderCommand(id)));

    [HttpPost("orders/{id:guid}/cancel")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.PurchasesCancel)]
    [ProducesResponseType(typeof(PurchaseOrderDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<PurchaseOrderDto>> CancelOrder(Guid id, [FromBody] CancelRequest request)
        => Ok(await _mediator.Send(new CancelPurchaseOrderCommand(id, request.Reason)));

    // ----------------------------------------------------------- receipts

    [HttpGet("receipts")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.PurchasesView)]
    [ProducesResponseType(typeof(List<PurchaseReceiptDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<PurchaseReceiptDto>>> GetReceipts(
        [FromQuery] Guid? supplierId, [FromQuery] Guid? purchaseOrderId,
        [FromQuery] DateTime? from, [FromQuery] DateTime? to)
        => Ok(await _mediator.Send(new GetPurchaseReceiptsQuery(supplierId, purchaseOrderId, from, to)));

    [HttpGet("receipts/{id:guid}")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.PurchasesView)]
    [ProducesResponseType(typeof(PurchaseReceiptDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<PurchaseReceiptDto>> GetReceipt(Guid id)
        => Ok(await _mediator.Send(new GetPurchaseReceiptByIdQuery(id)));

    /// <summary>Receives factory-owned materials into a warehouse - the only action that moves purchase stock.</summary>
    [HttpPost("receipts")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.PurchasesReceive)]
    [ProducesResponseType(typeof(PurchaseReceiptDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<PurchaseReceiptDto>> CreateReceipt([FromBody] CreatePurchaseReceiptCommand command)
    {
        var result = await _mediator.Send(command);
        return CreatedAtAction(nameof(GetReceipt), new { id = result.Id }, result);
    }

    // --------------------------------------------------- supplier invoices

    [HttpGet("supplier-invoices")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.PurchasesView)]
    [ProducesResponseType(typeof(List<SupplierInvoiceDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<SupplierInvoiceDto>>> GetInvoices(
        [FromQuery] Guid? supplierId, [FromQuery] SupplierInvoiceStatus? status,
        [FromQuery] bool? overdueOnly, [FromQuery] DateTime? asOf)
        => Ok(await _mediator.Send(new GetSupplierInvoicesQuery(supplierId, status, overdueOnly, asOf)));

    [HttpGet("supplier-invoices/{id:guid}")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.PurchasesView)]
    [ProducesResponseType(typeof(SupplierInvoiceDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<SupplierInvoiceDto>> GetInvoice(Guid id)
        => Ok(await _mediator.Send(new GetSupplierInvoiceByIdQuery(id)));

    [HttpPost("supplier-invoices")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.PurchasesInvoice)]
    [ProducesResponseType(typeof(SupplierInvoiceDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<SupplierInvoiceDto>> CreateInvoice([FromBody] CreateSupplierInvoiceCommand command)
    {
        var result = await _mediator.Send(command);
        return CreatedAtAction(nameof(GetInvoice), new { id = result.Id }, result);
    }

    [HttpPut("supplier-invoices/{id:guid}")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.PurchasesInvoice)]
    [ProducesResponseType(typeof(SupplierInvoiceDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<SupplierInvoiceDto>> UpdateInvoice(Guid id, [FromBody] UpdateSupplierInvoiceCommand command)
    {
        if (id != command.Id) command = command with { Id = id };
        return Ok(await _mediator.Send(command));
    }

    /// <summary>Posts the invoice - the moment it becomes a payable on the supplier account.</summary>
    [HttpPost("supplier-invoices/{id:guid}/post")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.PurchasesInvoice)]
    [ProducesResponseType(typeof(SupplierInvoiceDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<SupplierInvoiceDto>> PostInvoice(Guid id)
        => Ok(await _mediator.Send(new PostSupplierInvoiceCommand(id)));

    [HttpPost("supplier-invoices/{id:guid}/cancel")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.PurchasesCancel)]
    [ProducesResponseType(typeof(SupplierInvoiceDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<SupplierInvoiceDto>> CancelInvoice(Guid id, [FromBody] CancelRequest request)
        => Ok(await _mediator.Send(new CancelSupplierInvoiceCommand(id, request.Reason)));

    // --------------------------------------------------- supplier payments

    [HttpGet("supplier-payments")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.PurchasesView)]
    [ProducesResponseType(typeof(List<SupplierPaymentDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<SupplierPaymentDto>>> GetPayments(
        [FromQuery] Guid? supplierId, [FromQuery] DateTime? from, [FromQuery] DateTime? to)
        => Ok(await _mediator.Send(new GetSupplierPaymentsQuery(supplierId, from, to)));

    [HttpPost("supplier-payments")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.PurchasesPay)]
    [ProducesResponseType(typeof(SupplierPaymentDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<SupplierPaymentDto>> PaySupplier([FromBody] PaySupplierCommand command)
    {
        var result = await _mediator.Send(command);
        return CreatedAtAction(nameof(GetPayments), new { }, result);
    }

    /// <summary>Cancels a posted supplier payment and writes the reversal rows (B4/B5). Reason required; double cancel rejected.</summary>
    [HttpPost("supplier-payments/{id:guid}/cancel")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.PurchasesPay)]
    [ProducesResponseType(typeof(SupplierPaymentDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<SupplierPaymentDto>> CancelSupplierPayment(Guid id, [FromBody] CancelSupplierPaymentRequest request)
        => Ok(await _mediator.Send(new CancelSupplierPaymentCommand(id, request.Reason)));

    // -------------------------------------------------- accounts / reports

    /// <summary>Supplier account statement (spec sections 41 and 48) - a live sum over the append-only ledger.</summary>
    [HttpGet("suppliers/{supplierId:guid}/ledger")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.PurchasesView)]
    [ProducesResponseType(typeof(List<SupplierLedgerEntryDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<SupplierLedgerEntryDto>>> GetSupplierLedger(
        Guid supplierId, [FromQuery] DateTime? from, [FromQuery] DateTime? to)
        => Ok(await _mediator.Send(new GetSupplierLedgerQuery(supplierId, from, to)));

    // ------------------------------------------------------------- exports (spec sections 35 + 49)

    /// <summary>One purchase order as a printable document (spec section 39).</summary>
    [HttpGet("orders/{id:guid}/pdf")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.PurchasesView)]
    public async Task<IActionResult> GetOrderPdf(Guid id, [FromServices] IReportExportService export)
    {
        var order = await _mediator.Send(new GetPurchaseOrderByIdQuery(id));

        var headers = new[] { "Material", "Quantity", "Unit", "Unit price", "Line value", "Received", "Outstanding" };
        var rows = order.Lines.Select(l => new object?[]
        {
            $"{l.MaterialCode} - {l.MaterialName}", l.Quantity, l.Unit.ToString(), l.UnitPrice, l.LineValue,
            l.ReceivedQuantity, l.OutstandingQuantity
        }).ToList();
        rows.Add(new object?[] { "", "", "", "", "Total", null, null });

        var subtitle = $"{order.SupplierCode} - {order.SupplierName}  |  {order.OrderDate:yyyy-MM-dd}  |  {order.WarehouseName}  |  {order.Status}";
        return ExportFileHelper.ToPdf(export, $"أمر توريد رقم {order.OrderNumber}", subtitle, headers, rows, $"purchase-order-{order.OrderNumber}");
    }

    /// <summary>One goods receipt as a printable document (spec section 39) - the document the storeroom signs.</summary>
    [HttpGet("receipts/{id:guid}/pdf")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.PurchasesView)]
    public async Task<IActionResult> GetReceiptPdf(Guid id, [FromServices] IReportExportService export)
    {
        var receipt = await _mediator.Send(new GetPurchaseReceiptByIdQuery(id));

        var headers = new[] { "Material", "Quantity", "Unit", "Unit cost", "Line value", "Notes" };
        var rows = receipt.Lines.Select(l => new object?[]
        {
            $"{l.MaterialCode} - {l.MaterialName}", l.Quantity, l.Unit.ToString(), l.UnitCost, l.LineValue, l.Notes
        }).ToList();
        rows.Add(new object?[] { "Supplier doc.", receipt.SupplierDocumentNumber, "", "", "Total", receipt.TotalValue });

        var subtitle = $"{receipt.SupplierCode} - {receipt.SupplierName}  |  {receipt.ReceiptDate:yyyy-MM-dd}  |  {receipt.WarehouseName}"
            + $"  |  order: {receipt.OrderNumber ?? "-"}  |  received by {receipt.ReceivedBy}";
        return ExportFileHelper.ToPdf(export, $"استلام مشتريات رقم {receipt.ReceiptNumber}", subtitle, headers, rows, $"purchase-receipt-{receipt.ReceiptNumber}");
    }

    /// <summary>One supplier invoice as a printable document (spec section 39).</summary>
    [HttpGet("supplier-invoices/{id:guid}/pdf")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.PurchasesView)]
    public async Task<IActionResult> GetSupplierInvoicePdf(Guid id, [FromServices] IReportExportService export)
    {
        var invoice = await _mediator.Send(new GetSupplierInvoiceByIdQuery(id));

        var headers = new[] { "Material", "Description", "Quantity", "Unit", "Unit price", "Line value" };
        var rows = invoice.Lines.Select(l => new object?[]
        {
            l.MaterialCode, l.Description, l.Quantity, l.Unit.ToString(), l.UnitPrice, l.LineValue
        }).ToList();
        rows.Add(new object?[] { "", "Subtotal", "", "", "", invoice.SubTotal });
        rows.Add(new object?[] { "", "Discount", "", "", "", invoice.Discount });
        rows.Add(new object?[] { "", "Tax", "", "", "", invoice.Tax });
        rows.Add(new object?[] { "", "Total", "", "", "", invoice.Total });

        var subtitle = $"{invoice.SupplierCode} - {invoice.SupplierName}  |  {invoice.InvoiceDate:yyyy-MM-dd}  |  due {invoice.DueDate:yyyy-MM-dd}"
            + $"  |  internal: {invoice.InternalNumber ?? "-"}  |  {invoice.Status}  |  {invoice.Currency}";
        return ExportFileHelper.ToPdf(export, $"فاتورة مورد رقم {invoice.InvoiceNumber}", subtitle, headers, rows, $"supplier-invoice-{invoice.InvoiceNumber}");
    }

    /// <summary>
    /// Supplier account statement as Excel/PDF, honouring the same date range as the
    /// ledger query. The opening balance is the true ledger balance carried in from
    /// before the requested window - not a re-based zero.
    /// </summary>
    [HttpGet("suppliers/{supplierId:guid}/ledger/export")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.PurchasesExport)]
    public async Task<IActionResult> ExportSupplierLedger(Guid supplierId,
        [FromQuery] DateTime? from, [FromQuery] DateTime? to, [FromQuery] string format = "excel",
        [FromServices] IReportExportService export = null!)
    {
        var all = await _mediator.Send(new GetSupplierLedgerQuery(supplierId, null, null));

        var opening = all.Where(e => from.HasValue && e.EntryDate < from.Value).Sum(e => e.Debit - e.Credit);
        var window = all.Where(e => (!from.HasValue || e.EntryDate >= from.Value) && (!to.HasValue || e.EntryDate <= to.Value)).ToList();
        var closing = opening + window.Sum(e => e.Debit - e.Credit);

        var headers = new[] { "Date", "Document", "Type", "Description", "Debit (owed)", "Credit (paid)", "Balance", "User" };
        var rows = new List<object?[]>
        {
            new object?[] { "", "", "", "Opening balance", null, null, opening, "" }
        };

        var running = opening;
        foreach (var entry in window)
        {
            running += entry.Debit - entry.Credit;
            rows.Add(new object?[]
            {
                entry.EntryDate.ToString("yyyy-MM-dd"), entry.SourceDocumentNumber, entry.SourceDocumentType, entry.Description,
                entry.Debit == 0 ? null : entry.Debit, entry.Credit == 0 ? null : entry.Credit, running, entry.CreatedBy
            });
        }

        rows.Add(new object?[] { "", "", "", "Closing balance", null, null, closing, "" });

        var supplier = all.FirstOrDefault();
        var name = supplier is null ? "Supplier" : $"{supplier.SupplierCode} - {supplier.SupplierName}";
        var subtitle = from.HasValue || to.HasValue
            ? $"{from?.ToString("yyyy-MM-dd") ?? "..."} - {to?.ToString("yyyy-MM-dd") ?? "..."}"
            : "All movements";

        return ExportFileHelper.ToFile(export, format, $"Supplier statement {name}", subtitle, headers, rows,
            $"supplier-statement-{supplier?.SupplierCode ?? supplierId.ToString()}", "Statement");
    }

    /// <summary>Purchase orders as Excel/PDF, honouring the same filters as the list.</summary>
    [HttpGet("orders/export")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.PurchasesExport)]
    public async Task<IActionResult> ExportOrders([FromQuery] Guid? supplierId, [FromQuery] PurchaseOrderStatus? status,
        [FromQuery] DateTime? from, [FromQuery] DateTime? to, [FromQuery] string format = "excel",
        [FromServices] IReportExportService export = null!)
    {
        var orders = await _mediator.Send(new GetPurchaseOrdersQuery(supplierId, status, from, to));

        var headers = new[] { "Order no.", "Date", "Supplier", "Warehouse", "Status", "Lines", "Total" };
        var rows = orders.Select(o => new object?[]
        {
            o.OrderNumber, o.OrderDate.ToString("yyyy-MM-dd"), o.SupplierName, o.WarehouseName,
            o.Status.ToString(), o.Lines.Count, o.TotalValue
        }).ToList();

        return ExportFile(export, format, "Purchase Orders", "DyeHouse ERP", headers, rows, "purchase-orders");
    }

    /// <summary>Goods receipts as Excel/PDF.</summary>
    [HttpGet("receipts/export")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.PurchasesExport)]
    public async Task<IActionResult> ExportReceipts([FromQuery] Guid? supplierId, [FromQuery] DateTime? from,
        [FromQuery] DateTime? to, [FromQuery] string format = "excel",
        [FromServices] IReportExportService export = null!)
    {
        var receipts = await _mediator.Send(new GetPurchaseReceiptsQuery(supplierId, null, from, to));

        var headers = new[] { "Receipt no.", "Date", "Supplier", "Warehouse", "Supplier doc.", "Lines", "Total" };
        var rows = receipts.Select(r => new object?[]
        {
            r.ReceiptNumber, r.ReceiptDate.ToString("yyyy-MM-dd"), r.SupplierName, r.WarehouseName,
            r.SupplierDocumentNumber, r.Lines.Count, r.TotalValue
        }).ToList();

        return ExportFile(export, format, "Purchase Receipts", "DyeHouse ERP", headers, rows, "purchase-receipts");
    }

    /// <summary>Supplier invoices as Excel/PDF - the supplier-payables working paper.</summary>
    [HttpGet("supplier-invoices/export")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.PurchasesExport)]
    public async Task<IActionResult> ExportInvoices([FromQuery] Guid? supplierId, [FromQuery] SupplierInvoiceStatus? status,
        [FromQuery] bool? overdueOnly, [FromQuery] string format = "excel",
        [FromServices] IReportExportService export = null!)
    {
        var invoices = await _mediator.Send(new GetSupplierInvoicesQuery(supplierId, status, overdueOnly, null));

        var headers = new[] { "Internal no.", "Supplier doc.", "Date", "Due", "Supplier", "Status", "Total" };
        var rows = invoices.Select(i => new object?[]
        {
            i.InternalNumber, i.InvoiceNumber, i.InvoiceDate.ToString("yyyy-MM-dd"),
            i.DueDate.ToString("yyyy-MM-dd"), i.SupplierName, i.Status.ToString(), i.Total
        }).ToList();

        return ExportFile(export, format, "Supplier Invoices", "DyeHouse ERP", headers, rows, "supplier-invoices");
    }

    /// <summary>Supplier outstanding balances as Excel/PDF (spec sections 41 + 48).</summary>
    [HttpGet("supplier-balances/export")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.PurchasesExport)]
    public async Task<IActionResult> ExportSupplierBalances([FromQuery] Guid? supplierId,
        [FromQuery] bool? withBalanceOnly, [FromQuery] string format = "excel",
        [FromServices] IReportExportService export = null!)
    {
        var balances = await _mediator.Send(new GetSupplierBalancesQuery(supplierId, withBalanceOnly));

        var headers = new[] { "Supplier code", "Supplier", "Invoiced", "Paid", "Outstanding", "Overdue" };
        var rows = balances.Select(b => new object?[]
        {
            b.SupplierCode, b.SupplierName, b.TotalInvoiced, b.TotalPaid, b.Outstanding, b.OverdueAmount
        }).ToList();

        return ExportFile(export, format, "Supplier Balances", "DyeHouse ERP", headers, rows, "supplier-balances");
    }

    /// <summary>Supplier payments as Excel/PDF - the cash-paid-out working paper (spec section 35).</summary>
    [HttpGet("supplier-payments/export")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.PurchasesExport)]
    public async Task<IActionResult> ExportSupplierPayments([FromQuery] Guid? supplierId, [FromQuery] DateTime? from,
        [FromQuery] DateTime? to, [FromQuery] string format = "excel",
        [FromServices] IReportExportService export = null!)
    {
        var payments = await _mediator.Send(new GetSupplierPaymentsQuery(supplierId, from, to));

        var headers = new[] { "Payment no.", "Date", "Supplier code", "Supplier", "Account", "Method", "Amount", "Currency" };
        var rows = payments.Select(p => new object?[]
        {
            p.PaymentNumber, p.PaymentDate.ToString("yyyy-MM-dd"), p.SupplierCode, p.SupplierName,
            p.TreasuryAccountName, p.PaymentMethod, p.Amount, p.Currency
        }).ToList();

        return ExportFile(export, format, "Supplier Payments", "DyeHouse ERP", headers, rows, "supplier-payments");
    }

    private static IActionResult ExportFile(IReportExportService export, string format, string title,
        string subtitle, IReadOnlyList<string> headers, IReadOnlyList<object?[]> rows, string fileStem)
    {
        if (string.Equals(format, "pdf", StringComparison.OrdinalIgnoreCase))
            return new FileContentResult(export.GeneratePdf(title, subtitle, headers, rows), "application/pdf")
            { FileDownloadName = $"{fileStem}.pdf" };

        return new FileContentResult(export.GenerateExcel(title, headers, rows),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")
        { FileDownloadName = $"{fileStem}.xlsx" };
    }

    [HttpGet("supplier-balances")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.PurchasesView)]
    [ProducesResponseType(typeof(List<SupplierBalanceDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<SupplierBalanceDto>>> GetSupplierBalances(
        [FromQuery] Guid? supplierId, [FromQuery] bool? withBalanceOnly)
        => Ok(await _mediator.Send(new GetSupplierBalancesQuery(supplierId, withBalanceOnly)));
}

/// <summary>Shared body for the cancel actions that require a documented reason (spec section 46).</summary>
public record CancelRequest(string Reason);

public record CancelSupplierPaymentRequest(string Reason);
