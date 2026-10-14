using DyeHouseERP.API.Authorization;
using DyeHouseERP.Application.CustomerReturns.Commands;
using DyeHouseERP.Application.CustomerReturns.DTOs;
using DyeHouseERP.Application.CustomerReturns.Queries;
using DyeHouseERP.Application.ProductionOrders.DTOs;
using DyeHouseERP.Domain.Common;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DyeHouseERP.API.Controllers;

/// <summary>
/// Customer returns of processed goods (spec sections 32-33). Recording a return
/// posts IN ledger rows into the RAW MATERIAL warehouse, so it is gated by the
/// ordinary inventory permissions rather than by an approval workflow - there is
/// deliberately no inspection/approval step here.
/// </summary>
[ApiController]
[Route("api/customer-returns")]
[Authorize]
public class CustomerReturnsController : ControllerBase
{
    private readonly ISender _mediator;
    public CustomerReturnsController(ISender mediator) => _mediator = mediator;

    [HttpGet]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.InventoryView)]
    [ProducesResponseType(typeof(List<CustomerReturnDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<CustomerReturnDto>>> Get(
        [FromQuery] Guid? customerId, [FromQuery] DateTime? fromDate, [FromQuery] DateTime? toDate, [FromQuery] string? search)
        => Ok(await _mediator.Send(new GetCustomerReturnsQuery(customerId, fromDate, toDate, search)));

    [HttpGet("{id:guid}")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.InventoryView)]
    [ProducesResponseType(typeof(CustomerReturnDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<CustomerReturnDto>> GetById(Guid id)
        => Ok(await _mediator.Send(new GetCustomerReturnByIdQuery(id)));

    /// <summary>
    /// Records a return into raw material stock. Gated by the returns' own right
    /// (<see cref="Permissions.RawReturn"/>) rather than by the general inventory-edit right: receiving a
    /// customer's goods back is a stock operation of its own, and the correction of one is a different
    /// right again (<see cref="Permissions.RawReturnCancel"/>).
    /// </summary>
    [HttpPost]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.RawReturn)]
    [ProducesResponseType(typeof(CustomerReturnDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<CustomerReturnDto>> Create([FromBody] CreateCustomerReturnCommand command)
    {
        var result = await _mediator.Send(command);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    /// <summary>
    /// Raises a NEW, INDEPENDENT Job Order from a return (spec section 33). The return is only a
    /// reference: it is never converted, no invoice is touched, no credit note is created and no charge
    /// is reversed. The order comes from the canonical issue workflow, so real stock and production
    /// transactions are recorded exactly as for any other order.
    ///
    /// The caller MUST state who carries the cost (على حساب العميل / على حساب الشركة) - there is no
    /// default, because the same rework can legitimately be billed or absorbed.
    /// </summary>
    [HttpPost("{id:guid}/production-order")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.RawConsume)]
    [ProducesResponseType(typeof(ProductionOrderDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<ProductionOrderDto>> CreateProductionOrder(
        Guid id, [FromBody] CreateOrderFromReturnRequest request)
    {
        var command = new CreateProductionOrderFromReturnCommand(
            id, request.ItemId, request.RawMessageId, request.OrderDate,
            request.ActualQuantityKg, request.ActualQuantityMeter, request.CostBearer,
            request.Color, request.Notes, request.RequestId, request.StartingStageId);

        var result = await _mediator.Send(command);
        return CreatedAtAction(nameof(GetById), new { id }, result);
    }

    /// <summary>
    /// Cancels a return entered in error - the wrong lot, quantity or customer (spec sections 32-33 + 49).
    /// A return posts stock the moment it is created, so it is never edited or deleted: the document and
    /// its IN rows are preserved and the stock it restored is taken back out with an equal-and-opposite
    /// OUT row, recorded with the reason and the user. Refused while that material has already been used.
    /// </summary>
    [HttpPost("{id:guid}/cancel")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.RawReturnCancel)]
    [ProducesResponseType(typeof(CustomerReturnDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<CustomerReturnDto>> Cancel(Guid id, [FromBody] CancelCustomerReturnRequest request)
        => Ok(await _mediator.Send(new CancelCustomerReturnCommand(id, request.Reason)));
}

/// <summary>
/// A cancellation always carries a reason: it is stored on the return and written to the audit
/// trail, so a corrected mistake is explainable later.
/// </summary>
public record CancelCustomerReturnRequest(string Reason);

/// <summary>
/// Body of the "new Job Order from a return" request (spec section 33). The account choice is nullable
/// on purpose so that an omitted value is a validation error naming the choice, never a silent default.
/// </summary>
public record CreateOrderFromReturnRequest(
    Guid ItemId,
    Guid RawMessageId,
    DateTime OrderDate,
    decimal? ActualQuantityKg,
    decimal? ActualQuantityMeter,
    DyeHouseERP.Domain.Enums.CostBearer? CostBearer,
    string? Color = null,
    string? Notes = null,
    Guid? RequestId = null,
    /// <summary>Optional starting stage: returned material may be reworked from the operation it failed at (spec section 33). Must be an existing, active stage.</summary>
    Guid? StartingStageId = null);
