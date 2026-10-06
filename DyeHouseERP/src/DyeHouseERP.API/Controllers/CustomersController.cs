using DyeHouseERP.Application.Customers.Commands;
using DyeHouseERP.Application.Customers.DTOs;
using DyeHouseERP.Application.Customers.Queries;
using DyeHouseERP.API.Authorization;
using DyeHouseERP.Application.Common.Import;
using DyeHouseERP.Domain.Common;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DyeHouseERP.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class CustomersController : ControllerBase
{
    private readonly ISender _mediator;
    public CustomersController(ISender mediator) => _mediator = mediator;

    /// <summary>List customers, optionally filtered by active status and a code/name search term.</summary>
    [HttpGet]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.CustomersView)]
    [ProducesResponseType(typeof(List<CustomerDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<CustomerDto>>> Get([FromQuery] bool? activeOnly, [FromQuery] string? search)
        => Ok(await _mediator.Send(new GetCustomersQuery(activeOnly, search)));

    /// <summary>Create a new customer. Code is manually entered and must be unique (spec section 7).</summary>
    [HttpPost]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.CustomersCreate)]
    [ProducesResponseType(typeof(CustomerDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<CustomerDto>> Create([FromBody] CreateCustomerCommand command)
    {
        var result = await _mediator.Send(command);
        return CreatedAtAction(nameof(Get), new { }, result);
    }

    /// <summary>
    /// Updates a customer's name and contact details (spec section 7). The Code is never
    /// editable - it is the key every downstream document already refers to.
    /// </summary>
    [HttpPut("{id:guid}")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.CustomersEdit)]
    public async Task<ActionResult<CustomerDto>> Update(Guid id, [FromBody] UpdateCustomerCommand command)
    {
        if (id != command.Id) command = command with { Id = id };
        return Ok(await _mediator.Send(command));
    }

    /// <summary>
    /// Deactivation, never deletion: a customer that owns raw material, invoices and deliveries
    /// must stay resolvable so old documents keep resolving.
    /// </summary>
    [HttpPost("{id:guid}/active")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.CustomersEdit)]
    public async Task<ActionResult<CustomerDto>> SetActive(Guid id, [FromBody] SetCustomerActiveRequest request)
        => Ok(await _mediator.Send(new SetCustomerActiveCommand(id, request.IsActive)));

    /// <summary>Excel export of the current customer list (spec section 37) - respects the same activeOnly/search filters as GET.</summary>
    [HttpGet("export/excel")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ReportsExport)]
    public async Task<IActionResult> ExportExcel([FromQuery] bool? activeOnly, [FromQuery] string? search, [FromServices] Application.Common.Interfaces.IReportExportService export)
    {
        var customers = await _mediator.Send(new GetCustomersQuery(activeOnly, search));
        var headers = new List<string> { "Code", "Name", "Phone", "Address", "ContactPerson", "TaxNumber", "Active" };
        var rows = customers.Select(c => new object?[]
        {
            c.Code, c.Name, c.Phone, c.Address, c.ContactPerson, c.TaxNumber, c.IsActive ? "Yes" : "No"
        }).ToList();
        var bytes = export.GenerateExcel("Customers", headers, rows);
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "customers.xlsx");
    }

    /// <summary>
    /// The same list as a printable document (spec section 39) - one `format`
    /// switch, so the Customers screen can offer Excel and PDF from the same
    /// filters without a second round of code.
    /// </summary>
    [HttpGet("export")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ReportsExport)]
    public async Task<IActionResult> Export([FromQuery] bool? activeOnly, [FromQuery] string? search,
        [FromQuery] string format = "excel", [FromServices] Application.Common.Interfaces.IReportExportService export = null!)
    {
        var customers = await _mediator.Send(new GetCustomersQuery(activeOnly, search));
        var headers = new List<string> { "Code", "Name", "Phone", "Address", "ContactPerson", "TaxNumber", "Active" };
        var rows = customers
            .Select(c => new object?[] { c.Code, c.Name, c.Phone, c.Address, c.ContactPerson, c.TaxNumber, c.IsActive ? "Yes" : "No" })
            .ToList();

        return Common.ExportFileHelper.ToFile(export, format, "Customer List", "DyeHouse ERP",
            headers, rows, "customers", "Customers");
    }

    /// <summary>Step 1 of the import workflow: the template with the exact expected columns.</summary>
    [HttpGet("import/template")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.CustomersView)]
    public IActionResult ImportTemplate([FromServices] Application.Common.Interfaces.IReportExportService export)
        => Common.ExportFileHelper.Template(export, "Customers", CustomerImportTemplate.Headers,
            CustomerImportTemplate.SampleRows(), "customers-import-template");

    /// <summary>
    /// Step 2 of the Excel import workflow (spec section 38): parses and
    /// validates the uploaded file WITHOUT saving anything, returning a
    /// per-row preview with any errors so the user can review before
    /// confirming. Header names accept English or Arabic spellings.
    /// </summary>
    [HttpPost("import/preview")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.CustomersCreate)]
    public async Task<ActionResult<ImportPreviewDto>> PreviewImport(IFormFile file)
        => Ok(await _mediator.Send(new PreviewCustomerImportCommand(
            await Common.ExportFileHelper.ReadUploadAsync(file), AllowUpdate: false)));

    /// <summary>Step 3: re-validates the same file and imports only the valid rows (spec: "No silent invalid imports", "Transaction rollback on critical failure").</summary>
    [HttpPost("import/execute")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.CustomersCreate)]
    public async Task<ActionResult<ImportExecuteResultDto>> ExecuteImport(IFormFile file)
        => Ok(await _mediator.Send(new ExecuteCustomerImportCommand(
            await Common.ExportFileHelper.ReadUploadAsync(file), AllowUpdate: false)));

    /// <summary>
    /// The same two steps for a user who may also update existing customers.
    /// `customers.edit` is required here and only here - a create-only user
    /// cannot reach an endpoint that would overwrite a master record.
    /// </summary>
    [HttpPost("import/preview-update")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.CustomersEdit)]
    public async Task<ActionResult<ImportPreviewDto>> PreviewImportWithUpdate(IFormFile file)
        => Ok(await _mediator.Send(new PreviewCustomerImportCommand(
            await Common.ExportFileHelper.ReadUploadAsync(file), AllowUpdate: true)));

    [HttpPost("import/execute-update")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.CustomersEdit)]
    public async Task<ActionResult<ImportExecuteResultDto>> ExecuteImportWithUpdate(IFormFile file)
        => Ok(await _mediator.Send(new ExecuteCustomerImportCommand(
            await Common.ExportFileHelper.ReadUploadAsync(file), AllowUpdate: true)));
}

public record SetCustomerActiveRequest(bool IsActive);
