using DyeHouseERP.Application.Common.Exceptions;
using DyeHouseERP.Application.Common.Interfaces;
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

    public CreateMaterialIssueCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser,
        IDocumentNumberGenerator numberGenerator, IMaterialLedgerService ledger, IDateTime clock)
    {
        _db = db; _currentUser = currentUser; _numberGenerator = numberGenerator; _ledger = ledger; _clock = clock;
    }

    public async Task<MaterialIssueDto> Handle(CreateMaterialIssueCommand request, CancellationToken cancellationToken)
    {
        var material = await _db.Materials.FirstOrDefaultAsync(m => m.Id == request.MaterialId, cancellationToken)
            ?? throw new NotFoundException("Material", request.MaterialId);
        var order = await _db.ProductionOrders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == request.ProductionOrderId, cancellationToken)
            ?? throw new NotFoundException("ProductionOrder", request.ProductionOrderId);

        var balance = await _ledger.GetBalanceAsync(request.MaterialId, request.WarehouseId, cancellationToken);
        if (request.Quantity > balance)
            throw new NegativeStockException(balance, request.Quantity);

        var unitCost = request.UnitCost ?? material.PurchasePrice;
        var issueNumber = await _numberGenerator.NextAsync(DocumentType.MaterialIssue, cancellationToken: cancellationToken);

        var issue = new MaterialIssue(issueNumber, _clock.UtcNow, request.MaterialId, request.WarehouseId,
            request.ProductionOrderId, request.Quantity, unitCost, _currentUser.UserName, request.Notes);
        _db.MaterialIssues.Add(issue);

        _db.MaterialTransactions.Add(new MaterialTransaction(
            DocumentType.MaterialIssue, issue.IssueNumber, issue.Id, _clock.UtcNow,
            request.MaterialId, request.WarehouseId, request.ProductionOrderId,
            request.Quantity, MaterialTransactionDirection.Out, unitCost, _currentUser.UserName));

        await _db.SaveChangesAsync(cancellationToken);

        return new MaterialIssueDto
        {
            Id = issue.Id, IssueNumber = issue.IssueNumber, IssueDate = issue.IssueDate,
            MaterialId = material.Id, MaterialCode = material.Code, MaterialName = material.Name,
            WarehouseId = issue.WarehouseId, ProductionOrderId = order.Id, ProductionOrderNumber = order.OrderNumber,
            Quantity = issue.Quantity, UnitCost = issue.UnitCost, TotalCost = issue.TotalCost, Notes = issue.Notes
        };
    }
}
