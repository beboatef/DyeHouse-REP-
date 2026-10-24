using DyeHouseERP.Application.Common.Exceptions;
using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.Common.Services;
using DyeHouseERP.Application.ProductionOrders.DTOs;
using DyeHouseERP.Application.ProductionOrders.Queries;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.ProductionOrders.Commands;

/// <summary>
/// Marks a Production Order Completed once every stage is done (spec sections 12 and 18).
/// The reason is mandatory and recorded on the order: a close with no output has to be
/// explainable, and one with output still records who closed it and why. If this order
/// exists to reprocess a Separate, the Separate is marked Reprocessed too (spec section 23).
/// </summary>
public record CompleteProductionOrderCommand(Guid ProductionOrderId, string Reason) : IRequest<ProductionOrderDto>;

public class CompleteProductionOrderCommandValidator : AbstractValidator<CompleteProductionOrderCommand>
{
    public CompleteProductionOrderCommandValidator()
    {
        RuleFor(x => x.ProductionOrderId).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty()
            .WithMessage("Completing a production order requires a reason - it is recorded with the completion.")
            .MaximumLength(1000);
    }
}

public class CompleteProductionOrderCommandHandler : IRequestHandler<CompleteProductionOrderCommand, ProductionOrderDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    public CompleteProductionOrderCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    { _db = db; _currentUser = currentUser; }

    public async Task<ProductionOrderDto> Handle(CompleteProductionOrderCommand request, CancellationToken cancellationToken)
    {
        var order = await _db.ProductionOrders.FirstOrDefaultAsync(o => o.Id == request.ProductionOrderId, cancellationToken)
            ?? throw new NotFoundException("ProductionOrder", request.ProductionOrderId);

        // H1: completing the Job Order (with or without output) needs 100% of the raw
        // allocated. Abandoning an unallocated order stays possible through Cancel, which
        // is the correct way to close a job that never consumed material.
        await RawAllocationGuard.EnsureCompleteAsync(_db, order, cancellationToken);

        // Load stage executions explicitly since the aggregate's private
        // collection is backed by its own table, not always populated by a
        // plain FirstOrDefaultAsync without Include.
        var stages = await _db.ProductionOrderStageExecutions.Where(s => s.ProductionOrderId == order.Id).ToListAsync(cancellationToken);
        if (stages.Any(s => s.Status is Domain.Enums.StageExecutionStatus.Pending or Domain.Enums.StageExecutionStatus.InProgress))
            throw new Domain.Exceptions.DomainException("Cannot complete a production order while stages are still pending or in progress.");

        order.CompleteWithReason(request.Reason);

        if (order.ReprocessingOfProductionOrderId.HasValue)
        {
            var separates = await _db.Separates
                .Where(s => s.ReprocessingProductionOrderId == order.Id).ToListAsync(cancellationToken);
            foreach (var separate in separates)
                separate.MarkReprocessed();
        }

        await _db.SaveChangesAsync(cancellationToken);
        return await GetProductionOrderByIdQueryHandler.LoadDtoAsync(_db, order.Id, cancellationToken);
    }
}
