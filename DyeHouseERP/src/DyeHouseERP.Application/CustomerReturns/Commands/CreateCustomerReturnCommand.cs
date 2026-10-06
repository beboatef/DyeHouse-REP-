using DyeHouseERP.Application.Common.Exceptions;
using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.CustomerReturns.DTOs;
using DyeHouseERP.Application.CustomerReturns.Queries;
using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Enums;
using DyeHouseERP.Domain.Exceptions;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.CustomerReturns.Commands;

/// <summary>
/// One returned line. <see cref="RawMessageId"/> is the raw lot the quantity lands
/// in and may be omitted ONLY when a Job Order is supplied - in that case the lot
/// is derived from that Job Order's own raw allocations. When the Job Order is
/// unknown the lot is mandatory, because the ledger row has to name a lot.
/// </summary>
public record CustomerReturnLineInput(
    Guid? RawMessageId,
    Guid ItemId,
    decimal? QuantityKg,
    decimal? QuantityMeter,
    Guid? ProductionOrderId = null,
    Guid? FormationGroupId = null,
    string? Notes = null);

/// <summary>
/// Records processed goods coming back from a customer into the RAW MATERIAL
/// warehouse (spec sections 32-33). Posts IN ledger rows on the chosen lots, which
/// is what makes the material available for a later Job Order - the displayed
/// balance is never edited directly.
/// </summary>
public record CreateCustomerReturnCommand(
    Guid CustomerId,
    DateTime ReturnDate,
    string? Reason,
    string? Notes,
    List<CustomerReturnLineInput> Lines) : IRequest<CustomerReturnDto>;

public class CreateCustomerReturnCommandValidator : AbstractValidator<CreateCustomerReturnCommand>
{
    public CreateCustomerReturnCommandValidator()
    {
        RuleFor(x => x.CustomerId).NotEmpty();
        RuleFor(x => x.Reason).MaximumLength(500);
        RuleFor(x => x.Notes).MaximumLength(2000);

        RuleFor(x => x.Lines).NotEmpty()
            .WithMessage("A customer return must contain at least one line.");

        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.ItemId).NotEmpty();

            // The reason is deliberately NOT required - spec section 32 makes it optional free text.
            line.RuleFor(l => l)
                .Must(l => l.QuantityKg is > 0 || l.QuantityMeter is > 0)
                .WithMessage("A return line must specify a KG and/or Meter quantity greater than zero.");

            line.RuleFor(l => l)
                .Must(l => l.RawMessageId.HasValue || l.ProductionOrderId.HasValue)
                .WithMessage("Specify the raw material lot explicitly when the Job Order is unknown.");
        });
    }
}

public class CreateCustomerReturnCommandHandler : IRequestHandler<CreateCustomerReturnCommand, CustomerReturnDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IDocumentNumberGenerator _numberGenerator;
    private readonly IDateTime _clock;
    private readonly IPeriodCloseService _periodClose;
    private readonly IAllocationLockService _stockLock;

    public CreateCustomerReturnCommandHandler(
        IApplicationDbContext db, ICurrentUserService currentUser, IDocumentNumberGenerator numberGenerator,
        IDateTime clock, IPeriodCloseService periodClose, IAllocationLockService stockLock)
    {
        _db = db;
        _currentUser = currentUser;
        _numberGenerator = numberGenerator;
        _clock = clock;
        _periodClose = periodClose;
        _stockLock = stockLock;
    }

    public async Task<CustomerReturnDto> Handle(CreateCustomerReturnCommand request, CancellationToken cancellationToken)
    {
        // A customer return posts stock, so it must fall outside any closed period.
        await _periodClose.EnsureOpenAsync(_clock.UtcNow, cancellationToken);

        var customer = await _db.Customers.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == request.CustomerId, cancellationToken)
            ?? throw new NotFoundException("Customer", request.CustomerId);

        // Resolve every line's target lot BEFORE taking locks, so the lock set is
        // known up front and validation can fail without holding anything.
        var resolved = new List<(CustomerReturnLineInput Input, Guid LotId, ProductionOrder? Order)>();
        foreach (var line in request.Lines)
        {
            var order = line.ProductionOrderId.HasValue
                ? await _db.ProductionOrders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == line.ProductionOrderId.Value, cancellationToken)
                    ?? throw new NotFoundException("ProductionOrder", line.ProductionOrderId.Value)
                : null;

            if (order is not null && order.CustomerId != request.CustomerId)
                throw new DomainException(
                    $"Job Order '{order.OrderNumber}' belongs to a different customer than this return.");

            var lotId = line.RawMessageId ?? await ResolveLotFromJobOrderAsync(line, order!, cancellationToken);
            resolved.Add((line, lotId, order));
        }

        var lotIds = resolved.Select(r => r.LotId).Distinct().ToList();
        var messages = await _db.RawMessages
            .Include(m => m.Lines)
            .Where(m => lotIds.Contains(m.Id))
            .ToListAsync(cancellationToken);

        var warehouses = await _db.Warehouses.AsNoTracking()
            .Where(w => messages.Select(m => m.WarehouseId).Contains(w.Id))
            .ToDictionaryAsync(w => w.Id, cancellationToken);

        foreach (var (input, lotId, _) in resolved)
        {
            var message = messages.FirstOrDefault(m => m.Id == lotId)
                ?? throw new NotFoundException("RawMessage", lotId);

            // Spec section 32: returns land in the RAW MATERIAL warehouse, never Ready Goods.
            if (!warehouses.TryGetValue(message.WarehouseId, out var warehouse) || warehouse.Kind != WarehouseKind.RawMaterial)
                throw new DomainException(
                    $"Lot '{message.MessageNumber}' is not in a raw material warehouse. Customer returns are received into raw material stock.");

            // Customer-owned stock: a return must go back onto the returning customer's own lot.
            if (message.CustomerId != request.CustomerId)
                throw new DomainException(
                    $"Lot '{message.MessageNumber}' belongs to another customer and cannot receive this return.");
        }

        var returnNumber = await _numberGenerator.NextAsync(DocumentType.CustomerReturn, cancellationToken: cancellationToken);
        var customerReturn = new CustomerReturn(
            returnNumber, request.ReturnDate, request.CustomerId, _currentUser.UserName, request.Reason, request.Notes);

        // Lock every (warehouse, item, customer, lot) dimension being written, so a
        // concurrent allocation cannot read a stale balance while this posts.
        var lockKeys = resolved
            .Select(r => StockLockKey.RawLot(
                messages.First(m => m.Id == r.LotId).WarehouseId, r.Input.ItemId, request.CustomerId, r.LotId))
            .ToList();

        await using (await _stockLock.AcquireManyAsync(lockKeys, cancellationToken))
        {
            foreach (var (input, lotId, order) in resolved)
            {
                var message = messages.First(m => m.Id == lotId);

                var line = customerReturn.AddLine(
                    lotId, input.ItemId, input.QuantityKg, input.QuantityMeter,
                    input.ProductionOrderId, input.FormationGroupId, input.Notes);

                _db.CustomerReturnLines.Add(line);

                // The IN row is the return: it restores the lot's availability.
                _db.InventoryTransactions.Add(new InventoryTransaction(
                    DocumentType.CustomerReturn, customerReturn.ReturnNumber, customerReturn.Id,
                    _clock.UtcNow, message.WarehouseId, request.CustomerId, input.ItemId,
                    rawMessageId: lotId, productionOrderId: input.ProductionOrderId,
                    quantityKg: input.QuantityKg, quantityMeter: input.QuantityMeter,
                    direction: TransactionDirection.In, createdBy: _currentUser.UserName));
            }

            customerReturn.Post();
            _db.CustomerReturns.Add(customerReturn);

            await _db.SaveChangesAsync(cancellationToken);

            // A lot that had been fully consumed reads as Depleted, which would keep
            // the returned material unallocatable. Recompute each affected lot's status
            // from the ledger, so the returned stock is genuinely usable again.
            foreach (var lotId in lotIds)
            {
                var message = messages.First(m => m.Id == lotId);
                var (remainingKg, remainingMeter, hasAnyOut) =
                    await ReadLotTotalsAsync(lotId, message.WarehouseId, cancellationToken);

                var hasRemaining = remainingKg > 0 || remainingMeter > 0;
                message.RecalculateStatus(hasRemaining, hasAnyOut);
            }

            await _db.SaveChangesAsync(cancellationToken);
        }

        return await GetCustomerReturnsQueryHandler.LoadDtoAsync(_db, customerReturn.Id, cancellationToken);
    }

    /// <summary>
    /// Derives the target lot from a known Job Order's own raw allocations. If the
    /// order consumed from several lots the choice is genuinely ambiguous, so it is
    /// refused rather than guessed - the user must name the lot.
    /// </summary>
    private async Task<Guid> ResolveLotFromJobOrderAsync(
        CustomerReturnLineInput line, ProductionOrder order, CancellationToken cancellationToken)
    {
        var lotIds = await _db.RawAllocations.AsNoTracking()
            .Where(a => a.ProductionOrderId == order.Id && a.ItemId == line.ItemId)
            .Select(a => a.RawMessageId)
            .Distinct()
            .ToListAsync(cancellationToken);

        return lotIds.Count switch
        {
            1 => lotIds[0],
            0 => throw new DomainException(
                $"Job Order '{order.OrderNumber}' has no raw material allocation for this item, so the raw lot cannot be derived. Specify the raw material lot explicitly."),
            _ => throw new DomainException(
                $"Job Order '{order.OrderNumber}' consumed this item from {lotIds.Count} lots, so the target lot is ambiguous. Specify the raw material lot explicitly.")
        };
    }

    /// <summary>
    /// Reads a lot's totals the same way the inventory ledger service does: pull the
    /// rows, then reduce in memory. Keeps enum handling off the SQL side.
    /// </summary>
    private async Task<(decimal Kg, decimal Meter, bool HasAnyOut)> ReadLotTotalsAsync(
        Guid lotId, Guid warehouseId, CancellationToken cancellationToken)
    {
        var rows = await _db.InventoryTransactions.AsNoTracking()
            .Where(t => t.RawMessageId == lotId && t.WarehouseId == warehouseId)
            .Select(t => new { t.QuantityKg, t.QuantityMeter, t.Direction })
            .ToListAsync(cancellationToken);

        decimal kg = 0, meter = 0;
        var hasAnyOut = false;
        foreach (var row in rows)
        {
            var sign = (int)row.Direction;
            kg += (row.QuantityKg ?? 0) * sign;
            meter += (row.QuantityMeter ?? 0) * sign;
            if (row.Direction == TransactionDirection.Out) hasAnyOut = true;
        }

        return (kg, meter, hasAnyOut);
    }
}
