using DyeHouseERP.Application.Common.Exceptions;
using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.CostAccounting.DTOs;
using DyeHouseERP.Application.CostAccounting.Queries;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.CostAccounting.Commands;

/// <summary>
/// Records/updates the ESTIMATED cost of a Job Order (spec section 34). The
/// estimate is advisory and may be revised while the order is open; the actual
/// cost is never written here - it always comes from posted transactions.
/// </summary>
public record SetProductionOrderEstimateCommand(Guid ProductionOrderId, decimal? EstimatedCost,
    string? CostingNotes = null) : IRequest<ProductionOrderCostDto>;

public class SetProductionOrderEstimateCommandHandler
    : IRequestHandler<SetProductionOrderEstimateCommand, ProductionOrderCostDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly ISender _mediator;

    public SetProductionOrderEstimateCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser, ISender mediator)
    { _db = db; _currentUser = currentUser; _mediator = mediator; }

    public async Task<ProductionOrderCostDto> Handle(SetProductionOrderEstimateCommand request, CancellationToken cancellationToken)
    {
        var order = await _db.ProductionOrders
            .FirstOrDefaultAsync(o => o.Id == request.ProductionOrderId, cancellationToken)
            ?? throw new NotFoundException("ProductionOrder", request.ProductionOrderId);

        order.SetEstimatedCost(request.EstimatedCost, request.CostingNotes, _currentUser.UserName);
        await _db.SaveChangesAsync(cancellationToken);

        return await _mediator.Send(new GetProductionOrderCostQuery(request.ProductionOrderId), cancellationToken);
    }
}

/// <summary>
/// Signs off the APPROVED cost of a completed Job Order (spec section 34). The
/// approved figure is an explicit, attributable decision (who + when), recorded
/// separately from the live actual so a later transaction can never silently
/// change what was approved.
/// </summary>
public record ApproveProductionOrderCostCommand(Guid ProductionOrderId, decimal ApprovedCost,
    string? CostingNotes = null) : IRequest<ProductionOrderCostDto>;

public class ApproveProductionOrderCostCommandHandler
    : IRequestHandler<ApproveProductionOrderCostCommand, ProductionOrderCostDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly ISender _mediator;

    public ApproveProductionOrderCostCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser, ISender mediator)
    { _db = db; _currentUser = currentUser; _mediator = mediator; }

    public async Task<ProductionOrderCostDto> Handle(ApproveProductionOrderCostCommand request, CancellationToken cancellationToken)
    {
        var order = await _db.ProductionOrders
            .FirstOrDefaultAsync(o => o.Id == request.ProductionOrderId, cancellationToken)
            ?? throw new NotFoundException("ProductionOrder", request.ProductionOrderId);

        order.ApproveCost(request.ApprovedCost, request.CostingNotes, _currentUser.UserName);
        await _db.SaveChangesAsync(cancellationToken);

        return await _mediator.Send(new GetProductionOrderCostQuery(request.ProductionOrderId), cancellationToken);
    }
}
