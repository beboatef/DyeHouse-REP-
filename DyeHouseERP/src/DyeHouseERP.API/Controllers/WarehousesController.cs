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

public record WarehouseDto(Guid Id, string Code, string Name, WarehouseKind Kind, bool IsActive);
public record CreateWarehouseRequest(string Code, string Name, WarehouseKind Kind);
public record UpdateWarehouseRequest(string Name, WarehouseKind Kind);
public record SetWarehouseActiveRequest(bool IsActive);

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class WarehousesController : ControllerBase
{
    private readonly IApplicationDbContext _db;
    private readonly ISender _mediator;

    public WarehousesController(IApplicationDbContext db, ISender mediator)
    {
        _db = db;
        _mediator = mediator;
    }

    private static WarehouseDto ToApi(WarehouseMasterDto w) => new(w.Id, w.Code, w.Name, w.Kind, w.IsActive);

    [HttpGet]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.InventoryView)]
    public async Task<ActionResult<List<WarehouseDto>>> Get(
        [FromQuery] WarehouseKind? kind, [FromQuery] bool? activeOnly, [FromQuery] string? search)
    {
        var warehouses = await _mediator.Send(new GetWarehousesQuery(kind, activeOnly, search));
        return Ok(warehouses.Select(ToApi).ToList());
    }

    [HttpPost]
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
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.SettingsManage)]
    public async Task<ActionResult<WarehouseDto>> Update(Guid id, [FromBody] UpdateWarehouseRequest request)
        => Ok(ToApi(await _mediator.Send(new UpdateWarehouseCommand(id, request.Name, request.Kind))));

    /// <summary>
    /// Activates or deactivates a warehouse. Deactivation is a flag only - nothing is deleted,
    /// stock history stays readable, and the warehouse can be reactivated at any time.
    /// </summary>
    [HttpPost("{id:guid}/active")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.SettingsManage)]
    public async Task<ActionResult<WarehouseDto>> SetActive(Guid id, [FromBody] SetWarehouseActiveRequest request)
        => Ok(ToApi(await _mediator.Send(new SetWarehouseActiveCommand(id, request.IsActive))));

    // ------------------------------------------- export / import (spec sections 15 + 38)

    /// <summary>Warehouse master data as Excel or PDF.</summary>
    [HttpGet("export")]
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
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.InventoryView)]
    public IActionResult ImportTemplate([FromServices] IReportExportService export)
        => ExportFileHelper.Template(export, "Warehouses", WarehouseImportTemplate.Headers, WarehouseImportTemplate.SampleRows(), "warehouses-import-template");

    /// <summary>Step 2: validate the upload and report row errors. Writes nothing.</summary>
    [HttpPost("import/preview")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.SettingsManage)]
    public async Task<ActionResult<ImportPreviewDto>> PreviewImport(IFormFile file, [FromServices] ISender mediator = null!)
        => Ok(await mediator.Send(new PreviewWarehouseImportCommand(await ExportFileHelper.ReadUploadAsync(file))));

    /// <summary>Step 3: confirm - creates only the valid new warehouses; existing codes are always skipped.</summary>
    [HttpPost("import/execute")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.SettingsManage)]
    public async Task<ActionResult<ImportExecuteResultDto>> ExecuteImport(IFormFile file, [FromServices] ISender mediator = null!)
        => Ok(await mediator.Send(new ExecuteWarehouseImportCommand(await ExportFileHelper.ReadUploadAsync(file))));
}
