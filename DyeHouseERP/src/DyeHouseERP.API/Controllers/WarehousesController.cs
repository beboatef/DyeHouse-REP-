using DyeHouseERP.API.Authorization;
using DyeHouseERP.Domain.Common;
using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.API.Controllers;

public record WarehouseDto(Guid Id, string Code, string Name, WarehouseKind Kind, bool IsActive);
public record CreateWarehouseRequest(string Code, string Name, WarehouseKind Kind);

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class WarehousesController : ControllerBase
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public WarehousesController(IApplicationDbContext db, ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    [HttpGet]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.InventoryView)]
    public async Task<ActionResult<List<WarehouseDto>>> Get([FromQuery] WarehouseKind? kind)
    {
        var query = _db.Warehouses.AsNoTracking().AsQueryable();
        if (kind.HasValue) query = query.Where(w => w.Kind == kind);

        var result = await query
            .OrderBy(w => w.Code)
            .Select(w => new WarehouseDto(w.Id, w.Code, w.Name, w.Kind, w.IsActive))
            .ToListAsync();

        return Ok(result);
    }

    [HttpPost]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.SettingsManage)]
    public async Task<ActionResult<WarehouseDto>> Create([FromBody] CreateWarehouseRequest request)
    {
        var warehouse = new Warehouse(request.Code, request.Name, request.Kind, _currentUser.UserName);
        _db.Warehouses.Add(warehouse);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(Get), new { }, new WarehouseDto(warehouse.Id, warehouse.Code, warehouse.Name, warehouse.Kind, warehouse.IsActive));
    }
}
