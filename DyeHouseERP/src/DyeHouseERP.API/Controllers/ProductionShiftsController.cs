using DyeHouseERP.API.Authorization;
using DyeHouseERP.Application.ProductionMasterData.Commands;
using DyeHouseERP.Application.ProductionMasterData.DTOs;
using DyeHouseERP.Application.ProductionMasterData.Queries;
using DyeHouseERP.Domain.Common;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DyeHouseERP.API.Controllers;

/// <summary>
/// Production EXECUTION shift master data (S-Production-Execution-Shifts).
/// Execution data only - separate from HR employee shifts, attendance and
/// payroll. Fully user-configurable with no fixed three-shift limit.
/// </summary>
[ApiController]
[Route("api/production-shifts")]
[Authorize]
public class ProductionShiftsController : ControllerBase
{
    private readonly ISender _mediator;
    public ProductionShiftsController(ISender mediator) => _mediator = mediator;

    [HttpGet]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ProductionView)]
    [ProducesResponseType(typeof(List<ProductionShiftDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<ProductionShiftDto>>> Get([FromQuery] bool? activeOnly)
        => Ok(await _mediator.Send(new GetProductionShiftsQuery(activeOnly)));

    [HttpPost]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ProductionManageShifts)]
    [ProducesResponseType(typeof(ProductionShiftDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<ProductionShiftDto>> Create([FromBody] CreateProductionShiftCommand command)
    {
        var result = await _mediator.Send(command);
        return CreatedAtAction(nameof(Get), new { }, result);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ProductionManageShifts)]
    [ProducesResponseType(typeof(ProductionShiftDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<ProductionShiftDto>> Update(Guid id, [FromBody] UpdateProductionShiftRequest request)
        => Ok(await _mediator.Send(new UpdateProductionShiftCommand(id, request.Name, request.Sequence)));

    [HttpPost("{id:guid}/activation")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ProductionManageShifts)]
    [ProducesResponseType(typeof(ProductionShiftDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<ProductionShiftDto>> SetActivation(Guid id, [FromBody] SetActivationRequest request)
        => Ok(await _mediator.Send(new SetProductionShiftActivationCommand(id, request.IsActive)));
}

public record UpdateProductionShiftRequest(string Name, int Sequence);
