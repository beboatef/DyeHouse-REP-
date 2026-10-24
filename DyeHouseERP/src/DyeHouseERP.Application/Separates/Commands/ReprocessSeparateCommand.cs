using DyeHouseERP.Application.Common.Exceptions;
using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.Common.Services;
using DyeHouseERP.Application.ProductionOrders.DTOs;
using DyeHouseERP.Application.ProductionOrders.Queries;
using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Enums;
using DyeHouseERP.Domain.Exceptions;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.Separates.Commands;

/// <summary>
/// Creates one new production order from one or more posted Ready Goods separates.
/// Each separate is consumed in full, remains linked to the new order, and is deducted
/// from its own original-order Ready Goods ledger dimension in the same SaveChanges call.
/// </summary>
public record ReprocessSeparateCommand(
    Guid SeparateId, DateTime OrderDate, string? Notes, Guid? StartingStageId = null,
    IReadOnlyCollection<Guid>? AdditionalSeparateIds = null) : IRequest<ProductionOrderDto>;

public class ReprocessSeparateCommandValidator : AbstractValidator<ReprocessSeparateCommand>
{
    public ReprocessSeparateCommandValidator()
    {
        RuleFor(x => x.SeparateId).NotEmpty();
        RuleFor(x => x.OrderDate).NotEmpty();
        RuleFor(x => x.AdditionalSeparateIds)
            .Must(ids => ids is null || ids.All(id => id != Guid.Empty))
            .WithMessage("All selected separate IDs are required.");
    }
}

public class ReprocessSeparateCommandHandler : IRequestHandler<ReprocessSeparateCommand, ProductionOrderDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IDocumentNumberGenerator _numberGenerator;
    private readonly IAllocationLockService _allocationLock;

    public ReprocessSeparateCommandHandler(
        IApplicationDbContext db, ICurrentUserService currentUser,
        IDocumentNumberGenerator numberGenerator, IAllocationLockService allocationLock)
    {
        _db = db;
        _currentUser = currentUser;
        _numberGenerator = numberGenerator;
        _allocationLock = allocationLock;
    }

    public async Task<ProductionOrderDto> Handle(ReprocessSeparateCommand request, CancellationToken cancellationToken)
    {
        var ids = new[] { request.SeparateId }
            .Concat(request.AdditionalSeparateIds ?? Array.Empty<Guid>())
            .Distinct()
            .ToArray();
        if (ids.Length != 1 + (request.AdditionalSeparateIds?.Count ?? 0))
            throw new DomainException("The same separate cannot be selected more than once.");
        if (ids.Length == 0 || ids.Any(id => id == Guid.Empty))
            throw new DomainException("Select at least one valid separate.");

        // IDs and their immutable stock dimensions are read first only to determine locks.
        var initial = await _db.Separates.AsNoTracking()
            .Where(s => ids.Contains(s.Id))
            .Select(s => new { s.Id, s.OriginalProductionOrderId, s.ItemId })
            .ToListAsync(cancellationToken);
        if (initial.Count != ids.Length)
            throw new NotFoundException("Separate", ids.First(id => initial.All(s => s.Id != id)));

        await using var stockLock = await _allocationLock.AcquireManyAsync(
            initial.Select(s => StockLockKey.ReadyLot(s.ItemId, s.OriginalProductionOrderId)),
            cancellationToken);

        // Re-read tracked rows only after acquiring the same lock used by ready-goods postings.
        var separates = await _db.Separates
            .Where(s => ids.Contains(s.Id))
            .OrderBy(s => s.CreatedAtUtc).ToListAsync(cancellationToken);
        if (separates.Count != ids.Length)
            throw new DomainException("One or more selected separates no longer exist.");
        if (separates.Any(s => s.Status != SeparateStatus.PendingReprocessing))
            throw new DomainException("Only separates waiting for reprocessing can be selected.");

        var customerId = separates[0].CustomerId;
        var itemId = separates[0].ItemId;
        if (separates.Any(s => s.CustomerId != customerId || s.ItemId != itemId))
            throw new DomainException("One reprocessing order can contain separates for only one customer and one item.");

        var totalKg = separates.Any(s => s.QuantityKg.HasValue)
            ? separates.Sum(s => s.QuantityKg ?? 0m) : (decimal?)null;
        var totalMeter = separates.Any(s => s.QuantityMeter.HasValue)
            ? separates.Sum(s => s.QuantityMeter ?? 0m) : (decimal?)null;
        await ItemUnitGuard.EnsureValidQuantitiesAsync(
            _db, itemId, totalKg, totalMeter, cancellationToken, "on this reprocessing order");

        // Load the authoritative ledger balance by original order and warehouse. No editable
        // balance is consulted; only posted Separates-category movements are considered.
        var originalOrderIds = separates.Select(s => s.OriginalProductionOrderId).Distinct().ToArray();
        var ledgerRows = await _db.InventoryTransactions.AsNoTracking()
            .Where(t => t.RawMessageId == null
                && t.ProductionOrderId.HasValue
                && originalOrderIds.Contains(t.ProductionOrderId.Value)
                && t.ItemId == itemId
                && t.CustomerId == customerId
                && t.ReadyGoodsCategory == ReadyGoodsCategory.Separates)
            .Select(t => new { t.ProductionOrderId, t.WarehouseId, t.QuantityKg, t.QuantityMeter, t.Direction })
            .ToListAsync(cancellationToken);

        var balances = ledgerRows
            .GroupBy(t => (OrderId: t.ProductionOrderId!.Value, t.WarehouseId))
            .ToDictionary(g => g.Key, g => new StockBalance(
                g.Sum(t => (t.QuantityKg ?? 0m) * (int)t.Direction),
                g.Sum(t => (t.QuantityMeter ?? 0m) * (int)t.Direction)));

        var orderNumber = await _numberGenerator.NextAsync(
            DocumentType.ProductionOrder, cancellationToken: cancellationToken);
        var newOrder = new ProductionOrder(
            orderNumber, customerId, itemId, request.OrderDate, _currentUser.UserName,
            requestedQuantityKg: totalKg, requestedQuantityMeter: totalMeter,
            notes: ComposeNotes(request.Notes, separates),
            reprocessingOfProductionOrderId: separates[0].OriginalProductionOrderId);

        var activeStages = await _db.ProductionStageDefinitions.AsNoTracking()
            .Where(s => s.IsActive).OrderBy(s => s.Sequence).ToListAsync(cancellationToken);
        newOrder.BuildStageRoute(activeStages, _currentUser.UserName, request.StartingStageId);

        // Plan every ledger OUT before mutating entities. KG and Meter are independent facts,
        // and can be sourced from different warehouse rows without conversion.
        var deductions = new List<InventoryTransaction>();
        foreach (var separate in separates)
        {
            var orderBalances = balances
                .Where(pair => pair.Key.OrderId == separate.OriginalProductionOrderId)
                .OrderBy(pair => pair.Key.WarehouseId)
                .ToList();
            if (orderBalances.Count == 0)
                throw new DomainException($"Separate {separate.Id} has no posted Ready Goods stock. Reprocessing is allowed only after its stock has been posted.");

            Allocate(separate.QuantityKg, isKg: true, separate, orderBalances, deductions, newOrder);
            Allocate(separate.QuantityMeter, isKg: false, separate, orderBalances, deductions, newOrder);
        }

        _db.ProductionOrders.Add(newOrder);
        foreach (var separate in separates)
            separate.StartReprocessing(newOrder.Id);
        _db.InventoryTransactions.AddRange(deductions);

        // One EF SaveChanges transaction commits the new order, every separate status change,
        // and all corresponding OUT movements together. RowVersion + stock locks stop reuse.
        await _db.SaveChangesAsync(cancellationToken);
        return await GetProductionOrderByIdQueryHandler.LoadDtoAsync(_db, newOrder.Id, cancellationToken);
    }

    private void Allocate(
        decimal? requested, bool isKg, Separate separate,
        List<KeyValuePair<(Guid OrderId, Guid WarehouseId), StockBalance>> balances,
        List<InventoryTransaction> deductions, ProductionOrder newOrder)
    {
        var remaining = requested ?? 0m;
        if (remaining <= 0m) return;

        foreach (var entry in balances)
        {
            var available = isKg ? entry.Value.Kg : entry.Value.Meter;
            if (available <= 0m) continue;
            var quantity = Math.Min(remaining, available);
            if (quantity <= 0m) continue;

            if (isKg) entry.Value.Kg -= quantity;
            else entry.Value.Meter -= quantity;

            deductions.Add(new InventoryTransaction(
                DocumentType.ProductionOrder, newOrder.OrderNumber, newOrder.Id,
                DateTime.UtcNow, entry.Key.WarehouseId, separate.CustomerId, separate.ItemId,
                rawMessageId: null, productionOrderId: separate.OriginalProductionOrderId,
                quantityKg: isKg ? quantity : null, quantityMeter: isKg ? null : quantity,
                direction: TransactionDirection.Out, createdBy: _currentUser.UserName,
                readyGoodsCategory: ReadyGoodsCategory.Separates));
            remaining -= quantity;
            if (remaining <= 0m) break;
        }

        if (remaining > 0m)
        {
            var unit = isKg ? "KG" : "Meter";
            throw new DomainException(
                $"Insufficient posted Ready Goods {unit} balance for separate {separate.Id}. " +
                "Refresh the register and select only available separates.");
        }
    }

    private static string ComposeNotes(string? notes, IReadOnlyCollection<Separate> separates)
    {
        var ids = string.Join(", ", separates.Select(s => s.Id.ToString("D")));
        var trace = $"Reprocessing batch. Source separate IDs: {ids}.";
        return string.IsNullOrWhiteSpace(notes) ? trace : $"{notes.Trim()}\n{trace}";
    }

    private sealed class StockBalance(decimal kg, decimal meter)
    {
        public decimal Kg { get; set; } = kg;
        public decimal Meter { get; set; } = meter;
    }
}
