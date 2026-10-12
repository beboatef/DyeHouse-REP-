using DyeHouseERP.Application.Common.Exceptions;
using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.Common.Services;
using DyeHouseERP.Application.Materials.DTOs;
using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Enums;
using DyeHouseERP.Domain.Exceptions;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.Materials.Commands;

/// <summary>Issues a material against a Production Order (spec section 27) - becomes part of that order's material cost.</summary>
public record CreateMaterialIssueCommand(
    Guid MaterialId, Guid WarehouseId, Guid ProductionOrderId, decimal Quantity, decimal? UnitCost, string? Notes)
    : IRequest<MaterialIssueDto>;

public class CreateMaterialIssueCommandValidator : AbstractValidator<CreateMaterialIssueCommand>
{
    public CreateMaterialIssueCommandValidator()
    {
        RuleFor(x => x.MaterialId).NotEmpty();
        RuleFor(x => x.WarehouseId).NotEmpty();
        RuleFor(x => x.ProductionOrderId).NotEmpty();
        RuleFor(x => x.Quantity).GreaterThan(0);
    }
}

public class CreateMaterialIssueCommandHandler : IRequestHandler<CreateMaterialIssueCommand, MaterialIssueDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IDocumentNumberGenerator _numberGenerator;
    private readonly IMaterialLedgerService _ledger;
    private readonly IDateTime _clock;
    private readonly IPeriodCloseService _periodClose;
    private readonly IAllocationLockService _stockLock;

    public CreateMaterialIssueCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser,
        IDocumentNumberGenerator numberGenerator, IMaterialLedgerService ledger, IDateTime clock,
        IPeriodCloseService periodClose, IAllocationLockService stockLock)
    {
        _db = db; _currentUser = currentUser; _numberGenerator = numberGenerator; _ledger = ledger; _clock = clock;
        _periodClose = periodClose; _stockLock = stockLock;
    }

    public async Task<MaterialIssueDto> Handle(CreateMaterialIssueCommand request, CancellationToken cancellationToken)
    {
        // Spec section 43: issuing chemicals to production is a stock posting.
        // One clock read for the guard, the document date and the ledger row below.
        var postingDate = _clock.UtcNow;
        await using var periodGuard = await _periodClose.BeginPostingAsync(postingDate, cancellationToken);

        var material = await _db.Materials.FirstOrDefaultAsync(m => m.Id == request.MaterialId, cancellationToken)
            ?? throw new NotFoundException("Material", request.MaterialId);
        var order = await _db.ProductionOrders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == request.ProductionOrderId, cancellationToken)
            ?? throw new NotFoundException("ProductionOrder", request.ProductionOrderId);

        // Issuing material writes an OUT row against this warehouse, so the warehouse must
        // still be active: a deactivated one never receives a new stock row, while everything
        // already issued from it stays readable and unchanged.
        await WarehouseGuard.EnsureUsableForNewMovementAsync(
            _db, request.WarehouseId, cancellationToken, "to issue material out of it");

        // H6: material stock is guarded by the same lock mechanism as raw
        // inventory, keyed on (warehouse, material) - the exact dimension the
        // balance query below reads.
        await using (await _stockLock.AcquireAsync(
            StockLockKey.MaterialLot(request.WarehouseId, request.MaterialId), cancellationToken))
        {

        var balance = await _ledger.GetBalanceAsync(request.MaterialId, request.WarehouseId, cancellationToken);
        if (request.Quantity > balance)
            throw new NegativeStockException(balance, request.Quantity);

        var unitCost = request.UnitCost ?? material.PurchasePrice;
        var issueNumber = await _numberGenerator.NextAsync(DocumentType.MaterialIssue, cancellationToken: cancellationToken);

        var issue = new MaterialIssue(issueNumber, postingDate, request.MaterialId, request.WarehouseId,
            request.ProductionOrderId, request.Quantity, unitCost, _currentUser.UserName, request.Notes);
        _db.MaterialIssues.Add(issue);

        _db.MaterialTransactions.Add(new MaterialTransaction(
            DocumentType.MaterialIssue, issue.IssueNumber, issue.Id, postingDate,
            request.MaterialId, request.WarehouseId, request.ProductionOrderId,
            request.Quantity, MaterialTransactionDirection.Out, unitCost, _currentUser.UserName));

        await _db.SaveChangesAsync(cancellationToken);

        return new MaterialIssueDto
        {
            Id = issue.Id, IssueNumber = issue.IssueNumber, IssueDate = issue.IssueDate,
            MaterialId = material.Id, MaterialCode = material.Code, MaterialName = material.Name,
            WarehouseId = issue.WarehouseId, ProductionOrderId = order.Id, ProductionOrderNumber = order.OrderNumber,
            Quantity = issue.Quantity, UnitCost = issue.UnitCost,            TotalCost = issue.TotalCost, Notes = issue.Notes
        };
        }
    }
}
