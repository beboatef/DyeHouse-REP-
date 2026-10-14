using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.Items.Commands;
using DyeHouseERP.Application.Items.DTOs;
using DyeHouseERP.Application.Items.Queries;
using DyeHouseERP.API.Authorization;
using DyeHouseERP.API.Common;
using DyeHouseERP.Domain.Common;
using DyeHouseERP.Domain.Enums;
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

    /// <summary>
    /// The explicit, item-scoped unit conversion rules (spec sections 5-6). Empty is the
    /// normal answer: KG and Meter are never converted unless a rule is configured here.
    /// </summary>
    [HttpGet("{id:guid}/conversions")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ItemsView)]
    [ProducesResponseType(typeof(List<ItemUnitConversionDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<ItemUnitConversionDto>>> GetConversions(
        Guid id, [FromQuery] string? operation)
        => Ok(await _mediator.Send(new GetItemUnitConversionsQuery(id, operation)));

    /// <summary>
    /// Creates or replaces the ONE conversion rule for an (item + operation) pair. The only
    /// place a KG&lt;-&gt;Meter conversion can be introduced - opt-in, per item, per operation.
    /// </summary>
    [HttpPost("{id:guid}/conversions")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ItemsEdit)]
    [ProducesResponseType(typeof(ItemUnitConversionDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<ItemUnitConversionDto>> SetConversion(
        Guid id, [FromBody] SetItemUnitConversionRequest request)
        => Ok(await _mediator.Send(new SetItemUnitConversionCommand(
            id, request.FromUnit, request.ToUnit, request.Factor, request.Operation,
            request.Notes, request.IsActive)));

    /// <summary>Removes the explicit conversion rule for an (item + operation) pair.</summary>
    [HttpDelete("{id:guid}/conversions/{operation}")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ItemsEdit)]
    public async Task<IActionResult> RemoveConversion(Guid id, string operation)
    {
        await _mediator.Send(new RemoveItemUnitConversionCommand(id, operation));
        return NoContent();
    }

    /// <summary>Excel export of the current item list (spec section 7) - same filters as GET.</summary>
    [HttpGet("export/excel")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ReportsExport)]
    public async Task<IActionResult> ExportExcel(
        [FromQuery] bool? activeOnly, [FromQuery] string? search,
        [FromServices] IReportExportService export)
    {
        var items = await _mediator.Send(new GetItemsQuery(activeOnly, search));
        var headers = new List<string>
            { "Code", "NameAr", "NameEn", "Category", "BaseUnit", "SecondaryUnit", "RequireBothUnits", "Active" };
        var rows = items.Select(i => new object?[]
        {
            i.Code, i.NameAr, i.NameEn, i.Category, i.BaseUnit.ToString(),
            i.SecondaryUnit?.ToString() ?? string.Empty, i.RequireBothUnits ? "Yes" : "No",
            i.IsActive ? "Yes" : "No"
        }).ToList();

        var bytes = export.GenerateExcel("Items", headers, rows);
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "items.xlsx");
    }

    /// <summary>The same list as a printable document (spec section 39), driven by the same filters.</summary>
    [HttpGet("export")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ReportsExport)]
    public async Task<IActionResult> Export(
        [FromQuery] bool? activeOnly, [FromQuery] string? search,
        [FromQuery] string format = "excel", [FromServices] IReportExportService export = null!)
    {
        var items = await _mediator.Send(new GetItemsQuery(activeOnly, search));
        var headers = new List<string>
            { "Code", "NameAr", "NameEn", "Category", "BaseUnit", "SecondaryUnit", "RequireBothUnits", "Active" };
        var rows = items.Select(i => new object?[]
        {
            i.Code, i.NameAr, i.NameEn, i.Category, i.BaseUnit.ToString(),
            i.SecondaryUnit?.ToString() ?? string.Empty, i.RequireBothUnits ? "Yes" : "No",
            i.IsActive ? "Yes" : "No"
        }).ToList();

        return ExportFileHelper.ToFile(export, format, "Item List", null, headers, rows, "items", "Items");
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

/// <summary>Body for an explicit item-scoped unit conversion rule (see <see cref="SetItemUnitConversionCommand"/>).</summary>
public record SetItemUnitConversionRequest(
    UnitOfMeasure FromUnit, UnitOfMeasure ToUnit, decimal Factor, string Operation,
    string? Notes = null, bool IsActive = true);
