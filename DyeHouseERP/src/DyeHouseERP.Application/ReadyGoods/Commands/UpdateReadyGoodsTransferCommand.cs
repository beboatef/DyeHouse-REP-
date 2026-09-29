using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.Common.Services;
using DyeHouseERP.Application.ReadyGoods.DTOs;
using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.ReadyGoods.Commands;

/// <summary>
/// Ready-goods transfers are posted atomically at creation: the transfer row
/// and its InventoryTransaction IN rows commit together, and the ready
/// balance is always a live sum over that ledger (spec sections 18, 29, 30).
/// Editing the quantities of a posted document can never touch the ledger
/// (the ledger is append-only), so any edit would silently split the
/// document from the stock it claims. Corrections therefore follow the same
/// path as every other stock-posting document in the system: cancel the
/// transfer - which posts reversal rows - and create a new one if needed.
/// Date and free-text notes (nothing that the ledger depends on) may still
/// be corrected here; quantities and warehouse cannot.
/// </summary>
public sealed record UpdateReadyGoodsTransferCommand(
    Guid Id,
    DateTime TransferDate,
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

        if (transfer.Status == ReadyGoodsTransferStatus.Cancelled)
            throw new DomainException("A cancelled ready goods transfer cannot be edited.");

        _permissionService.EnsureCanEdit(transfer.ProductionOrderId);

        transfer.UpdateDetails(request.TransferDate, request.PieceCount, request.Notes);

        await _db.SaveChangesAsync(cancellationToken);

        return await DeleteReadyGoodsTransferCommandHandler.LoadDtoAsync(_db, transfer.Id, cancellationToken);
    }
}
