using DyeHouseERP.Application.Common.Exceptions;
using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.ProductionOrders.DTOs;
using DyeHouseERP.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.ProductionOrders.Queries;

public record GetProductionOrderByIdQuery(Guid Id) : IRequest<ProductionOrderDto>;

public class GetProductionOrderByIdQueryHandler : IRequestHandler<GetProductionOrderByIdQuery, ProductionOrderDto>
{
    private readonly IApplicationDbContext _db;
    public GetProductionOrderByIdQueryHandler(IApplicationDbContext db) => _db = db;

    public Task<ProductionOrderDto> Handle(GetProductionOrderByIdQuery request, CancellationToken cancellationToken)
        => LoadDtoAsync(_db, request.Id, cancellationToken);

    /// <summary>
    /// Shared loader so CreateProductionOrderCommand / AllocateRaw / stage
    /// commands can all return the same fully-hydrated DTO shape after a
    /// write, without duplicating the join/mapping logic in each handler.
    /// </summary>
    internal static async Task<ProductionOrderDto> LoadDtoAsync(IApplicationDbContext db, Guid orderId, CancellationToken cancellationToken)
    {
        var order = await db.ProductionOrders.AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken)
            ?? throw new NotFoundException("ProductionOrder", orderId);

        var customer = await db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == order.CustomerId, cancellationToken);
        var item = await db.Items.AsNoTracking().FirstOrDefaultAsync(i => i.Id == order.ItemId, cancellationToken);

        var stageExecutions = await db.ProductionOrderStageExecutions.AsNoTracking()
            .Where(s => s.ProductionOrderId == orderId)
            .OrderBy(s => s.Sequence)
            .ToListAsync(cancellationToken);

        var stageDefIds = stageExecutions.Select(s => s.StageDefinitionId).ToList();
        var stageDefs = await db.ProductionStageDefinitions.AsNoTracking()
            .Where(s => stageDefIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, cancellationToken);

        var allocations = await db.RawAllocations.AsNoTracking()
            .Where(a => a.ProductionOrderId == orderId)
            .ToListAsync(cancellationToken);

        var messageIds = allocations.Select(a => a.RawMessageId).Distinct().ToList();
        var messages = await db.RawMessages.AsNoTracking()
            .Where(m => messageIds.Contains(m.Id))
            .ToDictionaryAsync(m => m.Id, cancellationToken);

        // Required / allocated / remaining / allocation status (spec section 13). The sum is
        // taken over the allocation ROWS that were just read back, and completeness is judged
        // by the domain's own rule - the very same call RawAllocationGuard makes before it
        // lets production start - so the figure shown here is the figure enforced.
        var allocatedKg = allocations.Sum(a => a.QuantityKg ?? 0m);
        var allocatedMeter = allocations.Sum(a => a.QuantityMeter ?? 0m);

        // Original raw accounting across partial deliveries (spec section 31). The original quantity is
        // the order's own fixed figure; what has been settled is summed from the lines of every
        // NON-cancelled delivery of this order, and remaining comes from the domain rule that owns the
        // calculation - so an order only ever reports what the backend would actually let a delivery do.
        var cancelledDeliveryIds = await db.Deliveries.AsNoTracking()
            .Where(d => d.Status == DeliveryStatus.Cancelled)
            .Select(d => d.Id)
            .ToListAsync(cancellationToken);

        var deliveredLines = await db.DeliveryLines.AsNoTracking()
            .Where(l => l.ProductionOrderId == orderId && !cancelledDeliveryIds.Contains(l.DeliveryId))
            .Select(l => new { l.OriginalRawQuantityKg, l.OriginalRawQuantityMeter })
            .ToListAsync(cancellationToken);

        var deliveredOriginalRawKg = deliveredLines.Sum(l => l.OriginalRawQuantityKg ?? 0m);
        var deliveredOriginalRawMeter = deliveredLines.Sum(l => l.OriginalRawQuantityMeter ?? 0m);
        var (remainingOriginalRawKg, remainingOriginalRawMeter) =
            order.RemainingOriginalRaw(deliveredOriginalRawKg, deliveredOriginalRawMeter);

        string? customerReturnNumber = null;
        if (order.CustomerReturnId.HasValue)
        {
            customerReturnNumber = await db.CustomerReturns.AsNoTracking()
                .Where(r => r.Id == order.CustomerReturnId.Value)
                .Select(r => r.ReturnNumber)
                .FirstOrDefaultAsync(cancellationToken);
        }

        return new ProductionOrderDto
        {
            Id = order.Id,
            OrderNumber = order.OrderNumber,
            CustomerId = order.CustomerId,
            CustomerCode = customer?.Code ?? string.Empty,
            CustomerName = customer?.Name ?? string.Empty,
            ItemId = order.ItemId,
            ItemCode = item?.Code ?? string.Empty,
            ItemName = item?.Name ?? string.Empty,
            Color = order.Color,
            RequestedQuantityKg = order.RequestedQuantityKg,
            RequestedQuantityMeter = order.RequestedQuantityMeter,
            RawOrigin = order.RawOrigin,
            CustomerReference = order.CustomerReference,
            Notes = order.Notes,
            Priority = order.Priority,
            JobOrderType = order.JobOrderType,
            OrderDate = order.OrderDate,
            Status = order.Status,
            ReprocessingOfProductionOrderId = order.ReprocessingOfProductionOrderId,
            SourceIssueTransactionId = order.SourceIssueTransactionId,
            FormationRequestId = order.FormationRequestId,
            FormationBasinId = order.FormationBasinId,
            CustomerReturnId = order.CustomerReturnId,
            CustomerReturnNumber = customerReturnNumber,
            CostBearer = order.CostBearer,
            OriginalRawQuantityKg = order.OriginalRawQuantityKg,
            OriginalRawQuantityMeter = order.OriginalRawQuantityMeter,
            DeliveredOriginalRawKg = deliveredOriginalRawKg,
            DeliveredOriginalRawMeter = deliveredOriginalRawMeter,
            RemainingOriginalRawKg = remainingOriginalRawKg,
            RemainingOriginalRawMeter = remainingOriginalRawMeter,
            // Derived from the stage executions loaded below, never from a stored flag.
            AllocatedQuantityKg = allocatedKg,
            AllocatedQuantityMeter = allocatedMeter,
            RemainingToAllocateKg = Math.Max(0m, (order.RequestedQuantityKg ?? 0m) - allocatedKg),
            RemainingToAllocateMeter = Math.Max(0m, (order.RequestedQuantityMeter ?? 0m) - allocatedMeter),
            IsRawAllocationComplete = order.IsRawAllocationComplete(allocatedKg, allocatedMeter),
            HasProductionStarted = order.Status == ProductionOrderStatus.InProduction
                || stageExecutions.Any(s => s.OutputKg != null || s.OutputMeter != null
                    || s.SeparatesKg != null || s.SeparatesMeter != null
                    || s.LossKg != null || s.LossMeter != null
                    || s.Status == StageExecutionStatus.Completed || s.Status == StageExecutionStatus.Skipped
                    || s.CompletedAtUtc != null),
            RawAllocations = allocations.Select(a => new RawAllocationDto
            {
                Id = a.Id,
                RawMessageId = a.RawMessageId,
                MessageNumber = messages.GetValueOrDefault(a.RawMessageId)?.MessageNumber ?? string.Empty,
                QuantityKg = a.QuantityKg,
                QuantityMeter = a.QuantityMeter,
                AllocatedBy = a.AllocatedBy,
                AllocatedAtUtc = a.AllocatedAtUtc
            }).ToList(),
            StageExecutions = stageExecutions.Select(s =>
            {
                stageDefs.TryGetValue(s.StageDefinitionId, out var def);
                return new StageExecutionDto
                {
                    Id = s.Id,
                    StageDefinitionId = s.StageDefinitionId,
                    RequiresDrawingNumber = s.RequiresDrawingNumber,
                    DrawingNumber = s.DrawingNumber,
                    StageCode = def?.Code ?? string.Empty,
                    StageName = def?.Name ?? string.Empty,
                    Sequence = s.Sequence,
                    Status = s.Status,
                    InputKg = s.InputKg,
                    InputMeter = s.InputMeter,
                    OutputKg = s.OutputKg,
                    OutputMeter = s.OutputMeter,
                    LossKg = s.LossKg,
                    LossMeter = s.LossMeter,
                    SeparatesKg = s.SeparatesKg,
                    SeparatesMeter = s.SeparatesMeter,
                    SecondGradeKg = s.SecondGradeKg,
                    SecondGradeMeter = s.SecondGradeMeter,
                    MachineId = s.MachineId,
                    ProductionShiftId = s.ProductionShiftId,
                    Operator = s.Operator,
                    Notes = s.Notes,
                    StartedAtUtc = s.StartedAtUtc,
                    CompletedAtUtc = s.CompletedAtUtc
                };
            }).ToList()
        };
    }
}
