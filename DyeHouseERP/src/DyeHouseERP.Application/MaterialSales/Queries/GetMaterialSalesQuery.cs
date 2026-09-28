using DyeHouseERP.Application.Common.Exceptions;
using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.MaterialSales.DTOs;
using DyeHouseERP.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.MaterialSales.Queries;

/// <summary>Factory-owned materials sales list (spec section 26).</summary>
public record GetMaterialSalesQuery(MaterialSaleStatus? Status = null, Guid? CustomerId = null,
    DateTime? From = null, DateTime? To = null, string? Search = null) : IRequest<List<MaterialSaleDto>>;

public class GetMaterialSalesQueryHandler : IRequestHandler<GetMaterialSalesQuery, List<MaterialSaleDto>>
{
    private readonly IApplicationDbContext _db;
    public GetMaterialSalesQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<List<MaterialSaleDto>> Handle(GetMaterialSalesQuery request, CancellationToken cancellationToken)
    {
        var query = _db.MaterialSales.AsNoTracking().Include(s => s.Lines).AsQueryable();

        if (request.Status is not null) query = query.Where(s => s.Status == request.Status);
        if (request.CustomerId is not null) query = query.Where(s => s.CustomerId == request.CustomerId);
        if (request.From is not null) query = query.Where(s => s.SaleDate >= request.From.Value);
        if (request.To is not null) query = query.Where(s => s.SaleDate <= request.To.Value);
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim();
            query = query.Where(s => s.SaleNumber.Contains(term) || s.BuyerName.Contains(term));
        }

        var sales = await query.OrderByDescending(s => s.SaleDate).ThenByDescending(s => s.SaleNumber)
            .Take(500)
            .ToListAsync(cancellationToken);

        return await MaterialSaleDtoBuilder.BuildManyAsync(_db, sales, cancellationToken);
    }
}

public record GetMaterialSaleByIdQuery(Guid Id) : IRequest<MaterialSaleDto>;

public class GetMaterialSaleByIdQueryHandler : IRequestHandler<GetMaterialSaleByIdQuery, MaterialSaleDto>
{
    private readonly IApplicationDbContext _db;
    public GetMaterialSaleByIdQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<MaterialSaleDto> Handle(GetMaterialSaleByIdQuery request, CancellationToken cancellationToken)
    {
        var sale = await _db.MaterialSales.AsNoTracking().Include(s => s.Lines)
            .FirstOrDefaultAsync(s => s.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("MaterialSale", request.Id);

        return (await MaterialSaleDtoBuilder.BuildManyAsync(_db, new[] { sale }, cancellationToken))[0];
    }
}

internal static class MaterialSaleDtoBuilder
{
    public static async Task<List<MaterialSaleDto>> BuildManyAsync(
        IApplicationDbContext db, IReadOnlyCollection<Domain.Entities.MaterialSale> sales, CancellationToken ct)
    {
        if (sales.Count == 0) return new List<MaterialSaleDto>();

        var warehouseIds = sales.Select(s => s.WarehouseId).Distinct().ToList();
        var customerIds = sales.Where(s => s.CustomerId != null).Select(s => s.CustomerId!.Value).Distinct().ToList();
        var accountIds = sales.Where(s => s.TreasuryAccountId != null).Select(s => s.TreasuryAccountId!.Value).Distinct().ToList();
        var materialIds = sales.SelectMany(s => s.Lines).Select(l => l.MaterialId).Distinct().ToList();

        var warehouses = await db.Warehouses.AsNoTracking()
            .Where(w => warehouseIds.Contains(w.Id)).ToDictionaryAsync(w => w.Id, w => w.Name, ct);
        var accounts = await db.TreasuryAccounts.AsNoTracking()
            .Where(a => accountIds.Contains(a.Id)).ToDictionaryAsync(a => a.Id, a => a.Name, ct);
        var materials = await db.Materials.AsNoTracking()
            .Where(m => materialIds.Contains(m.Id)).ToDictionaryAsync(m => m.Id, m => m, ct);

        var customers = await db.Customers.AsNoTracking()
            .Where(c => customerIds.Contains(c.Id))
            .Select(c => new { c.Id, c.Code, c.Name })
            .ToDictionaryAsync(c => c.Id, c => c, ct);

        var result = new List<MaterialSaleDto>(sales.Count);

        foreach (var sale in sales)
        {
            var dto = new MaterialSaleDto
            {
                Id = sale.Id, SaleNumber = sale.SaleNumber, SaleDate = sale.SaleDate,
                WarehouseId = sale.WarehouseId,
                WarehouseName = warehouses.TryGetValue(sale.WarehouseId, out var wn) ? wn : string.Empty,
                CustomerId = sale.CustomerId,
                CustomerCode = sale.CustomerId is not null && customers.TryGetValue(sale.CustomerId.Value, out var cu) ? cu.Code : null,
                BuyerName = sale.BuyerName,
                TreasuryAccountId = sale.TreasuryAccountId,
                TreasuryAccountName = sale.TreasuryAccountId is not null && accounts.TryGetValue(sale.TreasuryAccountId.Value, out var an) ? an : null,
                PaymentMethod = sale.PaymentMethod,
                Discount = sale.Discount, Tax = sale.Tax, SubTotal = sale.SubTotal, Total = sale.Total,
                Notes = sale.Notes, Status = sale.Status, IsEditable = sale.IsEditable,
                PostedBy = sale.PostedBy, PostedAtUtc = sale.PostedAtUtc,
                CancelledBy = sale.CancelledBy, CancelledAtUtc = sale.CancelledAtUtc,
                CancellationReason = sale.CancellationReason,
                CreatedBy = sale.CreatedBy, CreatedAtUtc = sale.CreatedAtUtc
            };

            foreach (var line in sale.Lines)
            {
                materials.TryGetValue(line.MaterialId, out var material);
                dto.Lines.Add(new MaterialSaleLineDto
                {
                    Id = line.Id, MaterialId = line.MaterialId,
                    MaterialCode = material?.Code ?? string.Empty,
                    MaterialName = material?.Name ?? string.Empty,
                    Quantity = line.Quantity, Unit = line.Unit, UnitPrice = line.UnitPrice,
                    LineTotal = line.LineTotal, Description = line.Description
                });
            }

            result.Add(dto);
        }

        return result;
    }
}
