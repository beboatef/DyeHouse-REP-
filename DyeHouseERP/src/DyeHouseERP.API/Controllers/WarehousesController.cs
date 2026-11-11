using DyeHouseERP.API.Authorization;
using DyeHouseERP.API.Common;
using DyeHouseERP.Domain.Common;
using DyeHouseERP.Application.Common.Import;
using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.Warehouses.Commands;
using DyeHouseERP.Application.Warehouses.DTOs;
using DyeHouseERP.Application.Warehouses.Queries;
using DyeHouseERP.Domain.Entities;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.API.Controllers;

/// <summary>Request payload to warehouse.</summary>
/// <param name="Id">The id.</param>
/// <param name="Code">The code.</param>
/// <param name="Name">The name.</param>
/// <param name="Kind">The kind.</param>
/// <param name="IsActive">Whether is active.</param>
public record WarehouseDto(Guid Id, string Code, string Name, WarehouseKind Kind, bool IsActive);
/// <summary>Request payload to create warehouse.</summary>
/// <param name="Code">The code.</param>
/// <param name="Name">The name.</param>
/// <param name="Kind">The kind.</param>
public record CreateWarehouseRequest(string Code, string Name, WarehouseKind Kind);
/// <summary>Request payload to update warehouse.</summary>
/// <param name="Name">The name.</param>
/// <param name="Kind">The kind.</param>
public record UpdateWarehouseRequest(string Name, WarehouseKind Kind);
/// <summary>Request payload to set warehouse active.</summary>
/// <param name="IsActive">Whether is active.</param>
public record SetWarehouseActiveRequest(bool IsActive);

/// <summary>Hosts the warehouses endpoints.</summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class WarehousesController : ControllerBase
{
    private readonly IApplicationDbContext _db;
    private readonly ISender _mediator;

    /// <summary>Initializes a new instance of the WarehousesController class.</summary>
    public WarehousesController(IApplicationDbContext db, ISender mediator)
    {
        _db = db;
        _mediator = mediator;
    }

    private static WarehouseDto ToApi(WarehouseMasterDto w) => new(w.Id, w.Code, w.Name, w.Kind, w.IsActive);

    /// <summary>Lists warehouses.</summary>
    /// <remarks>Route: GET api/Warehouses. Requires the 'inventory.view' permission.</remarks>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.InventoryView)]
    public async Task<ActionResult<List<WarehouseDto>>> Get(
        [FromQuery] WarehouseKind? kind, [FromQuery] bool? activeOnly, [FromQuery] string? search)
    {
        var warehouses = await _mediator.Send(new GetWarehousesQuery(kind, activeOnly, search));
        return Ok(warehouses.Select(ToApi).ToList());
    }

    /// <summary>Creates warehouses.</summary>
    /// <remarks>Route: POST api/Warehouses. Requires the 'settings.manage' permission.</remarks>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.SettingsManage)]
    public async Task<ActionResult<WarehouseDto>> Create([FromBody] CreateWarehouseRequest request)
    {
        var warehouse = await _mediator.Send(new CreateWarehouseCommand(request.Code, request.Name, request.Kind));
        return CreatedAtAction(nameof(Get), new { }, ToApi(warehouse));
    }

    /// <summary>
    /// Renames a warehouse and/or re-classifies its kind. The code stays immutable: it is
    /// the identity every document, ledger row and report already carries.
    /// </summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.SettingsManage)]
    public async Task<ActionResult<WarehouseDto>> Update(Guid id, [FromBody] UpdateWarehouseRequest request)
        => Ok(ToApi(await _mediator.Send(new UpdateWarehouseCommand(id, request.Name, request.Kind))));

    /// <summary>
    /// Activates or deactivates a warehouse. Deactivation is a flag only - nothing is deleted,
    /// stock history stays readable, and the warehouse can be reactivated at any time.
    /// </summary>
    [HttpPost("{id:guid}/active")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.SettingsManage)]
    public async Task<ActionResult<WarehouseDto>> SetActive(Guid id, [FromBody] SetWarehouseActiveRequest request)
        => Ok(ToApi(await _mediator.Send(new SetWarehouseActiveCommand(id, request.IsActive))));

    // ------------------------------------------- export / import (spec sections 15 + 38)

    /// <summary>Warehouse master data as Excel or PDF.</summary>
    [HttpGet("export")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ReportsExport)]
    public async Task<IActionResult> Export([FromQuery] WarehouseKind? kind,
        [FromQuery] string format = "excel", [FromServices] IReportExportService export = null!)
    {
        var query = _db.Warehouses.AsNoTracking().AsQueryable();
        if (kind.HasValue) query = query.Where(w => w.Kind == kind);
        var warehouses = await query.OrderBy(w => w.Code)
            .Select(w => new WarehouseDto(w.Id, w.Code, w.Name, w.Kind, w.IsActive))
            .ToListAsync();

        var headers = new[] { "Code", "Name", "Kind", "Active" };
        var rows = warehouses.Select(w => new object?[] { w.Code, w.Name, w.Kind.ToString(), w.IsActive ? "Yes" : "No" }).ToList();

        return ExportFileHelper.ToFile(export, format, "Warehouses", null, headers, rows, "warehouses", "Warehouses");
    }

    /// <summary>Step 1 of the warehouse import workflow: the template with the exact expected columns.</summary>
    [HttpGet("import/template")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.InventoryView)]
    public IActionResult ImportTemplate([FromServices] IReportExportService export)
        => ExportFileHelper.Template(export, "Warehouses", WarehouseImportTemplate.Headers, WarehouseImportTemplate.SampleRows(), "warehouses-import-template");

    /// <summary>Step 2: validate the upload and report row errors. Writes nothing.</summary>
    [HttpPost("import/preview")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.SettingsManage)]
    public async Task<ActionResult<ImportPreviewDto>> PreviewImport(IFormFile file, [FromServices] ISender mediator = null!)
        => Ok(await mediator.Send(new PreviewWarehouseImportCommand(await ExportFileHelper.ReadUploadAsync(file))));

    /// <summary>Step 3: confirm - creates only the valid new warehouses; existing codes are always skipped.</summary>
    [HttpPost("import/execute")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.SettingsManage)]
    public async Task<ActionResult<ImportExecuteResultDto>> ExecuteImport(IFormFile file, [FromServices] ISender mediator = null!)
        => Ok(await mediator.Send(new ExecuteWarehouseImportCommand(await ExportFileHelper.ReadUploadAsync(file))));
}
