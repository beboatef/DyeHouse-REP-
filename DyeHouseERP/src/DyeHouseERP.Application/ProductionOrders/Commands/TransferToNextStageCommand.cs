using System.Text.Json;
using DyeHouseERP.Application.Common.Exceptions;
using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.ProductionOrders.DTOs;
using DyeHouseERP.Application.ProductionOrders.Queries;
using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Enums;
using DyeHouseERP.Domain.Exceptions;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.ProductionOrders.Commands;

// =====================================================================
// Dynamic stage transfer (spec sections 13-17)
// =====================================================================

/// <summary>
/// Moves a Job Order from its current stage to the next one the USER chooses.
/// There is deliberately no separate "End Stage" button: choosing the next stage
/// IS the transfer, and it does all of this in one auditable step (spec 14):
///   1. closes the current stage,
///   2. records its final output,
///   3. computes loss and loss % against that stage's immutable baseline,
///   4. locks the closed stage,
///   5. activates ONLY the selected next stage, using the closed stage's output
///      as its baseline.
/// When the selected stage is the one marked IsReadyGoodsStage the output is
/// moved to the Ready Goods warehouse and the order is completed instead
/// (spec section 17) - still no separate "End Job Order" button.
/// </summary>
public record TransferToNextStageCommand(
    Guid StageExecutionId,
    Guid NextStageDefinitionId,
    decimal? OutputKg,
    decimal? OutputMeter,
    decimal? SeparatesKg,
    decimal? SeparatesMeter,
    string? Notes) : IRequest<ProductionOrderDto>;

public class TransferToNextStageCommandValidator : AbstractValidator<TransferToNextStageCommand>
{
    public TransferToNextStageCommandValidator()
    {
        RuleFor(x => x.StageExecutionId).NotEmpty();
        RuleFor(x => x.NextStageDefinitionId).NotEmpty();
        RuleFor(x => x)
            .Must(x => x.OutputKg is > 0 || x.OutputMeter is > 0)
            .WithMessage("Enter the final output quantity for this stage - the loss is measured from it.");
        RuleFor(x => x.OutputKg).GreaterThanOrEqualTo(0).When(x => x.OutputKg.HasValue);
        RuleFor(x => x.OutputMeter).GreaterThanOrEqualTo(0).When(x => x.OutputMeter.HasValue);
    }
}

public class TransferToNextStageCommandHandler : IRequestHandler<TransferToNextStageCommand, ProductionOrderDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IDateTime _clock;
    private readonly IPeriodCloseService _periodClose;
    private readonly IDocumentNumberGenerator _numberGenerator;

    public TransferToNextStageCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser,
        IDateTime clock, IPeriodCloseService periodClose, IDocumentNumberGenerator numberGenerator)
    {
        _db = db; _currentUser = currentUser; _clock = clock; _periodClose = periodClose;
        _numberGenerator = numberGenerator;
    }

    public async Task<ProductionOrderDto> Handle(TransferToNextStageCommand request, CancellationToken cancellationToken)
    {
        var stage = await _db.ProductionOrderStageExecutions
            .FirstOrDefaultAsync(s => s.Id == request.StageExecutionId, cancellationToken)
            ?? throw new NotFoundException("ProductionOrderStageExecution", request.StageExecutionId);

        if (stage.Status != StageExecutionStatus.InProgress)
            throw new DomainException(
                $"This stage is {stage.Status}; only the stage currently in progress can be transferred.");

        var order = await _db.ProductionOrders.FirstOrDefaultAsync(o => o.Id == stage.ProductionOrderId, cancellationToken)
            ?? throw new NotFoundException("ProductionOrder", stage.ProductionOrderId);

        // The next stage is identified ONLY by the id the user picked. There is no
        // default route and nothing is inferred from Sequence.
        var nextStage = await _db.ProductionStageDefinitions.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == request.NextStageDefinitionId, cancellationToken)
            ?? throw new NotFoundException("ProductionStageDefinition", request.NextStageDefinitionId);

        if (!nextStage.IsActive)
            throw new DomainException($"Stage '{nextStage.Name}' is inactive and cannot be selected.");
        if (nextStage.Id == stage.StageDefinitionId)
            throw new DomainException(
                $"'{nextStage.Name}' is the stage this Job Order is already on. Choose a different next stage.");

        // A paused order has released its unused material; transferring it now
        // would post output for a job that is not running.
        if (order.Status == ProductionOrderStatus.Paused)
            throw new DomainException(
                $"Production order {order.OrderNumber} is paused. Resume it before transferring a stage.");

        var previousOutputKg = stage.OutputKg;
        var previousOutputMeter = stage.OutputMeter;

        // Steps 1-4: close, record output, derive loss and loss %, lock.
        stage.CloseWithOutput(request.OutputKg, request.OutputMeter,
            request.SeparatesKg, request.SeparatesMeter, request.Notes, _currentUser.UserName);

        // Audit: the figures the stage actually ended on and where they came
        // from, so a weight edited before transfer leaves a visible trail (spec 16).
        _db.AuditLogEntries.Add(new AuditLogEntry(
            _clock.UtcNow, _currentUser.UserName, "StageTransfer", "ProductionOrderStageExecution",
            stage.Id.ToString(),
            JsonSerializer.Serialize(new { outputKg = previousOutputKg, outputMeter = previousOutputMeter }),
            JsonSerializer.Serialize(new
            {
                outputKg = request.OutputKg,
                outputMeter = request.OutputMeter,
                baselineKg = stage.BaselineKg,
                lossKg = stage.LossKg,
                lossPercentKg = stage.LossPercentKg,
                nextStage = nextStage.Name
            }),
            ipAddress: null,
            reason: request.Notes));

        if (nextStage.IsReadyGoodsStage)
        {
            // Spec 17: the final stage moves the output to Ready Goods and completes
            // the order. The transfer IS the completion - there is no other button.
            await _periodClose.EnsureOpenAsync(_clock.UtcNow, cancellationToken);

            var readyWarehouse = await _db.Warehouses.AsNoTracking()
                .Where(w => w.Kind == WarehouseKind.ReadyGoods)
                .OrderBy(w => w.Id)
                .FirstOrDefaultAsync(cancellationToken)
                ?? throw new DomainException("No Ready Goods warehouse is configured.");

            var transferNumber = await _numberGenerator.NextAsync(
                DocumentType.ReadyGoodsTransfer, cancellationToken: cancellationToken);

            _db.ReadyGoodsTransfers.Add(new ReadyGoodsTransfer(
                transferNumber, _clock.UtcNow, order.Id, order.CustomerId, order.ItemId,
                readyWarehouse.Id, request.OutputKg, request.OutputMeter, _currentUser.UserName,
                pieceCount: null,
                notes: $"Stage transfer to ready goods ({nextStage.Name})"));

            // IN into the ready dimension (RawMessageId = null, ProductionOrderId
            // set) - the same ledger shape ReadyGoods balances are read from.
            _db.InventoryTransactions.Add(new InventoryTransaction(
                DocumentType.ReadyGoodsTransfer, order.OrderNumber, order.Id,
                _clock.UtcNow, readyWarehouse.Id, order.CustomerId, order.ItemId,
                rawMessageId: null, productionOrderId: order.Id,
                quantityKg: request.OutputKg, quantityMeter: request.OutputMeter,
                direction: TransactionDirection.In, createdBy: _currentUser.UserName));

            order.Complete();
        }
        else
        {
            // Step 5: activate ONLY the chosen stage, baselined on this stage's output.
            order.ActivateNextStage(nextStage, request.OutputKg, request.OutputMeter, _currentUser.UserName);

            if (order.Status == ProductionOrderStatus.RawAllocated)
                order.MarkInProduction();
        }

        await _db.SaveChangesAsync(cancellationToken);
        return await GetProductionOrderByIdQueryHandler.LoadDtoAsync(_db, order.Id, cancellationToken);
    }
}

/// <summary>
/// Edits the open stage's output weight (spec section 16). No approval is needed -
/// this is the factory re-weighing - but the change is recorded, and the stage's
/// baseline is never touched, so the loss is always measured from the original.
/// </summary>
public record UpdateStageOutputCommand(
    Guid StageExecutionId, decimal? OutputKg, decimal? OutputMeter, string? Reason) : IRequest<ProductionOrderDto>;

public class UpdateStageOutputCommandHandler : IRequestHandler<UpdateStageOutputCommand, ProductionOrderDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IDateTime _clock;

    public UpdateStageOutputCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser, IDateTime clock)
    {
        _db = db; _currentUser = currentUser; _clock = clock;
    }

    public async Task<ProductionOrderDto> Handle(UpdateStageOutputCommand request, CancellationToken cancellationToken)
    {
        var stage = await _db.ProductionOrderStageExecutions
            .FirstOrDefaultAsync(s => s.Id == request.StageExecutionId, cancellationToken)
            ?? throw new NotFoundException("ProductionOrderStageExecution", request.StageExecutionId);

        var beforeKg = stage.OutputKg;
        var beforeMeter = stage.OutputMeter;

        stage.UpdateOutputWhileOpen(request.OutputKg, request.OutputMeter);

        _db.AuditLogEntries.Add(new AuditLogEntry(
            _clock.UtcNow, _currentUser.UserName, "Update", "ProductionOrderStageExecution",
            stage.Id.ToString(),
            JsonSerializer.Serialize(new { outputKg = beforeKg, outputMeter = beforeMeter }),
            JsonSerializer.Serialize(new { outputKg = request.OutputKg, outputMeter = request.OutputMeter }),
            ipAddress: null,
            reason: request.Reason));

        await _db.SaveChangesAsync(cancellationToken);
        return await GetProductionOrderByIdQueryHandler.LoadDtoAsync(_db, stage.ProductionOrderId, cancellationToken);
    }
}