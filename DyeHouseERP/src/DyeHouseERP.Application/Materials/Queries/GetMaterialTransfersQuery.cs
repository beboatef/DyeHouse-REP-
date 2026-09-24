using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.Materials.DTOs;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.Materials.Queries;

public record GetMaterialTransfersQuery : IRequest<List<MaterialTransferDto>>;

public class GetMaterialTransfersQueryHandler : IRequestHandler<GetMaterialTransfersQuery, List<MaterialTransferDto>>
{
    private readonly IApplicationDbContext _db;
    public GetMaterialTransfersQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<List<MaterialTransferDto>> Handle(GetMaterialTransfersQuery request, CancellationToken cancellationToken)
    {
        var transfers = await _db.MaterialTransfers.AsNoTracking().OrderByDescending(t => t.TransferDate).ToListAsync(cancellationToken);
        if (transfers.Count == 0) return new List<MaterialTransferDto>();

        var materials = await _db.Materials.AsNoTracking().Where(m => transfers.Select(t => t.MaterialId).Contains(m.Id)).ToDictionaryAsync(m => m.Id, cancellationToken);
        var warehouseIds = transfers.SelectMany(t => new[] { t.FromWarehouseId, t.ToWarehouseId }).Distinct().ToList();
        var warehouses = await _db.Warehouses.AsNoTracking().Where(w => warehouseIds.Contains(w.Id)).ToDictionaryAsync(w => w.Id, cancellationToken);

        return transfers.Select(t => new MaterialTransferDto
        {
            Id = t.Id, TransferNumber = t.TransferNumber, TransferDate = t.TransferDate,
            MaterialId = t.MaterialId, MaterialCode = materials.GetValueOrDefault(t.MaterialId)?.Code ?? "",
            FromWarehouseId = t.FromWarehouseId, FromWarehouseName = warehouses.GetValueOrDefault(t.FromWarehouseId)?.Name ?? "",
            ToWarehouseId = t.ToWarehouseId, ToWarehouseName = warehouses.GetValueOrDefault(t.ToWarehouseId)?.Name ?? "",
            Quantity = t.Quantity, Notes = t.Notes
        }).ToList();
    }
}
