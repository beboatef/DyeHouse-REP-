using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.Items.Commands;
using DyeHouseERP.Application.Items.DTOs;
using DyeHouseERP.Application.Items.Queries;
using DyeHouseERP.API.Authorization;
using DyeHouseERP.Domain.Common;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DyeHouseERP.API.Controllers;

/// <summary>
/// Item master (spec sections 6-7). BaseUnit is always exactly one of KG or
/// Meter, never auto-converted, and there is no "Top/توب" unit. Row/piece
/// counts are descriptive data on receiving documents, never an inventory unit.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ItemsController : ControllerBase
{
    private readonly ISender _mediator;
    public ItemsController(ISender mediator) => _mediator = mediator;

    [HttpGet]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ItemsView)]
    [ProducesResponseType(typeof(List<ItemDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<ItemDto>>> Get(
        [FromQuery] bool? activeOnly, [FromQuery] string? search, [FromQuery] string? category)
        => Ok(await _mediator.Send(new GetItemsQuery(activeOnly, search, category)));

    [HttpPost]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ItemsCreate)]
    [ProducesResponseType(typeof(ItemDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<ItemDto>> Create([FromBody] CreateItemCommand command)
    {
        var result = await _mediator.Send(command);
        return CreatedAtAction(nameof(Get), new { }, result);
    }

    /// <summary>Edits an item master record. Changing the base unit is refused once inventory history exists.</summary>
    [HttpPut("{id:guid}")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ItemsEdit)]
    public async Task<ActionResult<ItemDto>> Update(Guid id, [FromBody] UpdateItemCommand command)
    {
        if (id != command.Id) command = command with { Id = id };
        return Ok(await _mediator.Send(command));
    }

    /// <summary>Excel export of the current item list (spec section 7) - same filters as GET.</summary>
    [HttpGet("export/excel")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ReportsExport)]
    public async Task<IActionResult> ExportExcel(
        [FromQuery] bool? activeOnly, [FromQuery] string? search,
        [FromServices] IReportExportService export)
    {
        var items = await _mediator.Send(new GetItemsQuery(activeOnly, search));
        var headers = new List<string> { "Code", "NameAr", "NameEn", "Category", "BaseUnit", "Active" };
        var rows = items.Select(i => new object?[]
        {
            i.Code, i.NameAr, i.NameEn, i.Category, i.BaseUnit.ToString(), i.IsActive ? "Yes" : "No"
        }).ToList();

        var bytes = export.GenerateExcel("Items", headers, rows);
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "items.xlsx");
    }

    /// <summary>Step 1 of the import workflow (spec section 7): the empty template with the exact expected columns.</summary>
    [HttpGet("import/template")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ItemsView)]
    public IActionResult ImportTemplate([FromServices] IReportExportService export)
    {
        var bytes = export.GenerateExcel("Items", ItemImportTemplate.Headers, ItemImportTemplate.SampleRows());
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "items-import-template.xlsx");
    }

    /// <summary>
    /// Step 2 of the import workflow: preview + validate the uploaded file without saving anything.
    /// Existing item codes are reported as errors (never overwritten automatically) unless allowExistingUpdate is set -
    /// which is only reachable through the items.edit-protected endpoint below.
    /// </summary>
    [HttpPost("import/preview")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ItemsCreate)]
    public async Task<ActionResult<ItemImportPreviewDto>> PreviewImport(IFormFile file)
    {
        await using var stream = new MemoryStream();
        await file.CopyToAsync(stream);
        return Ok(await _mediator.Send(new PreviewItemImportCommand(stream.ToArray(), false)));
    }

    /// <summary>Step 3: confirm - re-validates from scratch and creates only the valid, new rows.</summary>
    [HttpPost("import/execute")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ItemsCreate)]
    public async Task<ActionResult<ItemImportExecuteResultDto>> ExecuteImport(IFormFile file)
    {
        await using var stream = new MemoryStream();
        await file.CopyToAsync(stream);
        return Ok(await _mediator.Send(new ExecuteItemImportCommand(stream.ToArray(), false)));
    }

    /// <summary>
    /// The same import, but also updating items whose code already exists. Kept behind the
    /// items.edit permission on purpose: spec section 7 requires an explicit right to overwrite existing items.
    /// </summary>
    [HttpPost("import/execute-update")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ItemsEdit)]
    public async Task<ActionResult<ItemImportExecuteResultDto>> ExecuteImportWithUpdate(IFormFile file)
    {
        await using var stream = new MemoryStream();
        await file.CopyToAsync(stream);
        return Ok(await _mediator.Send(new ExecuteItemImportCommand(stream.ToArray(), true)));
    }

    /// <summary>Preview of the same update-capable import, so the UI can show what will be updated before confirming.</summary>
    [HttpPost("import/preview-update")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ItemsEdit)]
    public async Task<ActionResult<ItemImportPreviewDto>> PreviewImportWithUpdate(IFormFile file)
    {
        await using var stream = new MemoryStream();
        await file.CopyToAsync(stream);
        return Ok(await _mediator.Send(new PreviewItemImportCommand(stream.ToArray(), true)));
    }
}
