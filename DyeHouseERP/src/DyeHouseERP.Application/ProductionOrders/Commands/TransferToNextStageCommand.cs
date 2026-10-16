using System.Text.Json;
using DyeHouseERP.Application.Common.Exceptions;
using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.Common.Services;
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
    string? Notes, string? DrawingNumber = null,
    // The ACTUAL input the operation consumed. Recorded as entered - never inferred from the
    // order's requested quantity and never derived from the output (spec section 20).
    decimal? InputKg = null, decimal? InputMeter = null,
    // Second-grade material: a quantity of its own - not output, not waste.
    decimal? SecondGradeKg = null, decimal? SecondGradeMeter = null,
    // Where the operation ran. Validated for SUITABILITY (the machine must be mapped to this stage)
    // and AVAILABILITY (machine and shift must be active) before anything is written.
    Guid? MachineId = null, Guid? ProductionShiftId = null) : IRequest<ProductionOrderDto>;

public class TransferToNextStageCommandValidator : AbstractValidator<TransferToNextStageCommand>
{
    public TransferToNextStageCommandValidator()
    {
        RuleFor(x => x.StageExecutionId).NotEmpty();
        RuleFor(x => x.DrawingNumber).MaximumLength(100);
        RuleFor(x => x.NextStageDefinitionId).NotEmpty();
        // The output is deliberately NOT required by the validator any more: choosing the operation the job
        // moves to is a ROUTE step, and a route step records no production figure at all (see the handler).
        // It IS required where production is genuinely recorded - closing an operation with figures, and the
        // final capture that moves the output into the Ready Goods warehouse - and the handler enforces both,
        // because only it knows which operation was chosen and what that operation's item is configured for.
        RuleFor(x => x.OutputKg).GreaterThanOrEqualTo(0).When(x => x.OutputKg.HasValue);
        RuleFor(x => x.OutputMeter).GreaterThanOrEqualTo(0).When(x => x.OutputMeter.HasValue);
        RuleFor(x => x.InputKg).GreaterThanOrEqualTo(0).When(x => x.InputKg.HasValue);
        RuleFor(x => x.InputMeter).GreaterThanOrEqualTo(0).When(x => x.InputMeter.HasValue);
        RuleFor(x => x.SeparatesKg).GreaterThanOrEqualTo(0).When(x => x.SeparatesKg.HasValue);
        RuleFor(x => x.SeparatesMeter).GreaterThanOrEqualTo(0).When(x => x.SeparatesMeter.HasValue);
        RuleFor(x => x.SecondGradeKg).GreaterThanOrEqualTo(0).When(x => x.SecondGradeKg.HasValue);
        RuleFor(x => x.SecondGradeMeter).GreaterThanOrEqualTo(0).When(x => x.SecondGradeMeter.HasValue);
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

    /// <summary>
    /// Validates the machine the operation says it ran on (spec section 20): the machine must be
    /// MAPPED TO THE CLOSING OPERATION (suitability - a machine mapped to another stage cannot be
    /// reported as having run this one) and it must still be ACTIVE (availability - a retired machine
    /// cannot receive a new execution, while the history of the ones already recorded stays intact).
    /// The shift must exist and be active. Neither is required: a stage may legitimately run without
    /// either being recorded.
    /// </summary>
    private async Task EnsureMachineAndShiftAreUsableAsync(
        TransferToNextStageCommand request, Guid stageDefinitionId, CancellationToken cancellationToken)
    {
        if (request.MachineId.HasValue)
        {
            var machine = await _db.Machines.AsNoTracking()
                .FirstOrDefaultAsync(m => m.Id == request.MachineId.Value, cancellationToken)
                ?? throw new NotFoundException("Machine", request.MachineId.Value);

            if (!machine.IsActive)
                throw new DomainException(
                    $"Machine '{machine.Name}' is inactive and cannot be recorded as running an operation. ماكينة موقوفة لا يمكن تسجيل تشغيلها.");

            var isMappedToStage = await _db.StageMachines.AsNoTracking()
                .AnyAsync(l => l.MachineId == machine.Id && l.StageDefinitionId == stageDefinitionId, cancellationToken);
            if (!isMappedToStage)
                throw new DomainException(
                    $"Machine '{machine.Name}' is not mapped to this operation, so it cannot be recorded as the machine it ran on. " +
                    "الماكينة غير مرتبطة بهذه المرحلة.");
        }

        if (request.ProductionShiftId.HasValue)
        {
            var shift = await _db.ProductionShifts.AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == request.ProductionShiftId.Value, cancellationToken)
                ?? throw new NotFoundException("ProductionShift", request.ProductionShiftId.Value);

            if (!shift.IsActive)
                throw new DomainException(
                    $"Production shift '{shift.Name}' is inactive and cannot be recorded. وردية موقوفة لا يمكن تسجيلها.");
        }
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
        // A Job Order may take a PRINTING stage more than once: one execution per drawing, each with
        // its own drawing number and its own recorded quantities (spec sections 19-22). The ability is
        // derived from the stage's OWN semantics - a stage that requires a drawing number - so no new
        // capability flag is introduced, and it stays the same transfer workflow every other stage uses:
        // the closed run is recorded and locked, and the next print run is activated with the output it
        // just produced as its baseline. Every other stage keeps the original "choose a different next
        // stage" rule, so no repeat/return/rework route is invented here.
        var isAdditionalPrintRun = nextStage.Id == stage.StageDefinitionId;
        if (isAdditionalPrintRun && !nextStage.RequiresDrawingNumber)
            throw new DomainException(
                $"'{nextStage.Name}' is the stage this Job Order is already on. Choose a different next stage.");

        // A paused order has released its unused material; transferring it now
        // would post output for a job that is not running.
        if (order.Status == ProductionOrderStatus.Paused)
            throw new DomainException(
                $"Production order {order.OrderNumber} is paused. Resume it before transferring a stage.");

        // H1: the transfer is where production output is recorded, so the 100% rule is
        // checked BEFORE anything is written. This single call covers both branches:
        // activating the next stage, and the ready-goods branch that posts the ready
        // document + IN movement and completes the order (spec section 17).
        await RawAllocationGuard.EnsureCompleteAsync(_db, order, cancellationToken);

        // ---- Is this a route step, a recording close, or the FINAL capture? --------------
        // Choosing the operation the job moves to is a ROUTE decision: the stage is closed with no
        // production figure and NOTHING is posted - no output, no loss, no separates, no stock movement
        // (spec: "selecting an operation records no production and no inventory movement"). Production is
        // recorded when the operation is closed WITH figures, and the one place figures are mandatory is the
        // LAST operation before the Ready Goods warehouse, whose capture is what posts ready-goods stock.
        var nextIsReadyGoods = nextStage.IsReadyGoodsStage;
        var hasReadyWeight = request.OutputKg is > 0 || request.OutputMeter is > 0;
        var hasOtherFigures = request.InputKg is > 0 || request.InputMeter is > 0
            || request.SeparatesKg is > 0 || request.SeparatesMeter is > 0
            || request.SecondGradeKg is > 0 || request.SecondGradeMeter is > 0;
        var isRouteStep = !nextIsReadyGoods && !hasReadyWeight;

        if (isRouteStep && hasOtherFigures)
            throw new DomainException(
                "This transfer records no production: pass the operation's OUTPUT (the ready weight) together with " +
                "its input, separates and second grade, or pass none of them to simply move the job on. " +
                "لتسجيل بيانات التشغيل أدخل وزن الخارج مع باقي القيم، أو اتركها كلها للانتقال إلى المرحلة التالية فقط.");

        if (nextIsReadyGoods)
        {
            // The FINAL CAPTURE on the last operation before the Ready Goods warehouse (see the class
            // documentation): the ready weight and the original raw weight are BOTH required, because the
            // waste is derived from them - and separates stay OPTIONAL, so an operation with none completes
            // without inventing a zero.
            if (!hasReadyWeight)
                throw new DomainException(
                    "Enter the ready weight of this last operation before the Ready Goods warehouse " +
                    "(وزن الجاهز بالكيلوجرام) - it is what the ready-goods stock is posted from. " +
                    "لا يمكن إدخال الجاهز إلى المخزن دون تسجيل وزنه.");

            if (request.InputKg is not > 0 && request.InputMeter is not > 0)
                throw new DomainException(
                    "Enter the ORIGINAL RAW weight this last operation started from (أصل الخام): the waste is " +
                    "calculated as original raw - ready weight - separates. أصل الخام مطلوب في المرحلة الأخيرة قبل مخزن الجاهز.");
        }

        // Spec sections 5-6: any recorded figure may only use the units configured on the order's item, with
        // both required when the item asks for both. Never converted, never inferred. A route step records
        // nothing at all, so it has nothing to validate (and must not be asked for a quantity it does not have).
        if (!isRouteStep)
        {
            await ItemUnitGuard.EnsureValidQuantitiesAsync(
                _db, order.ItemId, request.OutputKg, request.OutputMeter,
                cancellationToken, "as this stage's output");

            // The recorded input and second-grade quantity obey the SAME unit rules: an operation may only
            // report the units its item is configured for. That is what keeps a waste figure from ever being
            // computed between incompatible units - the KG waste is a KG subtraction and the Meter waste is a
            // Meter subtraction, and neither is converted into the other (spec sections 20-21).
            await ItemUnitGuard.EnsureValidQuantitiesAsync(
                _db, order.ItemId, request.InputKg, request.InputMeter,
                cancellationToken, "as this operation's actual input", requireAnyQuantity: false);
            await ItemUnitGuard.EnsureValidQuantitiesAsync(
                _db, order.ItemId, request.SecondGradeKg, request.SecondGradeMeter,
                cancellationToken, "as this operation's second-grade quantity", requireAnyQuantity: false);
        }

        await ItemUnitGuard.EnsureValidQuantitiesAsync(
            _db, order.ItemId, request.SeparatesKg, request.SeparatesMeter,
            cancellationToken, "as separates", requireAnyQuantity: false);

        // Machine suitability/availability and the execution shift.
        await EnsureMachineAndShiftAreUsableAsync(request, stage.StageDefinitionId, cancellationToken);

        stage.EnsureDrawingNumber();
        var drawingNumber = request.DrawingNumber;

        // A further print run of the SAME stage is a NEW drawing: it must name its own number, and it
        // must never silently inherit the previous run's, so the carry-forward below is skipped for it.
        if (isAdditionalPrintRun && string.IsNullOrWhiteSpace(drawingNumber))
            throw new DomainException(
                "Each printing run has its own drawing number. رقم الرسمة مطلوب لكل تشغيل طباعة.");

        // Revisiting the same operation LATER in the route retains this order's recorded drawing unless
        // the operator explicitly supplies another. Never borrow another order's number.
        if (nextStage.RequiresDrawingNumber && !isAdditionalPrintRun && drawingNumber is null)
            drawingNumber = await _db.ProductionOrderStageExecutions.AsNoTracking()
                .Where(s => s.ProductionOrderId == order.Id && s.StageDefinitionId == nextStage.Id && s.DrawingNumber != null)
                .OrderByDescending(s => s.StartedAtUtc).Select(s => s.DrawingNumber).FirstOrDefaultAsync(cancellationToken);
        if (nextStage.RequiresDrawingNumber &&
            (string.IsNullOrWhiteSpace(drawingNumber) || drawingNumber.Trim().Length > 100))
            throw new DomainException("Drawing number is required before printing. رقم الرسمة مطلوب قبل تنفيذ الطباعة.");

        var previousOutputKg = stage.OutputKg;
        var previousOutputMeter = stage.OutputMeter;

        if (isRouteStep)
        {
            // A route step: close the operation without inventing a single figure. The material is unchanged,
            // so the chosen operation starts from THIS operation's own baseline.
            stage.CloseAsRouteStep(request.Notes, _currentUser.UserName);
        }
        else
        {
            // The waste the floor states for the last operation before the ready-goods warehouse is
            // "original raw - ready weight - separates", checked per unit BEFORE anything is written, so an
            // impossible figure is refused rather than stored (separates are optional and simply count as
            // zero when they were not recorded).
            if (nextIsReadyGoods)
            {
                // The waste is a WEIGHT and is therefore checked in KILOGRAMS only. The metre figures
                // recorded on the same operation stay a length: they are never converted into a weight
                // and they form no part of the waste equation, so they are not passed here at all.
                stage.EnsureWasteIsPossible(
                    request.InputKg, request.OutputKg, request.SeparatesKg);
            }

            // Steps 1-4: close, record what the operation consumed and produced (input, output, separates,
            // second-grade), derive the waste from the immutable baseline, and lock. The machine and shift
            // this operation ran on are recorded on the SAME row, as historical references.
            stage.CloseWithOutput(request.OutputKg, request.OutputMeter,
                request.SeparatesKg, request.SeparatesMeter, request.Notes, _currentUser.UserName,
                inputKg: request.InputKg, inputMeter: request.InputMeter,
                secondGradeKg: request.SecondGradeKg, secondGradeMeter: request.SecondGradeMeter,
                machineId: request.MachineId, productionShiftId: request.ProductionShiftId);
        }

        // Spec sections 22-23: the separates quantity a closing operation records ENTERS the separates
        // register here, in the same transaction. A Separate is the record the register, the reprocessing
        // decision and the scrap decision all read from, so writing the figure on the stage row alone
        // would leave recorded material untracked (and the register permanently empty). It is created
        // ONLY when a quantity was actually recorded, and - exactly like a new stage execution or a raw
        // allocation - it is Added explicitly, because EF change detection would otherwise treat the
        // client-generated key as an existing row and try to UPDATE a row that was never inserted.
        if (request.SeparatesKg is > 0 || request.SeparatesMeter is > 0)
            _db.Separates.Add(new Separate(
                order.Id, stage.Id, order.CustomerId, order.ItemId,
                request.SeparatesKg, request.SeparatesMeter, _currentUser.UserName,
                notes: request.Notes));

        // Audit: the figures the stage actually ended on and where they came
        // from, so a weight edited before transfer leaves a visible trail (spec 16).
        _db.AuditLogEntries.Add(new AuditLogEntry(
            _clock.UtcNow, _currentUser.UserName, "StageTransfer", "ProductionOrderStageExecution",
            stage.Id.ToString(),
            JsonSerializer.Serialize(new { outputKg = previousOutputKg, outputMeter = previousOutputMeter }),
            JsonSerializer.Serialize(new
            {
                inputKg = request.InputKg,
                inputMeter = request.InputMeter,
                outputKg = request.OutputKg,
                outputMeter = request.OutputMeter,
                baselineKg = stage.BaselineKg,
                lossKg = stage.LossKg,
                lossPercentKg = stage.LossPercentKg,
                separatesKg = request.SeparatesKg,
                separatesMeter = request.SeparatesMeter,
                secondGradeKg = request.SecondGradeKg,
                secondGradeMeter = request.SecondGradeMeter,
                machineId = request.MachineId,
                productionShiftId = request.ProductionShiftId,
                // A route step records no production figure, and the waste the LAST operation before the ready
                // goods warehouse derived (original raw KG - ready weight KG - separates KG) is captured with
                // it. The metre figure is a length and is deliberately not turned into a "metre waste".
                routeStep = isRouteStep,
                wasteKg = stage.WasteKg,
                nextStage = nextStage.Name
            }),
            ipAddress: null,
            reason: request.Notes));

        if (nextStage.IsReadyGoodsStage)
        {
            // Spec 17: the final stage moves the output to Ready Goods and completes
            // the order. The transfer IS the completion - there is no other button.
            await using var periodGuard = await _periodClose.BeginPostingAsync(_clock.UtcNow, cancellationToken);

            var readyWarehouse = await _db.Warehouses.AsNoTracking()
                .Where(w => w.Kind == WarehouseKind.ReadyGoods)
                .OrderBy(w => w.Id)
                .FirstOrDefaultAsync(cancellationToken)
                ?? throw new DomainException("No Ready Goods warehouse is configured.");

            // A retired warehouse never receives a new stock row: this branch posts the
            // ready-goods transfer AND its IN movement, so the destination must still be
            // active. The warehouse is resolved HERE (not supplied by the caller), so the
            // check is on the exact row this handler is about to write.
            await WarehouseGuard.EnsureUsableForNewMovementAsync(
                _db, readyWarehouse.Id, cancellationToken, "to post ready-goods stock into it");

            var transferNumber = await _numberGenerator.NextAsync(
                DocumentType.ReadyGoodsTransfer, cancellationToken: cancellationToken);

            // Spec section 23: the automatic transfer carries the same stage link a manual
            // one does - the quantity it posts IS this stage's recorded output.
            _db.ReadyGoodsTransfers.Add(new ReadyGoodsTransfer(
                transferNumber, _clock.UtcNow, order.Id, order.CustomerId, order.ItemId,
                readyWarehouse.Id, request.OutputKg, request.OutputMeter, _currentUser.UserName,
                pieceCount: null,
                notes: $"Stage transfer to ready goods ({nextStage.Name})",
                stageExecutionId: stage.Id));

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
            // Step 5: activate ONLY the chosen stage, baselined on this stage's output - or, on a route step
            // that recorded no output at all, on this stage's OWN baseline: the material was not weighed at
            // this stop, so it is exactly what the previous operation handed over, and inventing an output
            // here would fabricate production.
            var baselineKg = isRouteStep ? stage.BaselineKg : request.OutputKg;
            var baselineMeter = isRouteStep ? stage.BaselineMeter : request.OutputMeter;

            var activated = order.ActivateNextStage(
                nextStage, baselineKg, baselineMeter, _currentUser.UserName, drawingNumber);

            // Added explicitly, exactly like a resumed cycle's allocation: EF change detection finds an entity
            // whose client-generated key is already set and treats it as an EXISTING row, so
            // without this Add the new stage would be saved as an UPDATE against a row that
            // was never inserted and every stage transfer would fail on SaveChanges.
            _db.ProductionOrderStageExecutions.Add(activated);

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

        // H1: editing the open stage's output IS recording production, so the same gate
        // applies as for a transfer or a stage completion.
        var order = await _db.ProductionOrders.AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == stage.ProductionOrderId, cancellationToken)
            ?? throw new NotFoundException("ProductionOrder", stage.ProductionOrderId);
        await RawAllocationGuard.EnsureCompleteAsync(_db, order, cancellationToken);

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