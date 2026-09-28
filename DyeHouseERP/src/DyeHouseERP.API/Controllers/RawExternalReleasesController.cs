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
    public RawExternalReleasesController(ISender mediator) => _mediator = mediator;

    [HttpGet]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.RawExternalRelease)]
    [ProducesResponseType(typeof(List<RawExternalReleaseDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<RawExternalReleaseDto>>> Get(
        [FromQuery] Guid? customerId, [FromQuery] RawReleaseReason? reason, [FromQuery] ExternalProcessingStatus? status)
        => Ok(await _mediator.Send(new GetRawExternalReleasesQuery(customerId, reason, status)));

    /// <summary>
    /// External releases (returns to customer, external processing, raw sales) as
    /// Excel or PDF - the warehouse issues / returns register, including the
    /// external-processing status, cost and returned quantities per release.
    /// </summary>
    [HttpGet("export")]
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

        return ExportFileHelper.ToFile(export, format, "Raw Material Releases", "DyeHouse ERP", headers, rows, "raw-external-releases", "Releases");
    }

    [HttpPost]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.RawExternalRelease)]
    [ProducesResponseType(typeof(RawExternalReleaseDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<RawExternalReleaseDto>> Create([FromBody] CreateRawExternalReleaseCommand command)
    {
        var result = await _mediator.Send(command);
        return CreatedAtAction(nameof(Get), new { }, result);
    }

    /// <summary>Material back from the external processor - posts an IN ledger row (spec section 21).</summary>
    [HttpPost("{id:guid}/return")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.RawExternalRelease)]
    [ProducesResponseType(typeof(RawExternalReleaseDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<RawExternalReleaseDto>> RecordReturn(Guid id, [FromBody] RecordReturnBody body)
        => Ok(await _mediator.Send(new RecordExternalProcessingReturnCommand(
            id, body.ReturnedQuantityKg, body.ReturnedQuantityMeter, body.ActualReturnDate, body.Notes)));

    [HttpPut("{id:guid}/external-processing")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.RawExternalRelease)]
    [ProducesResponseType(typeof(RawExternalReleaseDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<RawExternalReleaseDto>> SetExternalProcessing(Guid id, [FromBody] SetExternalProcessingBody body)
        => Ok(await _mediator.Send(new SetExternalProcessingDetailsCommand(
            id, body.ExternalParty, body.Stage, body.Cost, body.ExpectedReturnDate)));

    [HttpPost("{id:guid}/cancel")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.RawExternalRelease)]
    [ProducesResponseType(typeof(RawExternalReleaseDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<RawExternalReleaseDto>> Cancel(Guid id, [FromBody] CancelReleaseBody body)
        => Ok(await _mediator.Send(new CancelExternalProcessingCommand(id, body.Reason)));
}

public record RecordReturnBody(decimal? ReturnedQuantityKg, decimal? ReturnedQuantityMeter, DateTime ActualReturnDate, string? Notes);
public record SetExternalProcessingBody(string? ExternalParty, string? Stage, decimal? Cost, DateTime? ExpectedReturnDate);
public record CancelReleaseBody(string Reason);
