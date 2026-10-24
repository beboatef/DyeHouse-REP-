using DyeHouseERP.Application.Common.Exceptions;
using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.Common.Services;
using DyeHouseERP.Application.Separates.DTOs;
using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Enums;
using DyeHouseERP.Domain.Exceptions;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.Separates.Commands;

/// <summary>Formally declares a Separate as waste rather than reprocessable material (spec section 22).</summary>
public record ScrapSeparateCommand(Guid SeparateId, string Reason) : IRequest<SeparateDto>;

public class ScrapSeparateCommandValidator : AbstractValidator<ScrapSeparateCommand>
{
    public ScrapSeparateCommandValidator()
    {
        RuleFor(x => x.Reason).NotEmpty();
    }
}

public class ScrapSeparateCommandHandler : IRequestHandler<ScrapSeparateCommand, SeparateDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IDateTime _clock;
    private readonly IPeriodCloseService _periodClose;
    private readonly IAllocationLockService _stockLock;

    public ScrapSeparateCommandHandler(
        IApplicationDbContext db, ICurrentUserService currentUser, IDateTime clock,
        IPeriodCloseService periodClose, IAllocationLockService stockLock)
    {
        _db = db; _currentUser = currentUser; _clock = clock;
        _periodClose = periodClose; _stockLock = stockLock;
    }

    public async Task<SeparateDto> Handle(ScrapSeparateCommand request, CancellationToken cancellationToken)
    {
        var separate = await _db.Separates.FirstOrDefaultAsync(s => s.Id == request.SeparateId, cancellationToken)
            ?? throw new NotFoundException("Separate", request.SeparateId);

        // Always take the period guard and the same Ready Goods lot lock used by posting
        // BEFORE deciding whether this separate has reached Ready Goods. Otherwise a concurrent
        // transfer can post after a "no transfer" read and leave scrapped material in stock.
        var postingDate = _clock.UtcNow;
        await using var periodGuard = await _periodClose.BeginPostingAsync(postingDate, cancellationToken);
        await using (await _stockLock.AcquireAsync(
            StockLockKey.ReadyLot(separate.ItemId, separate.OriginalProductionOrderId), cancellationToken))
        {
            // This read is authoritative only while holding the stock lock: Ready Goods posting
            // and scrapping for the same item/order cannot cross each other.
            var postedSeparateTransfers = await _db.ReadyGoodsTransfers.AsNoTracking()
                .Where(t => t.ProductionOrderId == separate.OriginalProductionOrderId
                         && t.StageExecutionId == separate.StageExecutionId
                         && t.Category == ReadyGoodsCategory.Separates
                         && t.Status == ReadyGoodsTransferStatus.Posted)
                .Select(t => new { t.WarehouseId })
                .Distinct()
                .ToListAsync(cancellationToken);

            if (postedSeparateTransfers.Count > 0)
            {
                // Re-read the live category balance while holding the same lot lock used by
                // ready-goods posting and delivery. Never change a stored balance or erase history.
                var warehouseIds = postedSeparateTransfers.Select(t => t.WarehouseId).Distinct().ToList();
                var ledgerRows = await _db.InventoryTransactions.AsNoTracking()
                    .Where(t => warehouseIds.Contains(t.WarehouseId)
                             && t.RawMessageId == null
                             && t.ProductionOrderId == separate.OriginalProductionOrderId
                             && t.ItemId == separate.ItemId
                             && t.ReadyGoodsCategory == ReadyGoodsCategory.Separates)
                    .Select(t => new { t.WarehouseId, t.QuantityKg, t.QuantityMeter, t.Direction })
                    .ToListAsync(cancellationToken);

                var balances = ledgerRows.GroupBy(t => t.WarehouseId)
                    .ToDictionary(g => g.Key, g => (
                        Kg: g.Sum(t => (t.QuantityKg ?? 0m) * (int)t.Direction),
                        Meter: g.Sum(t => (t.QuantityMeter ?? 0m) * (int)t.Direction)));

                var availableKg = balances.Values.Sum(b => b.Kg);
                var availableMeter = balances.Values.Sum(b => b.Meter);
                if (separate.QuantityKg is > 0 && availableKg < separate.QuantityKg.Value)
                    throw new DomainException($"Cannot scrap this separate: only {availableKg} KG of its posted separates stock remains.");
                if (separate.QuantityMeter is > 0 && availableMeter < separate.QuantityMeter.Value)
                    throw new DomainException($"Cannot scrap this separate: only {availableMeter} Meter of its posted separates stock remains.");

                var remainingKg = separate.QuantityKg ?? 0m;
                var remainingMeter = separate.QuantityMeter ?? 0m;
                foreach (var warehouseId in warehouseIds.OrderBy(id => id))
                {
                    if (remainingKg <= 0 && remainingMeter <= 0) break;
                    var balance = balances.GetValueOrDefault(warehouseId);
                    if (balance == default) continue;

                    var kg = Math.Min(remainingKg, Math.Max(0m, balance.Kg));
                    var meter = Math.Min(remainingMeter, Math.Max(0m, balance.Meter));
                    if (kg <= 0 && meter <= 0) continue;

                    _db.InventoryTransactions.Add(new InventoryTransaction(
                        DocumentType.StockAdjustment, $"SCRAP-{separate.Id:N}", separate.Id, postingDate,
                        warehouseId, separate.CustomerId, separate.ItemId,
                        rawMessageId: null, productionOrderId: separate.OriginalProductionOrderId,
                        quantityKg: kg > 0 ? kg : null, quantityMeter: meter > 0 ? meter : null,
                        direction: TransactionDirection.Out, createdBy: _currentUser.UserName,
                        readyGoodsCategory: ReadyGoodsCategory.Separates));
                    remainingKg -= kg;
                    remainingMeter -= meter;
                }

                if (remainingKg > 0 || remainingMeter > 0)
                    throw new DomainException("Cannot scrap this separate: the posted separates balance could not cover its full quantity.");
            }

            // If no transfer exists, this is still a production-floor register entry; the
            // status change is saved under the same lot lock so a transfer cannot race it.
            separate.Scrap(request.Reason, _currentUser.UserName);
            await _db.SaveChangesAsync(cancellationToken);
        }

        return new SeparateDto
        {
            Id = separate.Id, OriginalProductionOrderId = separate.OriginalProductionOrderId,
            StageExecutionId = separate.StageExecutionId, CustomerId = separate.CustomerId, ItemId = separate.ItemId,
            QuantityKg = separate.QuantityKg, QuantityMeter = separate.QuantityMeter,
            Reason = separate.Reason, Notes = separate.Notes, Status = separate.Status,
            ReprocessingProductionOrderId = separate.ReprocessingProductionOrderId
        };
    }
}
