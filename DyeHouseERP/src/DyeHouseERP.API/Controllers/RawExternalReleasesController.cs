using DyeHouseERP.Application.RawExternalReleases.Commands;
using DyeHouseERP.Application.RawExternalReleases.DTOs;
using DyeHouseERP.Application.RawExternalReleases.Queries;
using DyeHouseERP.API.Authorization;
using DyeHouseERP.Domain.Common;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DyeHouseERP.API.Controllers;

/// <summary>Return to customer / external processing / raw material sale (spec section 14).</summary>
[ApiController]
[Route("api/raw-external-releases")]
[Authorize]
public class RawExternalReleasesController : ControllerBase
{
    private readonly ISender _mediator;
    public RawExternalReleasesController(ISender mediator) => _mediator = mediator;

    [HttpGet]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.RawExternalRelease)]
    [ProducesResponseType(typeof(List<RawExternalReleaseDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<RawExternalReleaseDto>>> Get([FromQuery] Guid? customerId)
        => Ok(await _mediator.Send(new GetRawExternalReleasesQuery(customerId)));

    [HttpPost]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.RawExternalRelease)]
    [ProducesResponseType(typeof(RawExternalReleaseDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<RawExternalReleaseDto>> Create([FromBody] CreateRawExternalReleaseCommand command)
    {
        var result = await _mediator.Send(command);
        return CreatedAtAction(nameof(Get), new { }, result);
    }
}
