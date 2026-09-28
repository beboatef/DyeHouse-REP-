using DyeHouseERP.API.Common;
using DyeHouseERP.Application.Common.Import;
using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.Materials.Commands;
using DyeHouseERP.Application.Materials.DTOs;
using DyeHouseERP.Application.Materials.Queries;
using DyeHouseERP.API.Authorization;
using DyeHouseERP.Domain.Common;
using DyeHouseERP.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DyeHouseERP.API.Controllers;

[ApiController]
[Route("api/materials")]
[Authorize]
public class MaterialsController : ControllerBase
{
    private readonly ISender _mediator;
    public MaterialsController(ISender mediator) => _mediator = mediator;

    [HttpGet]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ItemsView)]
    public async Task<ActionResult<List<MaterialDto>>> Get([FromQuery] bool? activeOnly, [FromQuery] MaterialKind? kind)
        => Ok(await _mediator.Send(new GetMaterialsQuery(activeOnly, kind)));

    [HttpPost]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ItemsCreate)]
    public async Task<ActionResult<MaterialDto>> Create([FromBody] CreateMaterialCommand command)
    {
        var result = await _mediator.Send(command);
        return CreatedAtAction(nameof(Get), new { }, result);
    }

    // ------------------------------------------- export / import (spec section 38)

    /// <summary>Materials/chemicals list as Excel or PDF, honouring the same filters as GET.</summary>
    [HttpGet("export")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ReportsExport)]
    public async Task<IActionResult> Export([FromQuery] bool? activeOnly, [FromQuery] MaterialKind? kind,
        [FromQuery] string format = "excel", [FromServices] IReportExportService export = null!)
    {
        var materials = await _mediator.Send(new GetMaterialsQuery(activeOnly, kind));
        var headers = new[] { "Code", "Name", "Unit", "Kind", "Purchase price", "Reorder level", "Balance", "Active" };
        var rows = materials.Select(m => new object?[]
        {
            m.Code, m.Name, m.Unit.ToString(), m.Kind.ToString(), m.PurchasePrice, m.ReorderLevel,
            m.CurrentBalance, m.IsActive ? "Yes" : "No"
        }).ToList();

        return ExportFileHelper.ToFile(export, format, "Materials & Chemicals", "DyeHouse ERP", headers, rows, "materials", "Materials");
    }

    /// <summary>Step 1 of the import workflow: the template with the exact expected columns.</summary>
    [HttpGet("import/template")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ItemsView)]
    public IActionResult ImportTemplate([FromServices] IReportExportService export)
        => ExportFileHelper.Template(export, "Materials", MaterialImportTemplate.Headers, MaterialImportTemplate.SampleRows(), "materials-import-template");

    /// <summary>Step 2: validate the upload and show every row/field error. Writes nothing.</summary>
    [HttpPost("import/preview")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ItemsCreate)]
    public async Task<ActionResult<ImportPreviewDto>> PreviewImport(IFormFile file)
        => Ok(await _mediator.Send(new PreviewMaterialImportCommand(await ExportFileHelper.ReadUploadAsync(file), false)));

    /// <summary>Step 3: confirm - re-validates from scratch and creates only the valid new rows.</summary>
    [HttpPost("import/execute")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ItemsCreate)]
    public async Task<ActionResult<ImportExecuteResultDto>> ExecuteImport(IFormFile file)
        => Ok(await _mediator.Send(new ExecuteMaterialImportCommand(await ExportFileHelper.ReadUploadAsync(file), false)));

    /// <summary>Preview of the update-capable import - items.edit is required to overwrite existing materials.</summary>
    [HttpPost("import/preview-update")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ItemsEdit)]
    public async Task<ActionResult<ImportPreviewDto>> PreviewImportWithUpdate(IFormFile file)
        => Ok(await _mediator.Send(new PreviewMaterialImportCommand(await ExportFileHelper.ReadUploadAsync(file), true)));

    /// <summary>Confirm of the update-capable import (spec rule: no silent overwrite).</summary>
    [HttpPost("import/execute-update")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ItemsEdit)]
    public async Task<ActionResult<ImportExecuteResultDto>> ExecuteImportWithUpdate(IFormFile file)
        => Ok(await _mediator.Send(new ExecuteMaterialImportCommand(await ExportFileHelper.ReadUploadAsync(file), true)));
}

[ApiController]
[Route("api/material-transfers")]
[Authorize]
public class MaterialTransfersController : ControllerBase
{
    private readonly ISender _mediator;
    public MaterialTransfersController(ISender mediator) => _mediator = mediator;

    [HttpGet]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.InventoryView)]
    public async Task<ActionResult<List<MaterialTransferDto>>> Get() => Ok(await _mediator.Send(new GetMaterialTransfersQuery()));

    [HttpPost]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.InventoryAdjust)]
    public async Task<ActionResult<MaterialTransferDto>> Create([FromBody] CreateMaterialTransferCommand command)
    {
        var result = await _mediator.Send(command);
        return CreatedAtAction(nameof(Get), new { }, result);
    }

    /// <summary>Material transfers as Excel or PDF (spec sections 25 + 39).</summary>
    [HttpGet("export")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ReportsExport)]
    public async Task<IActionResult> Export([FromQuery] string format = "excel", [FromServices] IReportExportService export = null!)
    {
        var transfers = await _mediator.Send(new GetMaterialTransfersQuery());
        var headers = new[] { "Transfer no.", "Date", "Material", "From warehouse", "To warehouse", "Quantity" };
        var rows = transfers.Select(t => new object?[]
        {
            t.TransferNumber, t.TransferDate.ToString("yyyy-MM-dd"), t.MaterialCode,
            t.FromWarehouseName, t.ToWarehouseName, t.Quantity
        }).ToList();

        return ExportFileHelper.ToFile(export, format, "Material Transfers", "DyeHouse ERP", headers, rows, "material-transfers", "Transfers");
    }
}

[ApiController]
[Route("api/material-issues")]
[Authorize]
public class MaterialIssuesController : ControllerBase
{
    private readonly ISender _mediator;
    public MaterialIssuesController(ISender mediator) => _mediator = mediator;

    [HttpGet]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ProductionView)]
    public async Task<ActionResult<List<MaterialIssueDto>>> Get([FromQuery] Guid? productionOrderId)
        => Ok(await _mediator.Send(new GetMaterialIssuesQuery(productionOrderId)));

    [HttpPost]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ProductionExecuteStage)]
    public async Task<ActionResult<MaterialIssueDto>> Create([FromBody] CreateMaterialIssueCommand command)
    {
        var result = await _mediator.Send(command);
        return CreatedAtAction(nameof(Get), new { }, result);
    }

    /// <summary>Chemical issues to Job Orders as Excel or PDF - each row names the Job Order it was charged to.</summary>
    [HttpGet("export")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ReportsExport)]
    public async Task<IActionResult> Export([FromQuery] string format = "excel", [FromServices] IReportExportService export = null!)
    {
        var issues = await _mediator.Send(new GetMaterialIssuesQuery());
        var headers = new[] { "Issue no.", "Date", "Material", "Job order", "Quantity", "Unit cost", "Total cost" };
        var rows = issues.Select(i => new object?[]
        {
            i.IssueNumber, i.IssueDate.ToString("yyyy-MM-dd"), $"{i.MaterialCode} - {i.MaterialName}",
            i.ProductionOrderNumber, i.Quantity, i.UnitCost, i.TotalCost
        }).ToList();

        return ExportFileHelper.ToFile(export, format, "Material Issues", "DyeHouse ERP", headers, rows, "material-issues", "Issues");
    }
}

[ApiController]
[Route("api/material-preparations")]
[Authorize]
public class MaterialPreparationsController : ControllerBase
{
    private readonly ISender _mediator;
    public MaterialPreparationsController(ISender mediator) => _mediator = mediator;

    [HttpGet]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ProductionView)]
    public async Task<ActionResult<List<MaterialPreparationDto>>> Get() => Ok(await _mediator.Send(new GetMaterialPreparationsQuery()));

    [HttpPost]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ProductionExecuteStage)]
    public async Task<ActionResult<MaterialPreparationDto>> Create([FromBody] CreateMaterialPreparationCommand command)
    {
        var result = await _mediator.Send(command);
        return CreatedAtAction(nameof(Get), new { }, result);
    }

    /// <summary>Preparation/dilution records as Excel or PDF (spec section 26).</summary>
    [HttpGet("export")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ReportsExport)]
    public async Task<IActionResult> Export([FromQuery] string format = "excel", [FromServices] IReportExportService export = null!)
    {
        var preparations = await _mediator.Send(new GetMaterialPreparationsQuery());
        var headers = new[] { "Preparation no.", "Date", "Material", "Original qty", "Water qty", "Resulting qty", "Concentration", "Cost" };
        var rows = preparations.Select(p => new object?[]
        {
            p.PreparationNumber, p.PreparationDate.ToString("yyyy-MM-dd"), p.OriginalMaterialCode,
            p.OriginalQuantity, p.WaterQuantity, p.ResultingQuantity, p.Concentration, p.Cost
        }).ToList();

        return ExportFileHelper.ToFile(export, format, "Material Preparation", "DyeHouse ERP", headers, rows, "material-preparations", "Preparations");
    }
}
