using DyeHouseERP.API.Authorization;
using DyeHouseERP.Application.Accounting.Queries;
using DyeHouseERP.Domain.Common;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DyeHouseERP.API.Controllers;

/// <summary>
/// Accounting export for the factory's accountant (spec Part 2): five export
/// sets as CSV (UTF-8 with BOM, one set per file) or XLSX (one sheet per set).
/// Read-only apart from the required audit entry (date range, format, row
/// counts - never file contents). Column mapping is documented in
/// docs/accounting/export-mapping.md and must be confirmed with the
/// accountant before the first real import.
/// </summary>
[ApiController]
[Route("api/accounting")]
[Authorize]
public class AccountingController : ControllerBase
{
    private readonly ISender _mediator;
    /// <summary>Initializes a new instance of the AccountingController class.</summary>
    public AccountingController(ISender mediator) => _mediator = mediator;

    /// <summary>Exports accounting data.</summary>
    /// <remarks>Route: GET api/accounting/export. Requires the 'accounting.export' permission.</remarks>
    [HttpGet("export")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.AccountingExport)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Export(
        [FromQuery] DateTime from, [FromQuery] DateTime to,
        [FromQuery] Guid? customerId, [FromQuery] Guid? supplierId,
        [FromQuery] string format = "xlsx", [FromQuery] string? set = null)
    {
        var file = await _mediator.Send(new ExportAccountingQuery(from, to, customerId, supplierId, format, set));
        return File(file.Content, file.ContentType, file.FileName);
    }
}
