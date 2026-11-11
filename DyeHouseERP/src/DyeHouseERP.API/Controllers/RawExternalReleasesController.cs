using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.RawExternalReleases.Commands;
using DyeHouseERP.Application.RawExternalReleases.DTOs;
using DyeHouseERP.Application.RawExternalReleases.Queries;
using DyeHouseERP.API.Authorization;
using DyeHouseERP.API.Common;
using DyeHouseERP.Domain.Common;
using DyeHouseERP.Domain.Enums;
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
    /// <summary>Initializes a new instance of the RawExternalReleasesController class.</summary>
    public RawExternalReleasesController(ISender mediator) => _mediator = mediator;

    /// <summary>Lists external processing releases (material sent out, returned or sold).</summary>
    /// <remarks>Route: GET api/raw-external-releases. Requires the 'raw.external_release' permission.</remarks>
    [HttpGet]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.RawExternalRelease)]
    [ProducesResponseType(typeof(List<RawExternalReleaseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<List<RawExternalReleaseDto>>> Get(
        [FromQuery] Guid? customerId, [FromQuery] RawReleaseReason? reason, [FromQuery] ExternalProcessingStatus? status)
        => Ok(await _mediator.Send(new GetRawExternalReleasesQuery(customerId, reason, status)));

    /// <summary>
    /// External processing movements (returns to customer, external
    /// processing, raw sales) as Excel or PDF - the warehouse issues / returns
    /// register, including the external-processing status, cost and returned
    /// quantities per movement.
    /// </summary>
    [HttpGet("export")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ReportsExport)]
    public async Task<IActionResult> Export([FromQuery] Guid? customerId, [FromQuery] RawReleaseReason? reason,
        [FromQuery] ExternalProcessingStatus? status, [FromQuery] string format = "excel",
        [FromServices] IReportExportService export = null!)
    {
        var releases = await _mediator.Send(new GetRawExternalReleasesQuery(customerId, reason, status));

        var headers = new[] { "Release no.", "Date", "Customer", "Item", "Message", "Qty KG", "Qty M", "Reason", "External party", "Stage", "Cost", "Expected return", "Actual return", "Returned KG", "Returned M", "Status" };
        var rows = releases.Select(r => new object?[]
        {
            r.ReleaseNumber, r.ReleaseDate.ToString("yyyy-MM-dd"), $"{r.CustomerCode} - {r.CustomerName}",
            $"{r.ItemCode} - {r.ItemName}", r.MessageNumber, r.QuantityKg, r.QuantityMeter,
            r.Reason.ToString(), r.ExternalParty, r.ExternalProcessingStage, r.ExternalProcessingCost,
            r.ExpectedReturnDate?.ToString("yyyy-MM-dd"), r.ActualReturnDate?.ToString("yyyy-MM-dd"),
            r.ReturnedQuantityKg, r.ReturnedQuantityMeter, r.Status.ToString()
        }).ToList();

        return ExportFileHelper.ToFile(export, format, "Raw Material Releases", null, headers, rows, "raw-external-releases", "Releases");
    }

    /// <summary>Creates external processing releases (material sent out, returned or sold).</summary>
    /// <remarks>Route: POST api/raw-external-releases. Requires the 'raw.external_release' permission.</remarks>
    [HttpPost]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.RawExternalRelease)]
    [ProducesResponseType(typeof(RawExternalReleaseDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<RawExternalReleaseDto>> Create([FromBody] CreateRawExternalReleaseCommand command)
    {
        var result = await _mediator.Send(command);
        return CreatedAtAction(nameof(Get), new { }, result);
    }

    /// <summary>Material back from the external processor - posts an IN ledger row (spec section 21).</summary>
    [HttpPost("{id:guid}/return")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.RawExternalRelease)]
    [ProducesResponseType(typeof(RawExternalReleaseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<RawExternalReleaseDto>> RecordReturn(Guid id, [FromBody] RecordReturnBody body)
        => Ok(await _mediator.Send(new RecordExternalProcessingReturnCommand(
            id, body.ReturnedQuantityKg, body.ReturnedQuantityMeter, body.ActualReturnDate, body.Notes)));

    /// <summary>Sets external processing.</summary>
    /// <remarks>Route: PUT api/raw-external-releases/{id:guid}/external-processing. Requires the 'raw.external_release' permission.</remarks>
    [HttpPut("{id:guid}/external-processing")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.RawExternalRelease)]
    [ProducesResponseType(typeof(RawExternalReleaseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<RawExternalReleaseDto>> SetExternalProcessing(Guid id, [FromBody] SetExternalProcessingBody body)
        => Ok(await _mediator.Send(new SetExternalProcessingDetailsCommand(
            id, body.ExternalParty, body.Stage, body.Cost, body.ExpectedReturnDate)));

    /// <summary>Cancels external processing releases (material sent out, returned or sold).</summary>
    /// <remarks>Route: POST api/raw-external-releases/{id:guid}/cancel. Requires the 'raw.external_release' permission.</remarks>
    [HttpPost("{id:guid}/cancel")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.RawExternalRelease)]
    [ProducesResponseType(typeof(RawExternalReleaseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<RawExternalReleaseDto>> Cancel(Guid id, [FromBody] CancelReleaseBody body)
        => Ok(await _mediator.Send(new CancelExternalProcessingCommand(id, body.Reason)));
}

/// <summary>Request payload to record return.</summary>
/// <param name="ReturnedQuantityKg">The returned quantity kg.</param>
/// <param name="ReturnedQuantityMeter">The returned quantity meter.</param>
/// <param name="ActualReturnDate">The actual return date.</param>
/// <param name="Notes">The notes.</param>
public record RecordReturnBody(decimal? ReturnedQuantityKg, decimal? ReturnedQuantityMeter, DateTime ActualReturnDate, string? Notes);
/// <summary>Request payload to set external processing.</summary>
/// <param name="ExternalParty">The external party.</param>
/// <param name="Stage">The stage.</param>
/// <param name="Cost">The cost.</param>
/// <param name="ExpectedReturnDate">The expected return date.</param>
public record SetExternalProcessingBody(string? ExternalParty, string? Stage, decimal? Cost, DateTime? ExpectedReturnDate);
/// <summary>Request payload to cancel release.</summary>
/// <param name="Reason">The reason.</param>
public record CancelReleaseBody(string Reason);
