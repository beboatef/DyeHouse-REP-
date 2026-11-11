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
    /// <summary>Initializes a new instance of the ProductionShiftsController class.</summary>
    public ProductionShiftsController(ISender mediator) => _mediator = mediator;

    /// <summary>Lists production shifts.</summary>
    /// <remarks>Route: GET api/production-shifts. Requires the 'production.view' permission.</remarks>
    [HttpGet]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ProductionView)]
    [ProducesResponseType(typeof(List<ProductionShiftDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<List<ProductionShiftDto>>> Get([FromQuery] bool? activeOnly)
        => Ok(await _mediator.Send(new GetProductionShiftsQuery(activeOnly)));

    /// <summary>Creates production shifts.</summary>
    /// <remarks>Route: POST api/production-shifts. Requires the 'production.manage_shifts' permission.</remarks>
    [HttpPost]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ProductionManageShifts)]
    [ProducesResponseType(typeof(ProductionShiftDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ProductionShiftDto>> Create([FromBody] CreateProductionShiftCommand command)
    {
        var result = await _mediator.Send(command);
        return CreatedAtAction(nameof(Get), new { }, result);
    }

    /// <summary>Updates production shifts.</summary>
    /// <remarks>Route: PUT api/production-shifts/{id:guid}. Requires the 'production.manage_shifts' permission.</remarks>
    [HttpPut("{id:guid}")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ProductionManageShifts)]
    [ProducesResponseType(typeof(ProductionShiftDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ProductionShiftDto>> Update(Guid id, [FromBody] UpdateProductionShiftRequest request)
        => Ok(await _mediator.Send(new UpdateProductionShiftCommand(id, request.Name, request.Sequence, request.HoursPerDay)));

    /// <summary>Sets activation.</summary>
    /// <remarks>Route: POST api/production-shifts/{id:guid}/activation. Requires the 'production.manage_shifts' permission.</remarks>
    [HttpPost("{id:guid}/activation")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ProductionManageShifts)]
    [ProducesResponseType(typeof(ProductionShiftDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ProductionShiftDto>> SetActivation(Guid id, [FromBody] SetActivationRequest request)
        => Ok(await _mediator.Send(new SetProductionShiftActivationCommand(id, request.IsActive)));
}

/// <summary>Request payload to update production shift.</summary>
/// <param name="Name">The name.</param>
/// <param name="Sequence">The sequence.</param>
/// <param name="HoursPerDay">The planned production hours per day (null leaves it unchanged).</param>
public record UpdateProductionShiftRequest(string Name, int Sequence, decimal? HoursPerDay = null);
