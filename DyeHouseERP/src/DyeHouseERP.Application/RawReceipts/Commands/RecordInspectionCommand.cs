using DyeHouseERP.Application.Common.Exceptions;
using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.RawReceipts.DTOs;
using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Enums;
using FluentValidation;
using MediatR;

namespace DyeHouseERP.Application.RawReceipts.Commands;

/// <summary>
/// Records receiving inspection information (spec section 9). This is NOT an
/// approval gate - the receipt already posted and is already allocatable. It only
/// records the result, the inspector, notes, and any rejected quantity per line.
/// A rejected quantity is corrected on the ledger with an equal-and-opposite OUT
/// row (never a balance edit), so rejected material stays traceable to this
/// customer's receipt while no longer being available for allocation.
/// </summary>
public record RecordInspectionCommand(
    Guid RawMessageId,
    InspectionStatus Result,
    string? Notes,
    List<RawMessageLineRejectionInput>? Rejections = null) : IRequest<Unit>;

public class RecordInspectionCommandValidator : AbstractValidator<RecordInspectionCommand>
{
    public RecordInspectionCommandValidator()
    {
        RuleFor(x => x.RawMessageId).NotEmpty();
        RuleFor(x => x.Result).IsInEnum().NotEqual(InspectionStatus.PendingInspection);
        RuleFor(x => x.Notes).MaximumLength(1000);

        // A pure "Accepted" result cannot carry rejected quantities - recording a
        // rejection means the outcome is at best a partial acceptance.
        RuleFor(x => x.Result)
            .Must(result => result != InspectionStatus.Accepted)
            .When(x => x.Rejections is { Count: > 0 })
            .WithMessage("Rejected quantities require a Rejected or Accepted With Notes inspection result.");

        RuleForEach(x => x.Rejections).ChildRules(rejection =>
        {
            rejection.RuleFor(r => r.LineId).NotEmpty();
            rejection.RuleFor(r => r)
                .Must(r => r.RejectedQuantityKg is > 0 || r.RejectedQuantityMeter is > 0)
                .WithMessage("A rejected quantity must be greater than zero in KG and/or Meter.");
            rejection.RuleFor(r => r.RejectedQuantityKg).GreaterThanOrEqualTo(0).When(r => r.RejectedQuantityKg.HasValue);
            rejection.RuleFor(r => r.RejectedQuantityMeter).GreaterThanOrEqualTo(0).When(r => r.RejectedQuantityMeter.HasValue);
        });
    }
}

public class RecordInspectionCommandHandler : IRequestHandler<RecordInspectionCommand, Unit>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;    private readonly IDateTime _clock;
    private readonly IPeriodCloseService _periodClose;
    private readonly IAllocationLockService _stockLock;

    public RecordInspectionCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser, IDateTime clock,
        IPeriodCloseService periodClose, IAllocationLockService stockLock)
    {
        _db = db; _currentUser = currentUser; _clock = clock; _periodClose = periodClose; _stockLock = stockLock;
    }

    public async Task<Unit> Handle(RecordInspectionCommand request, CancellationToken cancellationToken)
    {
        // Spec section 43: rejecting part of a receipt posts a correcting OUT row,
        // so the inspection date must fall outside any closed period.
        await _periodClose.EnsureOpenAsync(_clock.UtcNow, cancellationToken);

        var message = await _db.RawMessages.FindAsync(new object[] { request.RawMessageId }, cancellationToken)
            ?? throw new NotFoundException("RawMessage", request.RawMessageId);

        message.RecordInspection(request.Result, _currentUser.UserName, request.Notes, _clock.UtcNow);

        if (request.Rejections is { Count: > 0 })
        {
            // H6: a rejected quantity posts a correcting OUT row on the same raw
            // lot, so it takes the same lot locks the allocation/adjustment paths
            // use - a rejection and a concurrent allocation can no longer both
            // act on a stale view of the same balance.
            var lockKeys = request.Rejections
                .Select(r => message.Lines.FirstOrDefault(l => l.Id == r.LineId))
                .Where(l => l is not null)
                .Select(l => StockLockKey.RawLot(message.WarehouseId, l!.ItemId, message.CustomerId, message.Id))
                .Distinct()
                .ToList();

            await using (await _stockLock.AcquireManyAsync(lockKeys, cancellationToken))
            {
            foreach (var rejection in request.Rejections)
            {
                var (deltaKg, deltaMeter) = message.RecordLineRejection(
                    rejection.LineId, rejection.RejectedQuantityKg, rejection.RejectedQuantityMeter);

                if (deltaKg is null && deltaMeter is null)
                    continue;

                var line = message.Lines.First(l => l.Id == rejection.LineId);

                // The receipt posted the full received quantity as an IN row. The
                // rejected part is corrected with one OUT row referencing the same
                // message, so the message/customer balance (a live signed sum) drops
                // by exactly the rejected amount and the rejection is auditable.
                _db.InventoryTransactions.Add(new InventoryTransaction(
                    DocumentType.RawReceiptMessage, message.MessageNumber, message.Id,
                    _clock.UtcNow, message.WarehouseId, message.CustomerId, line.ItemId,
                    rawMessageId: message.Id, productionOrderId: null,
                    quantityKg: deltaKg, quantityMeter: deltaMeter,
                    direction: TransactionDirection.Out,                    createdBy: _currentUser.UserName));
            }
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}
