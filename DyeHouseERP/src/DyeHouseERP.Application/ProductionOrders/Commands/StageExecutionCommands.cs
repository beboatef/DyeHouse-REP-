using DyeHouseERP.Application.Common.Exceptions;
using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.ProductionOrders.DTOs;
using DyeHouseERP.Application.ProductionOrders.Queries;
using DyeHouseERP.Domain.Enums;
using DyeHouseERP.Domain.Exceptions;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.ProductionOrders.Commands;

// ---------------------------------------------------------------------
// Start
// ---------------------------------------------------------------------

public record StartStageCommand(Guid StageExecutionId) : IRequest<ProductionOrderDto>;

public class StartStageCommandHandler : IRequestHandler<StartStageCommand, ProductionOrderDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public StartStageCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<ProductionOrderDto> Handle(StartStageCommand request, CancellationToken cancellationToken)
    {
        var stage = await _db.ProductionOrderStageExecutions.FirstOrDefaultAsync(s => s.Id == request.StageExecutionId, cancellationToken)
            ?? throw new NotFoundException("ProductionOrderStageExecution", request.StageExecutionId);

        var order = await _db.ProductionOrders.FirstOrDefaultAsync(o => o.Id == stage.ProductionOrderId, cancellationToken)
            ?? throw new NotFoundException("ProductionOrder", stage.ProductionOrderId);

        if (order.Status == ProductionOrderStatus.RawAllocated)
            order.MarkInProduction();

        stage.Start(_currentUser.UserName);

        await _db.SaveChangesAsync(cancellationToken);
        return await GetProductionOrderByIdQueryHandler.LoadDtoAsync(_db, order.Id, cancellationToken);
    }
}

// ---------------------------------------------------------------------
// Complete
// ---------------------------------------------------------------------

public record CompleteStageCommand(
    Guid StageExecutionId,
    decimal? InputKg, decimal? InputMeter,
    decimal? OutputKg, decimal? OutputMeter,
    decimal? LossKg, decimal? LossMeter,
    decimal? SeparatesKg, decimal? SeparatesMeter,
    string? Notes, string? ApprovedBy) : IRequest<ProductionOrderDto>;

public class CompleteStageCommandValidator : AbstractValidator<CompleteStageCommand>
{
    public CompleteStageCommandValidator()
    {
        RuleFor(x => x.StageExecutionId).NotEmpty();
    }
}

public class CompleteStageCommandHandler : IRequestHandler<CompleteStageCommand, ProductionOrderDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public CompleteStageCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<ProductionOrderDto> Handle(CompleteStageCommand request, CancellationToken cancellationToken)
    {
        var stage = await _db.ProductionOrderStageExecutions.FirstOrDefaultAsync(s => s.Id == request.StageExecutionId, cancellationToken)
            ?? throw new NotFoundException("ProductionOrderStageExecution", request.StageExecutionId);

        var definition = await _db.ProductionStageDefinitions.AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == stage.StageDefinitionId, cancellationToken)
            ?? throw new NotFoundException("ProductionStageDefinition", stage.StageDefinitionId);

        // Enforce which quantities THIS stage type actually requires (spec
        // sections 20-21) - never a single formula forced on every stage.
        if (definition.RequiresInputQuantity && request.InputKg is null && request.InputMeter is null)
            throw new DomainException($"Stage '{definition.Name}' requires an input quantity (KG and/or Meter).");
        if (definition.RequiresOutputQuantity && request.OutputKg is null && request.OutputMeter is null)
            throw new DomainException($"Stage '{definition.Name}' requires an output quantity (KG and/or Meter).");

        stage.Complete(
            request.InputKg, request.InputMeter, request.OutputKg, request.OutputMeter,
            request.LossKg, request.LossMeter, request.SeparatesKg, request.SeparatesMeter,
            request.Notes, definition.RequiresApproval, request.ApprovedBy);

        // Auto-track any Separates recorded on this stage (spec section 22) -
        // they are never just a number on the stage row; each becomes its
        // own trackable record from the moment it's produced.
        if (request.SeparatesKg is > 0 || request.SeparatesMeter is > 0)
        {
            var order = await _db.ProductionOrders.AsNoTracking()
                .FirstOrDefaultAsync(o => o.Id == stage.ProductionOrderId, cancellationToken)
                ?? throw new NotFoundException("ProductionOrder", stage.ProductionOrderId);

            _db.Separates.Add(new Domain.Entities.Separate(
                order.Id, stage.Id, order.CustomerId, order.ItemId,
                request.SeparatesKg, request.SeparatesMeter, _currentUser.UserName,
                reason: $"Stage '{definition.Name}' completion"));
        }

        await _db.SaveChangesAsync(cancellationToken);
        return await GetProductionOrderByIdQueryHandler.LoadDtoAsync(_db, stage.ProductionOrderId, cancellationToken);
    }
}

// ---------------------------------------------------------------------
// Skip
// ---------------------------------------------------------------------

public record SkipStageCommand(Guid StageExecutionId, string Reason) : IRequest<ProductionOrderDto>;

public class SkipStageCommandValidator : AbstractValidator<SkipStageCommand>
{
    public SkipStageCommandValidator()
    {
        RuleFor(x => x.Reason).NotEmpty();
    }
}

public class SkipStageCommandHandler : IRequestHandler<SkipStageCommand, ProductionOrderDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public SkipStageCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<ProductionOrderDto> Handle(SkipStageCommand request, CancellationToken cancellationToken)
    {
        var stage = await _db.ProductionOrderStageExecutions.FirstOrDefaultAsync(s => s.Id == request.StageExecutionId, cancellationToken)
            ?? throw new NotFoundException("ProductionOrderStageExecution", request.StageExecutionId);

        var definition = await _db.ProductionStageDefinitions.AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == stage.StageDefinitionId, cancellationToken)
            ?? throw new NotFoundException("ProductionStageDefinition", stage.StageDefinitionId);

        stage.Skip(definition.AllowSkip, request.Reason, _currentUser.UserName);

        await _db.SaveChangesAsync(cancellationToken);
        return await GetProductionOrderByIdQueryHandler.LoadDtoAsync(_db, stage.ProductionOrderId, cancellationToken);
    }
}
