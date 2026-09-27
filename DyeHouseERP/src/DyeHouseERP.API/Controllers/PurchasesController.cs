using DyeHouseERP.Application.Purchases.Commands;
using DyeHouseERP.Application.Purchases.DTOs;
using DyeHouseERP.Application.Purchases.Queries;
using DyeHouseERP.API.Authorization;
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

    // -------------------------------------------------- accounts / reports

    /// <summary>Supplier account statement (spec sections 41 and 48) - a live sum over the append-only ledger.</summary>
    [HttpGet("suppliers/{supplierId:guid}/ledger")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.PurchasesView)]
    [ProducesResponseType(typeof(List<SupplierLedgerEntryDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<SupplierLedgerEntryDto>>> GetSupplierLedger(
        Guid supplierId, [FromQuery] DateTime? from, [FromQuery] DateTime? to)
        => Ok(await _mediator.Send(new GetSupplierLedgerQuery(supplierId, from, to)));

    [HttpGet("supplier-balances")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.PurchasesView)]
    [ProducesResponseType(typeof(List<SupplierBalanceDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<SupplierBalanceDto>>> GetSupplierBalances(
        [FromQuery] Guid? supplierId, [FromQuery] bool? withBalanceOnly)
        => Ok(await _mediator.Send(new GetSupplierBalancesQuery(supplierId, withBalanceOnly)));
}

/// <summary>Shared body for the cancel actions that require a documented reason (spec section 46).</summary>
public record CancelRequest(string Reason);
