using DyeHouseERP.Application.Common.Exceptions;
using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.ProductionOrders.DTOs;
using DyeHouseERP.Application.ProductionOrders.Queries;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.ProductionOrders.Commands;

/// <summary>Marks a Production Order Completed once every stage is done (spec section 12). If this order exists to reprocess a Separate, the Separate is marked Reprocessed too (spec section 23).</summary>
public record CompleteProductionOrderCommand(Guid ProductionOrderId) : IRequest<ProductionOrderDto>;

public class CompleteProductionOrderCommandHandler : IRequestHandler<CompleteProductionOrderCommand, ProductionOrderDto>
{
    private readonly IApplicationDbContext _db;
    public CompleteProductionOrderCommandHandler(IApplicationDbContext db) => _db = db;

    public async Task<ProductionOrderDto> Handle(CompleteProductionOrderCommand request, CancellationToken cancellationToken)
    {
        var order = await _db.ProductionOrders.FirstOrDefaultAsync(o => o.Id == request.ProductionOrderId, cancellationToken)
            ?? throw new NotFoundException("ProductionOrder", request.ProductionOrderId);

        // Load stage executions explicitly since the aggregate's private
        // collection is backed by its own table, not always populated by a
        // plain FirstOrDefaultAsync without Include.
        var stages = await _db.ProductionOrderStageExecutions.Where(s => s.ProductionOrderId == order.Id).ToListAsync(cancellationToken);
        if (stages.Any(s => s.Status is Domain.Enums.StageExecutionStatus.Pending or Domain.Enums.StageExecutionStatus.InProgress))
            throw new Domain.Exceptions.DomainException("Cannot complete a production order while stages are still pending or in progress.");

        order.Complete();

        if (order.ReprocessingOfProductionOrderId.HasValue)
        {
            var separate = await _db.Separates.FirstOrDefaultAsync(s => s.ReprocessingProductionOrderId == order.Id, cancellationToken);
            separate?.MarkReprocessed();
        }

        await _db.SaveChangesAsync(cancellationToken);
        return await GetProductionOrderByIdQueryHandler.LoadDtoAsync(_db, order.Id, cancellationToken);
    }
}
