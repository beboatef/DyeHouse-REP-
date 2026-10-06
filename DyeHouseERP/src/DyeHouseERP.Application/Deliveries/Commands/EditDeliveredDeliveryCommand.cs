using System.Text.Json;
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

// ---------------------------------------------------------------------
// Post-approval correction (spec section 30)
// ---------------------------------------------------------------------

/// <summary>One corrected line: the quantities the delivery should now read.</summary>
public record DeliveryLineQuantity(Guid LineId, decimal? QuantityKg, decimal? QuantityMeter);

/// <summary>
/// Corrects a delivery that has ALREADY been approved (Delivered).
///
/// The rule this command exists to enforce (spec section 30 + 5 + 18): the
/// original posted OUT rows are NEVER edited or deleted. Every difference is
/// posted as its own compensating movement, so the ledger - not the displayed
/// line - stays the source of truth:
///   * reduced  -> an IN of the difference returns to Ready Goods
///   * increased -> an OUT of the difference, but only if the lot actually
///                 has that much still available
///   * unchanged-> nothing is posted at all
///
/// A reason is mandatory and every changed line is written to the audit log with
/// its before and after figures, so who changed what, when and why is recoverable.
/// </summary>
public record EditDeliveredDeliveryCommand(
    Guid DeliveryId, IReadOnlyList<DeliveryLineQuantity> Lines, string Reason) : IRequest<DeliveryDto>;

public class EditDeliveredDeliveryCommandValidator : AbstractValidator<EditDeliveredDeliveryCommand>
{
    public EditDeliveredDeliveryCommandValidator()
    {
        RuleFor(x => x.DeliveryId).NotEmpty();
        // A correction without a reason is not auditable, so it is refused outright.
        RuleFor(x => x.Reason).NotEmpty().WithMessage("Correcting an approved delivery requires a reason.");
        RuleFor(x => x.Lines).NotEmpty();

        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.LineId).NotEmpty();
            line.RuleFor(l => l.QuantityKg).GreaterThanOrEqualTo(0).When(l => l.QuantityKg.HasValue);
            line.RuleFor(l => l.QuantityMeter).GreaterThanOrEqualTo(0).When(l => l.QuantityMeter.HasValue);
            // Same rule as AddLine: a line must always carry KG and/or Meter.
            line.RuleFor(l => l)
                .Must(l => l.QuantityKg.HasValue || l.QuantityMeter.HasValue)
                .WithMessage("A delivery line must keep a KG and/or Meter quantity.");
        });
    }
}

/// <summary>The net quantity change this correction makes to one ready lot.</summary>
public sealed record LotDelta(decimal Kg, decimal Meter);

public class EditDeliveredDeliveryCommandHandler
    : IRequestHandler<EditDeliveredDeliveryCommand, DeliveryDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IDateTime _clock;
    private readonly IPeriodCloseService _periodClose;
    private readonly IAllocationLockService _stockLock;

    public EditDeliveredDeliveryCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser,
        IDateTime clock, IPeriodCloseService periodClose, IAllocationLockService stockLock)
    {
        _db = db; _currentUser = currentUser; _clock = clock; _periodClose = periodClose; _stockLock = stockLock;
    }

    public async Task<DeliveryDto> Handle(EditDeliveredDeliveryCommand request, CancellationToken cancellationToken)
    {
        // The compensating rows are dated today, so a closed period blocks them if
        // today itself is closed - historical corrections stay possible (spec 43).
        await _periodClose.EnsureOpenAsync(_clock.UtcNow, cancellationToken);

        var delivery = await _db.Deliveries.FirstOrDefaultAsync(d => d.Id == request.DeliveryId, cancellationToken)
            ?? throw new NotFoundException("Delivery", request.DeliveryId);

        // State guard FIRST, before any stock work - the same ordering the
        // delivery command uses after the concurrency-hardening round.
        if (delivery.Status != DeliveryStatus.Delivered)
            throw new DomainException(
                $"Only a delivered delivery can be corrected after approval; delivery {delivery.DeliveryNumber} is {delivery.Status}.");

        var lines = await _db.DeliveryLines.Where(l => l.DeliveryId == delivery.Id).ToListAsync(cancellationToken);
        var linesById = lines.ToDictionary(l => l.Id);

        foreach (var requested in request.Lines)
        {
            if (!linesById.ContainsKey(requested.LineId))
                throw new DomainException(
                    $"Line {requested.LineId} does not belong to delivery {delivery.DeliveryNumber}. " +
                    "Adding or removing lines after approval is not allowed - only correcting existing ones.");
        }

        var readyWarehouse = await _db.Warehouses.AsNoTracking()
            .Where(w => w.Kind == WarehouseKind.ReadyGoods)
            .OrderBy(w => w.Id)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new DomainException("No Ready Goods warehouse is configured.");

        // Same lock key shape the delivery itself uses (R2), so a correction and a
        // second delivery of the same lot cannot interleave.
        var lockKeys = lines
            .Select(l => StockLockKey.ReadyLot(l.ItemId, l.ProductionOrderId))
            .Distinct()
            .ToList();

        await using (await _stockLock.AcquireManyAsync(lockKeys, cancellationToken))
        {
            // Aggregate the NET change per (order, item) BEFORE validating any
            // balance. Two lines on the same lot that each add 100 KG must be
            // checked as one 200 KG requirement, otherwise the lot could be
            // over-committed - the H3 bug class, guarded here too.
            var netByLot = new Dictionary<(Guid ItemId, Guid OrderId), LotDelta>();

            foreach (var requested in request.Lines)
            {
                var line = linesById[requested.LineId];
                var deltaKg = (requested.QuantityKg ?? 0) - (line.QuantityKg ?? 0);
                var deltaMeter = (requested.QuantityMeter ?? 0) - (line.QuantityMeter ?? 0);

                if (deltaKg == 0 && deltaMeter == 0) continue;

                var key = (line.ItemId, line.ProductionOrderId);
                netByLot.TryGetValue(key, out var current);
                netByLot[key] = new LotDelta(current?.Kg + deltaKg ?? deltaKg, current?.Meter + deltaMeter ?? deltaMeter);
            }

            foreach (var net in netByLot)
            {
                // Only an INCREASE can be refused: returning stock to Ready Goods
                // can never overdraw anything.
                if (net.Value.Kg <= 0 && net.Value.Meter <= 0) continue;

                var rows = await _db.InventoryTransactions.AsNoTracking()
                    .Where(t => t.RawMessageId == null
                             && t.ProductionOrderId == net.Key.OrderId
                             && t.ItemId == net.Key.ItemId)
                    .Select(t => new { t.QuantityKg, t.QuantityMeter, t.Direction })
                    .ToListAsync(cancellationToken);

                var availableKg = rows.Sum(r => (r.QuantityKg ?? 0) * (int)r.Direction);
                var availableMeter = rows.Sum(r => (r.QuantityMeter ?? 0) * (int)r.Direction);

                if (net.Value.Kg > availableKg)
                    throw new NegativeStockException(availableKg, net.Value.Kg);
                if (net.Value.Meter > availableMeter)
                    throw new NegativeStockException(availableMeter, net.Value.Meter);
            }

            // Apply the corrected figures and record each change in the audit log
            // BEFORE posting, so the log holds the real previous values.
            foreach (var requested in request.Lines)
            {
                var line = linesById[requested.LineId];

                var deltaKg = (requested.QuantityKg ?? 0) - (line.QuantityKg ?? 0);
                var deltaMeter = (requested.QuantityMeter ?? 0) - (line.QuantityMeter ?? 0);
                if (deltaKg == 0 && deltaMeter == 0) continue;

                var before = JsonSerializer.Serialize(new { line.QuantityKg, line.QuantityMeter });

                line.AdjustQuantitiesAfterApproval(requested.QuantityKg, requested.QuantityMeter);

                var after = JsonSerializer.Serialize(new
                {
                    line.QuantityKg,
                    line.QuantityMeter,
                    DifferenceKg = deltaKg,
                    DifferenceMeter = deltaMeter
                });

                _db.AuditLogEntries.Add(new AuditLogEntry(
                    _clock.UtcNow, _currentUser.UserName, "Update", "DeliveryLine",
                    line.Id.ToString(), before, after, ipAddress: null, reason: request.Reason));
            }

            // One compensating movement per affected lot, not one per line: the
            // ledger only ever shows the net effect of this correction.
            foreach (var net in netByLot)
            {
                if (net.Value.Kg == 0 && net.Value.Meter == 0) continue;

                // A negative net means the delivered quantity went DOWN, so the
                // difference comes back IN. A positive net is an increase and
                // leaves as OUT. Both are new rows - the originals stay as posted.
                var direction = net.Value.Kg < 0 || net.Value.Meter < 0
                    ? TransactionDirection.In
                    : TransactionDirection.Out;

                _db.InventoryTransactions.Add(new InventoryTransaction(
                    DocumentType.Delivery, delivery.DeliveryNumber, delivery.Id, _clock.UtcNow,
                    readyWarehouse.Id, delivery.CustomerId, net.Key.ItemId,
                    rawMessageId: null, productionOrderId: net.Key.OrderId,
                    quantityKg: Math.Abs(net.Value.Kg), quantityMeter: Math.Abs(net.Value.Meter),
                    direction: direction, createdBy: _currentUser.UserName));
            }

            delivery.EditAfterApproval(request.Reason, _currentUser.UserName);

            await _db.SaveChangesAsync(cancellationToken);
        }

        return await GetDeliveriesQueryHandler.LoadDtoAsync(_db, delivery.Id, cancellationToken);
    }
}