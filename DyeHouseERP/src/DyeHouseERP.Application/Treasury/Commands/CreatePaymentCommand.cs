using DyeHouseERP.Application.Common.Exceptions;
using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.Treasury.DTOs;
using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.Treasury.Commands;

public record CreatePaymentCommand(DateTime PaymentDate, Guid TreasuryAccountId, decimal Amount, string PayeeDescription,
    string? PaymentMethod, string? Description) : IRequest<PaymentDto>;

public class CreatePaymentCommandValidator : AbstractValidator<CreatePaymentCommand>
{
    public CreatePaymentCommandValidator()
    {
        RuleFor(x => x.TreasuryAccountId).NotEmpty();
        RuleFor(x => x.Amount).GreaterThan(0);
        RuleFor(x => x.PayeeDescription).NotEmpty();
    }
}

public class CreatePaymentCommandHandler : IRequestHandler<CreatePaymentCommand, PaymentDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IDocumentNumberGenerator _numberGenerator;
    private readonly IPeriodCloseService _periodClose;

    public CreatePaymentCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser, IDocumentNumberGenerator numberGenerator, IPeriodCloseService periodClose)
    {
        _db = db; _currentUser = currentUser; _numberGenerator = numberGenerator; _periodClose = periodClose;
    }

    public async Task<PaymentDto> Handle(CreatePaymentCommand request, CancellationToken cancellationToken)
    {
        await _periodClose.EnsureOpenAsync(request.PaymentDate, cancellationToken);

        var account = await _db.TreasuryAccounts.FirstOrDefaultAsync(a => a.Id == request.TreasuryAccountId, cancellationToken)
            ?? throw new NotFoundException("TreasuryAccount", request.TreasuryAccountId);

        var paymentNumber = await _numberGenerator.NextAsync(DocumentType.Payment, cancellationToken: cancellationToken);

        var payment = new Payment(paymentNumber, request.PaymentDate, request.TreasuryAccountId, request.Amount,
            request.PayeeDescription, _currentUser.UserName, request.PaymentMethod, request.Description);
        _db.Payments.Add(payment);

        _db.TreasuryTransactions.Add(new TreasuryTransaction(
            request.TreasuryAccountId, request.PaymentDate, DocumentType.Payment, paymentNumber, payment.Id,
            request.Amount, TreasuryDirection.Out, request.Description, _currentUser.UserName));

        await _db.SaveChangesAsync(cancellationToken);

        return new PaymentDto
        {
            Id = payment.Id, PaymentNumber = payment.PaymentNumber, PaymentDate = payment.PaymentDate,
            TreasuryAccountId = account.Id, TreasuryAccountName = account.Name,
            Amount = payment.Amount, PayeeDescription = payment.PayeeDescription, Description = payment.Description
        };
    }
}
