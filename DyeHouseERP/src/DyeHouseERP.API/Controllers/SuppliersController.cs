using DyeHouseERP.API.Common;
using DyeHouseERP.Application.Common.Import;
using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.Suppliers.Commands;
using DyeHouseERP.Application.Suppliers.DTOs;
using DyeHouseERP.Application.Suppliers.Queries;
using DyeHouseERP.API.Authorization;
using DyeHouseERP.Domain.Common;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DyeHouseERP.API.Controllers;

/// <summary>
/// Supplier master (spec section 35). Required by the checks register (outgoing
/// supplier checks and endorsements) and by the purchases module that builds on
/// it next: code is manually entered and unique, phone/address stay optional.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class SuppliersController : ControllerBase
{
    private readonly ISender _mediator;
    public SuppliersController(ISender mediator) => _mediator = mediator;

    [HttpGet]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.SuppliersView)]
    [ProducesResponseType(typeof(List<SupplierDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<SupplierDto>>> Get([FromQuery] bool? activeOnly, [FromQuery] string? search)
        => Ok(await _mediator.Send(new GetSuppliersQuery(activeOnly, search)));

    [HttpPost]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.SuppliersCreate)]
    [ProducesResponseType(typeof(SupplierDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<SupplierDto>> Create([FromBody] CreateSupplierCommand command)
    {
        var result = await _mediator.Send(command);
        return CreatedAtAction(nameof(Get), new { }, result);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.SuppliersEdit)]
    public async Task<ActionResult<SupplierDto>> Update(Guid id, [FromBody] UpdateSupplierCommand command)
    {
        if (id != command.Id) command = command with { Id = id };
        return Ok(await _mediator.Send(command));
    }

    // ------------------------------------------- export / import (spec sections 35 + 38)

    /// <summary>Supplier list as Excel or PDF, honouring the same filters as GET.</summary>
    [HttpGet("export")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ReportsExport)]
    public async Task<IActionResult> Export([FromQuery] bool? activeOnly, [FromQuery] string? search,
        [FromQuery] string format = "excel", [FromServices] IReportExportService export = null!)
    {
        var suppliers = await _mediator.Send(new GetSuppliersQuery(activeOnly, search));
        var headers = new[] { "Code", "NameAr", "NameEn", "Account", "Phone", "Contact", "Tax number", "Active" };
        var rows = suppliers.Select(s => new object?[]
        {
            s.Code, s.NameAr, s.NameEn, s.AccountNumber, s.Phone, s.ContactPerson, s.TaxNumber, s.IsActive ? "Yes" : "No"
        }).ToList();

        return ExportFileHelper.ToFile(export, format, "Suppliers", "DyeHouse ERP", headers, rows, "suppliers", "Suppliers");
    }

    /// <summary>Step 1 of the import workflow: the template with the exact expected columns.</summary>
    [HttpGet("import/template")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.SuppliersView)]
    public IActionResult ImportTemplate([FromServices] IReportExportService export)
        => ExportFileHelper.Template(export, "Suppliers", SupplierImportTemplate.Headers, SupplierImportTemplate.SampleRows(), "suppliers-import-template");

    /// <summary>Step 2: validate the upload and report every row error. Writes nothing.</summary>
    [HttpPost("import/preview")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.SuppliersCreate)]
    public async Task<ActionResult<ImportPreviewDto>> PreviewImport(IFormFile file)
        => Ok(await _mediator.Send(new PreviewSupplierImportCommand(await ExportFileHelper.ReadUploadAsync(file), false)));

    /// <summary>Step 3: confirm - re-validates and creates only the valid new rows.</summary>
    [HttpPost("import/execute")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.SuppliersCreate)]
    public async Task<ActionResult<ImportExecuteResultDto>> ExecuteImport(IFormFile file)
        => Ok(await _mediator.Send(new ExecuteSupplierImportCommand(await ExportFileHelper.ReadUploadAsync(file), false)));

    /// <summary>Preview of the update-capable import - suppliers.edit is required to overwrite an existing account.</summary>
    [HttpPost("import/preview-update")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.SuppliersEdit)]
    public async Task<ActionResult<ImportPreviewDto>> PreviewImportWithUpdate(IFormFile file)
        => Ok(await _mediator.Send(new PreviewSupplierImportCommand(await ExportFileHelper.ReadUploadAsync(file), true)));

    /// <summary>Confirm of the update-capable import (spec rule: no silent overwrite).</summary>
    [HttpPost("import/execute-update")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.SuppliersEdit)]
    public async Task<ActionResult<ImportExecuteResultDto>> ExecuteImportWithUpdate(IFormFile file)
        => Ok(await _mediator.Send(new ExecuteSupplierImportCommand(await ExportFileHelper.ReadUploadAsync(file), true)));
}
