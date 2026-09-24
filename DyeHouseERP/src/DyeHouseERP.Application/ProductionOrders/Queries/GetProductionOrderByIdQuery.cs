using DyeHouseERP.Application.Common.Exceptions;
using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.ProductionOrders.DTOs;
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
            CustomerReference = order.CustomerReference,
            Notes = order.Notes,
            Priority = order.Priority,
            OrderDate = order.OrderDate,
            Status = order.Status,
            ReprocessingOfProductionOrderId = order.ReprocessingOfProductionOrderId,
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
                    Operator = s.Operator,
                    Notes = s.Notes,
                    StartedAtUtc = s.StartedAtUtc,
                    CompletedAtUtc = s.CompletedAtUtc,
                    RequiresApproval = def?.RequiresApproval ?? false,
                    AllowSkip = def?.AllowSkip ?? false
                };
            }).ToList()
        };
    }
}
