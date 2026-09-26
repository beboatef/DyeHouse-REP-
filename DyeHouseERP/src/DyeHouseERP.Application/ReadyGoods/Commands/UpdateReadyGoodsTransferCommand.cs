using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.Common.Services;
using DyeHouseERP.Application.ReadyGoods.DTOs;
using DyeHouseERP.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.ReadyGoods.Commands;

public sealed record UpdateReadyGoodsTransferCommand(
    Guid Id,
    DateTime TransferDate,
    decimal? QuantityKg,
    decimal? QuantityMeter,
    int? PieceCount,
    string? Notes) : IRequest<ReadyGoodsTransferDto>;

public sealed class UpdateReadyGoodsTransferCommandHandler
    : IRequestHandler<UpdateReadyGoodsTransferCommand, ReadyGoodsTransferDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IInventoryMovementPermissionService _permissionService;

    public UpdateReadyGoodsTransferCommandHandler(
        IApplicationDbContext db,
        IInventoryMovementPermissionService permissionService)
    {
        _db = db;
        _permissionService = permissionService;
    }

    public async Task<ReadyGoodsTransferDto> Handle(
        UpdateReadyGoodsTransferCommand request,
        CancellationToken cancellationToken)
    {
        var transfer = await _db.ReadyGoodsTransfers
            .FirstOrDefaultAsync(x => x.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException("Ready goods transfer not found.");

        _permissionService.EnsureCanEdit(transfer.ProductionOrderId);

        if (request.QuantityKg is null && request.QuantityMeter is null)
            throw new ArgumentException("KG and/or Meter quantity is required.");

        transfer.UpdateDetails(
            request.TransferDate,
            request.QuantityKg,
            request.QuantityMeter,
            request.PieceCount,
            request.Notes);

        await _db.SaveChangesAsync(cancellationToken);

        var order = await _db.ProductionOrders
            .AsNoTracking()
            .FirstAsync(x => x.Id == transfer.ProductionOrderId, cancellationToken);

        var customer = await _db.Customers
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == transfer.CustomerId, cancellationToken);

        var item = await _db.Items
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == transfer.ItemId, cancellationToken);

        return new ReadyGoodsTransferDto
        {
            Id = transfer.Id,
            TransferNumber = transfer.TransferNumber,
            TransferDate = transfer.TransferDate,
            ProductionOrderId = order.Id,
            ProductionOrderNumber = order.OrderNumber,
            CustomerId = order.CustomerId,
            CustomerCode = customer?.Code ?? "",
            ItemId = order.ItemId,
            ItemCode = item?.Code ?? "",
            Color = order.Color,
            QuantityKg = transfer.QuantityKg,
            QuantityMeter = transfer.QuantityMeter,
            PieceCount = transfer.PieceCount
        };
    }
}
