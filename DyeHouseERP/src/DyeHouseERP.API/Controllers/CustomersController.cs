using DyeHouseERP.Application.Customers.Commands;
using DyeHouseERP.Application.Customers.DTOs;
using DyeHouseERP.Application.Customers.Queries;
using DyeHouseERP.API.Authorization;
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

    /// <summary>Excel export of the current customer list (spec section 37) - respects the same activeOnly/search filters as GET.</summary>
    [HttpGet("export/excel")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ReportsExport)]
    public async Task<IActionResult> ExportExcel([FromQuery] bool? activeOnly, [FromQuery] string? search, [FromServices] Application.Common.Interfaces.IReportExportService export)
    {
        var customers = await _mediator.Send(new GetCustomersQuery(activeOnly, search));
        var headers = new List<string> { "Code", "Name", "Active" };
        var rows = customers.Select(c => new object?[] { c.Code, c.Name, c.IsActive ? "Yes" : "No" }).ToList();
        var bytes = export.GenerateExcel("Customers", headers, rows);
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "customers.xlsx");
    }

    /// <summary>
    /// Step 1 of the Excel import workflow (spec section 38): parses and
    /// validates the uploaded file WITHOUT saving anything, returning a
    /// per-row preview with any errors so the user can review before
    /// confirming. Expected columns: Code, Name.
    /// </summary>
    [HttpPost("import/preview")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.CustomersCreate)]
    public async Task<ActionResult<CustomerImportPreviewDto>> PreviewImport(IFormFile file)
    {
        await using var stream = new MemoryStream();
        await file.CopyToAsync(stream);
        return Ok(await _mediator.Send(new PreviewCustomerImportCommand(stream.ToArray())));
    }

    /// <summary>Step 2: re-validates the same file and imports only the valid rows (spec: "No silent invalid imports", "Transaction rollback on critical failure").</summary>
    [HttpPost("import/execute")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.CustomersCreate)]
    public async Task<ActionResult<CustomerImportExecuteResultDto>> ExecuteImport(IFormFile file)
    {
        await using var stream = new MemoryStream();
        await file.CopyToAsync(stream);
        return Ok(await _mediator.Send(new ExecuteCustomerImportCommand(stream.ToArray())));
    }
}
