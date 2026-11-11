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

/// <summary>Hosts the customers endpoints.</summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class CustomersController : ControllerBase
{
    private readonly ISender _mediator;
    /// <summary>Initializes a new instance of the CustomersController class.</summary>
    public CustomersController(ISender mediator) => _mediator = mediator;

    /// <summary>List customers, optionally filtered by active status and a code/name search term.</summary>
    [HttpGet]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.CustomersView)]
    [ProducesResponseType(typeof(List<CustomerDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<List<CustomerDto>>> Get([FromQuery] bool? activeOnly, [FromQuery] string? search)
        => Ok(await _mediator.Send(new GetCustomersQuery(activeOnly, search)));

    /// <summary>Create a new customer. Code is manually entered and must be unique (spec section 7).</summary>
    [HttpPost]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.CustomersCreate)]
    [ProducesResponseType(typeof(CustomerDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
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
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
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
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.CustomersEdit)]
    public async Task<ActionResult<CustomerDto>> SetActive(Guid id, [FromBody] SetCustomerActiveRequest request)
        => Ok(await _mediator.Send(new SetCustomerActiveCommand(id, request.IsActive)));

    /// <summary>
    /// Sets (or clears, with a null parent) this customer's direct parent - making it a
    /// sub-customer of another customer (spec section 7). Refuses self-parenting and any
    /// assignment that would create a cycle.
    /// </summary>
    [HttpPost("{id:guid}/parent")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.CustomersEdit)]
    public async Task<ActionResult<CustomerDto>> SetParent(Guid id, [FromBody] SetCustomerParentRequest request)
        => Ok(await _mediator.Send(new SetCustomerParentCommand(id, request.ParentCustomerId)));

    /// <summary>Excel export of the current customer list (spec section 37) - respects the same activeOnly/search filters as GET.</summary>
    [HttpGet("export/excel")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
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
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ReportsExport)]
    public async Task<IActionResult> Export([FromQuery] bool? activeOnly, [FromQuery] string? search,
        [FromQuery] string format = "excel", [FromServices] Application.Common.Interfaces.IReportExportService export = null!)
    {
        var customers = await _mediator.Send(new GetCustomersQuery(activeOnly, search));
        var headers = new List<string> { "Code", "Name", "Phone", "Address", "ContactPerson", "TaxNumber", "Active" };
        var rows = customers
            .Select(c => new object?[] { c.Code, c.Name, c.Phone, c.Address, c.ContactPerson, c.TaxNumber, c.IsActive ? "Yes" : "No" })
            .ToList();

        return Common.ExportFileHelper.ToFile(export, format, "Customer List", null,
            headers, rows, "customers", "Customers");
    }

    /// <summary>Step 1 of the import workflow: the template with the exact expected columns.</summary>
    [HttpGet("import/template")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
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
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.CustomersCreate)]
    public async Task<ActionResult<ImportPreviewDto>> PreviewImport(IFormFile file)
        => Ok(await _mediator.Send(new PreviewCustomerImportCommand(
            await Common.ExportFileHelper.ReadUploadAsync(file), AllowUpdate: false)));

    /// <summary>Step 3: re-validates the same file and imports only the valid rows (spec: "No silent invalid imports", "Transaction rollback on critical failure").</summary>
    [HttpPost("import/execute")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
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
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.CustomersEdit)]
    public async Task<ActionResult<ImportPreviewDto>> PreviewImportWithUpdate(IFormFile file)
        => Ok(await _mediator.Send(new PreviewCustomerImportCommand(
            await Common.ExportFileHelper.ReadUploadAsync(file), AllowUpdate: true)));

    /// <summary>Runs the execute import with update operation.</summary>
    /// <remarks>Route: POST api/Customers/import/execute-update. Requires the 'customers.edit' permission.</remarks>
    [HttpPost("import/execute-update")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.CustomersEdit)]
    public async Task<ActionResult<ImportExecuteResultDto>> ExecuteImportWithUpdate(IFormFile file)
        => Ok(await _mediator.Send(new ExecuteCustomerImportCommand(
            await Common.ExportFileHelper.ReadUploadAsync(file), AllowUpdate: true)));
}

/// <summary>Request payload to set customer active.</summary>
/// <param name="IsActive">Whether is active.</param>
public record SetCustomerActiveRequest(bool IsActive);

/// <summary>Body for assigning a sub-customer's parent; a null parent clears the relationship.</summary>
/// <param name="ParentCustomerId">The parent customer id.</param>
public record SetCustomerParentRequest(Guid? ParentCustomerId);
