using DyeHouseERP.Application.Common.Exceptions;
using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.ReadyGoods.DTOs;
using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Enums;
using DyeHouseERP.Domain.Exceptions;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.ReadyGoods.Commands;

/// <summary>
/// "ترحيل" - transfers completed production output into the Ready Goods
/// warehouse (spec section 29). Validates the order's stages are done and
/// prevents a duplicate transfer for the same order (a second call for the
/// same ProductionOrderId fails on the DB's unique index as a backstop, but
/// this check gives a friendly error first).
/// </summary>
public record CreateReadyGoodsTransferCommand(
    Guid ProductionOrderId, Guid WarehouseId, decimal? QuantityKg, decimal? QuantityMeter, int? PieceCount, string? Notes)
    : IRequest<ReadyGoodsTransferDto>;

public class CreateReadyGoodsTransferCommandValidator : AbstractValidator<CreateReadyGoodsTransferCommand>
{
    public CreateReadyGoodsTransferCommandValidator()
    {
        RuleFor(x => x.ProductionOrderId).NotEmpty();
        RuleFor(x => x.WarehouseId).NotEmpty();
        RuleFor(x => x).Must(x => x.QuantityKg is > 0 || x.QuantityMeter is > 0)
            .WithMessage("Specify the transferred quantity in KG and/or Meter.");
    }
}

public class CreateReadyGoodsTransferCommandHandler : IRequestHandler<CreateReadyGoodsTransferCommand, ReadyGoodsTransferDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IDocumentNumberGenerator _numberGenerator;
    private readonly IDateTime _clock;

    public CreateReadyGoodsTransferCommandHandler(
        IApplicationDbContext db, ICurrentUserService currentUser, IDocumentNumberGenerator numberGenerator, IDateTime clock)
    {
        _db = db; _currentUser = currentUser; _numberGenerator = numberGenerator; _clock = clock;
    }

    public async Task<ReadyGoodsTransferDto> Handle(CreateReadyGoodsTransferCommand request, CancellationToken cancellationToken)
    {
        var order = await _db.ProductionOrders.FirstOrDefaultAsync(o => o.Id == request.ProductionOrderId, cancellationToken)
            ?? throw new NotFoundException("ProductionOrder", request.ProductionOrderId);

        var alreadyTransferred = await _db.ReadyGoodsTransfers.AnyAsync(t => t.ProductionOrderId == order.Id, cancellationToken);
        if (alreadyTransferred)
            throw new DomainException($"Production order '{order.OrderNumber}' has already been transferred to ready goods.");

        var pendingStages = await _db.ProductionOrderStageExecutions
            .Where(s => s.ProductionOrderId == order.Id && (s.Status == StageExecutionStatus.Pending || s.Status == StageExecutionStatus.InProgress))
            .AnyAsync(cancellationToken);
        if (pendingStages)
            throw new DomainException("Cannot transfer to ready goods while stages are still pending or in progress.");

        var transferNumber = await _numberGenerator.NextAsync(DocumentType.ReadyGoodsTransfer, cancellationToken: cancellationToken);

        var transfer = new ReadyGoodsTransfer(
            transferNumber, _clock.UtcNow, order.Id, order.CustomerId, order.ItemId, request.WarehouseId,
            request.QuantityKg, request.QuantityMeter, _currentUser.UserName, request.PieceCount, request.Notes);
        _db.ReadyGoodsTransfers.Add(transfer);

var allocations = await _db.RawAllocations
    .Where(x => x.ProductionOrderId == order.Id)
    .ToListAsync(cancellationToken);

foreach (var allocation in allocations)
{
    _db.ReadyGoodsSources.Add(new ReadyGoodsSource(
        transfer.Id,
        allocation.RawMessageId,
        allocation.ItemId,
        allocation.QuantityKg,
        allocation.QuantityMeter));
}

        _db.InventoryTransactions.Add(new InventoryTransaction(
            DocumentType.ReadyGoodsTransfer, transfer.TransferNumber, transfer.Id, _clock.UtcNow,
            request.WarehouseId, order.CustomerId, order.ItemId,
            rawMessageId: null, productionOrderId: order.Id,
            quantityKg: request.QuantityKg, quantityMeter: request.QuantityMeter,
            direction: TransactionDirection.In, createdBy: _currentUser.UserName));

        await _db.SaveChangesAsync(cancellationToken);

        var customer = await _db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == order.CustomerId, cancellationToken);
        var item = await _db.Items.AsNoTracking().FirstOrDefaultAsync(i => i.Id == order.ItemId, cancellationToken);

        return new ReadyGoodsTransferDto
        {
            Id = transfer.Id, TransferNumber = transfer.TransferNumber, TransferDate = transfer.TransferDate,
            ProductionOrderId = order.Id, ProductionOrderNumber = order.OrderNumber,
            CustomerId = order.CustomerId, CustomerCode = customer?.Code ?? "",
            ItemId = order.ItemId, ItemCode = item?.Code ?? "", Color = order.Color,
            QuantityKg = transfer.QuantityKg, QuantityMeter = transfer.QuantityMeter, PieceCount = transfer.PieceCount
        };
    }
}
