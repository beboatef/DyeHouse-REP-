using DyeHouseERP.Application.Treasury.Commands;
using DyeHouseERP.Application.Treasury.DTOs;
using DyeHouseERP.Application.Treasury.Queries;
using DyeHouseERP.API.Authorization;
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
