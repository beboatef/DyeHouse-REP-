using DyeHouseERP.Application.Common.Exceptions;
using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.Deliveries.DTOs;
using DyeHouseERP.Application.Deliveries.Queries;
using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Enums;
using DyeHouseERP.Domain.Exceptions;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.Deliveries.Commands;

// -------------------- Prepare --------------------
public record MarkDeliveryPreparedCommand(Guid DeliveryId) : IRequest<DeliveryDto>;

public class MarkDeliveryPreparedCommandHandler : IRequestHandler<MarkDeliveryPreparedCommand, DeliveryDto>
{
    private readonly IApplicationDbContext _db;
    public MarkDeliveryPreparedCommandHandler(IApplicationDbContext db) => _db = db;

    public async Task<DeliveryDto> Handle(MarkDeliveryPreparedCommand request, CancellationToken cancellationToken)
    {
        var delivery = await _db.Deliveries.Include(d => d.Lines).FirstOrDefaultAsync(d => d.Id == request.DeliveryId, cancellationToken)
            ?? throw new NotFoundException("Delivery", request.DeliveryId);
        var lines = await _db.DeliveryLines.Where(l => l.DeliveryId == delivery.Id).ToListAsync(cancellationToken);
        if (lines.Count == 0) throw new DomainException("Cannot prepare a delivery with no lines.");

        delivery.MarkPrepared();
        await _db.SaveChangesAsync(cancellationToken);
        return await GetDeliveriesQueryHandler.LoadDtoAsync(_db, delivery.Id, cancellationToken);
    }
}

// -------------------- Deliver --------------------
public record MarkDeliveryDeliveredCommand(Guid DeliveryId) : IRequest<DeliveryDto>;

/// <summary>
/// Deducts ready stock (spec section 31: "validate ready balance, prevent
/// over-delivery... deduct from ready stock, lock the document"). Each line
/// posts an OUT InventoryTransaction against the ready warehouse dimension
/// (RawMessageId = null, ProductionOrderId set) - the same ledger dimension
/// ReadyGoodsTransfer posts INs into.
/// </summary>
public class MarkDeliveryDeliveredCommandHandler : IRequestHandler<MarkDeliveryDeliveredCommand, DeliveryDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IDateTime _clock;

    public MarkDeliveryDeliveredCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser, IDateTime clock)
    {
        _db = db; _currentUser = currentUser; _clock = clock;
    }

    public async Task<DeliveryDto> Handle(MarkDeliveryDeliveredCommand request, CancellationToken cancellationToken)
    {
        var delivery = await _db.Deliveries.FirstOrDefaultAsync(d => d.Id == request.DeliveryId, cancellationToken)
            ?? throw new NotFoundException("Delivery", request.DeliveryId);
        var lines = await _db.DeliveryLines.Where(l => l.DeliveryId == delivery.Id).ToListAsync(cancellationToken);

        // Validate ready balance per line before posting anything.
        var readyWarehouse = await _db.Warehouses.AsNoTracking()
            .FirstOrDefaultAsync(w => w.Kind == Domain.Entities.WarehouseKind.ReadyGoods, cancellationToken)
            ?? throw new DomainException("No Ready Goods warehouse is configured.");

        foreach (var line in lines)
        {
            var rows = await _db.InventoryTransactions.AsNoTracking()
                .Where(t => t.RawMessageId == null && t.ProductionOrderId == line.ProductionOrderId && t.ItemId == line.ItemId)
                .Select(t => new { t.QuantityKg, t.QuantityMeter, t.Direction })
                .ToListAsync(cancellationToken);

            var availableKg = rows.Sum(r => (r.QuantityKg ?? 0) * (int)r.Direction);
            var availableMeter = rows.Sum(r => (r.QuantityMeter ?? 0) * (int)r.Direction);

            if (line.QuantityKg is > 0 && line.QuantityKg > availableKg)
                throw new NegativeStockException(availableKg, line.QuantityKg.Value);
            if (line.QuantityMeter is > 0 && line.QuantityMeter > availableMeter)
                throw new NegativeStockException(availableMeter, line.QuantityMeter.Value);
        }

        delivery.MarkDelivered();

        foreach (var line in lines)
        {
            _db.InventoryTransactions.Add(new InventoryTransaction(
                DocumentType.Delivery, delivery.DeliveryNumber, delivery.Id, _clock.UtcNow,
                readyWarehouse.Id, delivery.CustomerId, line.ItemId,
                rawMessageId: null, productionOrderId: line.ProductionOrderId,
                quantityKg: line.QuantityKg, quantityMeter: line.QuantityMeter,
                direction: TransactionDirection.Out, createdBy: _currentUser.UserName));
        }

        await _db.SaveChangesAsync(cancellationToken);
        return await GetDeliveriesQueryHandler.LoadDtoAsync(_db, delivery.Id, cancellationToken);
    }
}

// -------------------- Cancel --------------------
public record CancelDeliveryCommand(Guid DeliveryId, string Reason) : IRequest<DeliveryDto>;

public class CancelDeliveryCommandValidator : AbstractValidator<CancelDeliveryCommand>
{
    public CancelDeliveryCommandValidator() => RuleFor(x => x.Reason).NotEmpty();
}

/// <summary>Cancelling a Delivered delivery posts a full reversal (IN) of every OUT row it created - never edits or deletes the original rows (spec section 31 + 18).</summary>
public class CancelDeliveryCommandHandler : IRequestHandler<CancelDeliveryCommand, DeliveryDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IDateTime _clock;

    public CancelDeliveryCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser, IDateTime clock)
    {
        _db = db; _currentUser = currentUser; _clock = clock;
    }

    public async Task<DeliveryDto> Handle(CancelDeliveryCommand request, CancellationToken cancellationToken)
    {
        var delivery = await _db.Deliveries.FirstOrDefaultAsync(d => d.Id == request.DeliveryId, cancellationToken)
            ?? throw new NotFoundException("Delivery", request.DeliveryId);

        var wasDelivered = delivery.Status == DeliveryStatus.Delivered;

        if (wasDelivered)
        {
            var originalRows = await _db.InventoryTransactions
                .Where(t => t.SourceDocumentId == delivery.Id && t.SourceDocumentType == DocumentType.Delivery)
                .ToListAsync(cancellationToken);

            foreach (var row in originalRows)
            {
                _db.InventoryTransactions.Add(new InventoryTransaction(
                    DocumentType.Delivery, delivery.DeliveryNumber, delivery.Id, _clock.UtcNow,
                    row.WarehouseId, row.CustomerId, row.ItemId, row.RawMessageId, row.ProductionOrderId,
                    row.QuantityKg, row.QuantityMeter,
                    direction: row.Direction == TransactionDirection.Out ? TransactionDirection.In : TransactionDirection.Out,
                    createdBy: _currentUser.UserName, reversesTransactionId: row.Id));
            }
        }

        delivery.Cancel(request.Reason, _currentUser.UserName);

        await _db.SaveChangesAsync(cancellationToken);
        return await GetDeliveriesQueryHandler.LoadDtoAsync(_db, delivery.Id, cancellationToken);
    }
}
