using DyeHouseERP.Application.Items.Commands;
using DyeHouseERP.Application.Items.DTOs;
using DyeHouseERP.Application.Items.Queries;
using DyeHouseERP.API.Authorization;
using DyeHouseERP.Domain.Common;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DyeHouseERP.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ItemsController : ControllerBase
{
    private readonly ISender _mediator;
    public ItemsController(ISender mediator) => _mediator = mediator;

    /// <summary>List items. BaseUnit is always exactly one of KG or Meter (spec section 5/8).</summary>
    [HttpGet]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ItemsView)]
    [ProducesResponseType(typeof(List<ItemDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<ItemDto>>> Get([FromQuery] bool? activeOnly, [FromQuery] string? search)
        => Ok(await _mediator.Send(new GetItemsQuery(activeOnly, search)));

    [HttpPost]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ItemsCreate)]
    [ProducesResponseType(typeof(ItemDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<ItemDto>> Create([FromBody] CreateItemCommand command)
    {
        var result = await _mediator.Send(command);
        return CreatedAtAction(nameof(Get), new { }, result);
    }

    /// <summary>Excel export of the current item list (spec section 37).</summary>
    [HttpGet("export/excel")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ReportsExport)]
    public async Task<IActionResult> ExportExcel([FromQuery] bool? activeOnly, [FromQuery] string? search, [FromServices] Application.Common.Interfaces.IReportExportService export)
    {
        var items = await _mediator.Send(new GetItemsQuery(activeOnly, search));
        var headers = new List<string> { "Code", "Name", "BaseUnit", "Active" };
        var rows = items.Select(i => new object?[] { i.Code, i.Name, i.BaseUnit.ToString(), i.IsActive ? "Yes" : "No" }).ToList();
        var bytes = export.GenerateExcel("Items", headers, rows);
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "items.xlsx");
    }

    /// <summary>Step 1 of the Excel import workflow (spec section 38): preview + validate only. Expected columns: Code, Name, BaseUnit (KG/Meter).</summary>
    [HttpPost("import/preview")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ItemsCreate)]
    public async Task<ActionResult<ItemImportPreviewDto>> PreviewImport(IFormFile file)
    {
        await using var stream = new MemoryStream();
        await file.CopyToAsync(stream);
        return Ok(await _mediator.Send(new PreviewItemImportCommand(stream.ToArray())));
    }

    /// <summary>Step 2: re-validates and imports only the valid rows.</summary>
    [HttpPost("import/execute")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ItemsCreate)]
    public async Task<ActionResult<ItemImportExecuteResultDto>> ExecuteImport(IFormFile file)
    {
        await using var stream = new MemoryStream();
        await file.CopyToAsync(stream);
        return Ok(await _mediator.Send(new ExecuteItemImportCommand(stream.ToArray())));
    }
}
