using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.CostAccounting.DTOs;
using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Enums;
using FluentValidation;
using MediatR;

namespace DyeHouseERP.Application.CostAccounting.Commands;

public record CreateCostEntryCommand(Guid ProductionOrderId, CostCategory Category, decimal Amount, DateTime EntryDate, string? Description)
    : IRequest<CostEntryDto>;

public class CreateCostEntryCommandValidator : AbstractValidator<CreateCostEntryCommand>
{
    public CreateCostEntryCommandValidator()
    {
        RuleFor(x => x.ProductionOrderId).NotEmpty();
        RuleFor(x => x.Amount).GreaterThanOrEqualTo(0);
    }
}

public class CreateCostEntryCommandHandler : IRequestHandler<CreateCostEntryCommand, CostEntryDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IPeriodCloseService _periodClose;
    public CreateCostEntryCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser, IPeriodCloseService periodClose)
    { _db = db; _currentUser = currentUser; _periodClose = periodClose; }

    public async Task<CostEntryDto> Handle(CreateCostEntryCommand request, CancellationToken cancellationToken)
    {
        // Spec section 43: the cost entry is filed under its own accounting date, so that
        // exact date must fall inside an open period before anything is persisted.
        await using var periodGuard = await _periodClose.BeginPostingAsync(request.EntryDate, cancellationToken);

        var entry = new CostEntry(request.ProductionOrderId, request.Category, request.Amount, request.EntryDate, _currentUser.UserName, request.Description);
        _db.CostEntries.Add(entry);
        await _db.SaveChangesAsync(cancellationToken);

        return new CostEntryDto { Id = entry.Id, ProductionOrderId = entry.ProductionOrderId, Category = entry.Category, Amount = entry.Amount, EntryDate = entry.EntryDate, Description = entry.Description };
    }
}
