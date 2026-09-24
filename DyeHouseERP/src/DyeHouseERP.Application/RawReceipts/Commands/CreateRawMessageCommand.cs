using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.RawReceipts.DTOs;
using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Enums;
using FluentValidation;
using MediatR;

namespace DyeHouseERP.Application.RawReceipts.Commands;

public record CreateRawMessageCommand(
    DateTime ReceiptDate,
    Guid CustomerId,
    Guid WarehouseId,
    string? Notes,
    List<RawMessageLineInput> Lines) : IRequest<RawMessageDto>;

public class CreateRawMessageCommandValidator : AbstractValidator<CreateRawMessageCommand>
{
    public CreateRawMessageCommandValidator()
    {
        RuleFor(x => x.CustomerId).NotEmpty();
        RuleFor(x => x.WarehouseId).NotEmpty();
        RuleFor(x => x.Lines).NotEmpty().WithMessage("A raw receipt message must contain at least one line.");

        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.ItemId).NotEmpty();
            line.RuleFor(l => l)
                .Must(l => l.QuantityKg is > 0 || l.QuantityMeter is > 0)
                .WithMessage("Each line needs a KG quantity and/or a Meter quantity greater than zero.");
        });
    }
}

public class CreateRawMessageCommandHandler : IRequestHandler<CreateRawMessageCommand, RawMessageDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IDocumentNumberGenerator _numberGenerator;
    private readonly IDateTime _clock;

    public CreateRawMessageCommandHandler(
        IApplicationDbContext db,
        ICurrentUserService currentUser,
        IDocumentNumberGenerator numberGenerator,
        IDateTime clock)
    {
        _db = db;
        _currentUser = currentUser;
        _numberGenerator = numberGenerator;
        _clock = clock;
    }

    public async Task<RawMessageDto> Handle(CreateRawMessageCommand request, CancellationToken cancellationToken)
    {
        // Section 6: the message number is ALWAYS system-generated via the
        // concurrency-safe sequence engine - never typed by the user.
        var messageNumber = await _numberGenerator.NextAsync(DocumentType.RawReceiptMessage, cancellationToken: cancellationToken);

        var message = new RawMessage(
            messageNumber, request.ReceiptDate, request.CustomerId, request.WarehouseId,
            _currentUser.UserName, _currentUser.UserName, request.Notes);

        foreach (var line in request.Lines)
            message.AddLine(line.ItemId, line.QuantityKg, line.QuantityMeter, line.PieceCount, line.Notes);

        _db.RawMessages.Add(message);

        // Post the corresponding inventory ledger rows (spec section 18) -
        // one IN transaction per line, per unit that was actually recorded.
        foreach (var line in message.Lines)
        {
            _db.InventoryTransactions.Add(new InventoryTransaction(
                DocumentType.RawReceiptMessage, message.MessageNumber, message.Id,
                request.ReceiptDate, request.WarehouseId, request.CustomerId, line.ItemId,
                rawMessageId: message.Id, productionOrderId: null,
                quantityKg: line.QuantityKg, quantityMeter: line.QuantityMeter,
                direction: TransactionDirection.In, createdBy: _currentUser.UserName));
        }

        await _db.SaveChangesAsync(cancellationToken);

        return new RawMessageDto
        {
            Id = message.Id,
            MessageNumber = message.MessageNumber,
            ReceiptDate = message.ReceiptDate,
            CustomerId = message.CustomerId,
            WarehouseId = message.WarehouseId,
            ReceivingUser = message.ReceivingUser,
            Notes = message.Notes,
            InspectionStatus = message.InspectionStatus,
            Status = message.Status,
            Lines = message.Lines.Select(l => new RawMessageLineDto
            {
                Id = l.Id,
                ItemId = l.ItemId,
                QuantityKg = l.QuantityKg,
                QuantityMeter = l.QuantityMeter,
                PieceCount = l.PieceCount,
                Notes = l.Notes,
                RemainingKg = l.QuantityKg,
                RemainingMeter = l.QuantityMeter
            }).ToList()
        };
    }
}
