using DyeHouseERP.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.ReadyGoods.Commands;

public sealed record DeleteReadyGoodsTransferCommand(Guid Id) : IRequest<Unit>;

public sealed class DeleteReadyGoodsTransferCommandHandler
    : IRequestHandler<DeleteReadyGoodsTransferCommand, Unit>
{
    private readonly IApplicationDbContext _db;
    private readonly IInventoryMovementPermissionService _permissionService;

    public DeleteReadyGoodsTransferCommandHandler(
        IApplicationDbContext db,
        IInventoryMovementPermissionService permissionService)
    {
        _db = db;
        _permissionService = permissionService;
    }

    public async Task<Unit> Handle(
        DeleteReadyGoodsTransferCommand request,
        CancellationToken cancellationToken)
    {
        var transfer = await _db.ReadyGoodsTransfers
            .FirstOrDefaultAsync(x => x.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException("Ready goods transfer not found.");

        _permissionService.EnsureCanDelete(transfer.ProductionOrderId);

        _db.ReadyGoodsTransfers.Remove(transfer);
        await _db.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}
