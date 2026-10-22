using System.ComponentModel.DataAnnotations;
using DyeHouseERP.Domain.Common;
using DyeHouseERP.Domain.Enums;
using DyeHouseERP.Domain.Exceptions;

namespace DyeHouseERP.Domain.Entities;

/// <summary>
/// One stage's execution within one Production Order (spec sections 19-22).
/// Input/output/loss/separates are all independently nullable KG/Meter pairs
/// - a stage records only the quantities that are actually relevant to it,
/// never a universal formula forced onto every stage type.
/// </summary>
public class ProductionOrderStageExecution : BaseEntity
{
    public Guid ProductionOrderId { get; private set; }
    public Guid StageDefinitionId { get; private set; }
    public int Sequence { get; private set; } // snapshotted from the stage definition at route-build time

    public StageExecutionStatus Status { get; private set; } = StageExecutionStatus.Pending;

    public decimal? InputKg { get; private set; }
    public decimal? InputMeter { get; private set; }
    public decimal? OutputKg { get; private set; }
    public decimal? OutputMeter { get; private set; }
    public decimal? LossKg { get; private set; }
    public decimal? LossMeter { get; private set; }
    public decimal? SeparatesKg { get; private set; }
    public decimal? SeparatesMeter { get; private set; }

    /// <summary>
    /// Second-grade / second-quality material produced by this operation (spec section 20).
    ///
    /// A quantity of its OWN: it is neither the output nor the waste, and it is never derived
    /// from either. KG and Meter are independent and are never converted into one another.
    /// </summary>
    public decimal? SecondGradeKg { get; private set; }
    public decimal? SecondGradeMeter { get; private set; }

    /// <summary>Piece count of second-grade output, recorded independently from bale count.</summary>
    public int? SecondGradePieceCount { get; private set; }

    /// <summary>
    /// The IMMUTABLE weight this stage started from (spec section 15).
    /// For the first stage it is the Job Order's actual weight; for every later
    /// stage it is the previous stage's final output. It is written once when the
    /// stage is activated and is NEVER overwritten - the output may be edited
    /// before transfer, but the baseline the loss is measured against stays put,
    /// so 783 baseline / 775 then 778 edited reads as a real 1.02% then 0.64%.
    /// </summary>
    public decimal? BaselineKg { get; private set; }
    public decimal? BaselineMeter { get; private set; }

    /// <summary>
    /// Loss as a percentage of the baseline, recorded when the stage is closed.
    /// Null while the stage is still open, because there is no final figure yet.
    /// </summary>
    public decimal? LossPercentKg { get; private set; }
    public decimal? LossPercentMeter { get; private set; }

    /// <summary>
    /// The machine this stage execution ran on, and the execution shift it ran in
    /// (S-Dynamic-Machines / S-Production-Execution-Shifts).
    ///
    /// These are HISTORICAL references, kept on the execution itself rather than derived
    /// from the stage's current machine mapping: a completed stage must still name the
    /// machine and shift it actually ran on even after that machine is re-mapped,
    /// deactivated or removed from the stage, so the production history is never rewritten.
    /// Optional because a stage may legitimately run without either being recorded.
    /// </summary>
    public Guid? MachineId { get; private set; }
    public Guid? ProductionShiftId { get; private set; }

    public bool RequiresDrawingNumber { get; private set; }
    public string? DrawingNumber { get; private set; }

    /// <summary>
    /// Sets - or CORRECTS - this execution's drawing number.
    ///
    /// A print run that has already been CLOSED may still be corrected, but only as an explicit
    /// correction carrying a reason: the drawing number is documentary data (delivery statements
    /// read it live from the executions), so a typo must be fixable, while a silent overwrite of the
    /// drawing a run actually produced must not be possible. The caller records the old and new value,
    /// the user, the time and the reason in the audit trail (UpdateStageDrawingCommand).
    /// A drawn number is never invented or inherited here, a skipped run keeps its own, and a stage
    /// that does not require a drawing number refuses one outright.
    /// </summary>
    public void SetDrawingNumber(string drawingNumber, string? correctionReason = null)
    {
        if (!RequiresDrawingNumber)
            throw new DomainException("This stage does not require a drawing number.");
        if (Status == StageExecutionStatus.Skipped)
            throw new DomainException("A skipped stage's drawing number cannot be changed.");
        if (Status == StageExecutionStatus.Completed && string.IsNullOrWhiteSpace(correctionReason))
            throw new DomainException(
                "Correcting the drawing number of a closed stage requires a reason - it is recorded in the audit trail. تصحيح رقم رسمة مرحلة منتهية يتطلب سببًا.");
        if (string.IsNullOrWhiteSpace(drawingNumber) || drawingNumber.Trim().Length > 100)
            throw new DomainException("Drawing number is required and must not exceed 100 characters. رقم الرسمة مطلوب بحد أقصى 100 حرف.");
        DrawingNumber = drawingNumber.Trim();
    }

    public void EnsureDrawingNumber()
    {
        if (RequiresDrawingNumber && string.IsNullOrWhiteSpace(DrawingNumber))
            throw new DomainException("Drawing number is required before printing. رقم الرسمة مطلوب قبل تنفيذ الطباعة.");
    }

    public string? Operator { get; private set; }
    public string? Notes { get; private set; }
    public DateTime? StartedAtUtc { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }

    /// <summary>
    /// H1 optimistic concurrency token, maintained by SQL Server. Quantities and
    /// status on a stage feed production output and separates, so two operators
    /// recording the same stage at once must not silently overwrite each other.
    /// </summary>
    [Timestamp]
    public byte[] RowVersion { get; private set; } = Array.Empty<byte>();

    private ProductionOrderStageExecution() { } // EF Core

    internal ProductionOrderStageExecution(Guid productionOrderId, Guid stageDefinitionId, int sequence, bool requiresDrawingNumber = false, string? drawingNumber = null)
    {
        RequiresDrawingNumber = requiresDrawingNumber;
        if (drawingNumber is not null) SetDrawingNumber(drawingNumber);
        ProductionOrderId = productionOrderId;
        StageDefinitionId = stageDefinitionId;
        Sequence = sequence;
    }

    /// <summary>
    /// Activates this stage with its starting weight (spec section 14). This is
    /// the ONLY moment a baseline is set, which is what makes it immutable
    /// afterwards: once the stage has run, its baseline is history.
    /// </summary>
    public void Activate(decimal? baselineKg, decimal? baselineMeter, string startedBy)
    {
        if (Status != StageExecutionStatus.Pending)
            throw new DomainException($"Stage is {Status} and cannot be activated.");

        EnsureDrawingNumber();
        BaselineKg = baselineKg;
        BaselineMeter = baselineMeter;
        Status = StageExecutionStatus.InProgress;
        Operator = startedBy;
        StartedAtUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// Closes the operation by recording what it actually consumed and produced, and deriving
    /// the waste from the immutable baseline (spec sections 13-15). Waste = baseline - output;
    /// waste % = waste / baseline x 100. KG and Meter are handled independently - they are never
    /// subtracted from, or converted into, one another, so no figure is ever invented.
    ///
    /// This is the ONE way an operation is closed: it is called by the transfer transaction, which
    /// simultaneously activates the next operation the user chose. The legacy "complete a stage"
    /// path was deleted in Phase 4.
    ///
    /// A completed stage is LOCKED: this throws once the stage is Completed, so
    /// an ordinary re-entry cannot rewrite a finished stage's figures.
    /// </summary>
    /// <param name="inputKg">The operation's actual input. Recorded, never inferred from the order's requested quantity.</param>
    /// <param name="secondGradeKg">Second-grade material produced by this operation - its own quantity, not output and not waste.</param>
    /// <param name="machineId">The machine this operation ran on (validated by the caller for suitability and availability).</param>
    /// <param name="productionShiftId">The execution shift this operation ran on.</param>
    public void CloseWithOutput(
        decimal? outputKg, decimal? outputMeter, decimal? separatesKg, decimal? separatesMeter,
        string? notes, string completedBy,
        decimal? inputKg = null, decimal? inputMeter = null,
        decimal? secondGradeKg = null, decimal? secondGradeMeter = null,
        Guid? machineId = null, Guid? productionShiftId = null, int? secondGradePieceCount = null)
    {
        if (Status == StageExecutionStatus.Completed)
            throw new DomainException(
                "This stage is already closed and locked. A finished stage is corrected with a compensating movement, never by re-entering its figures.");

        if (outputKg is null && outputMeter is null)
            throw new DomainException("Enter the final output quantity for this stage.");

        EnsureDrawingNumber();
        if (outputKg is < 0 || outputMeter is < 0 || separatesKg is < 0 || separatesMeter is < 0 ||
            inputKg is < 0 || inputMeter is < 0 || secondGradeKg is < 0 || secondGradeMeter is < 0 || secondGradePieceCount is < 0)
            throw new DomainException("Stage quantities cannot be negative.");
        InputKg = inputKg;
        InputMeter = inputMeter;
        OutputKg = outputKg;
        OutputMeter = outputMeter;
        SeparatesKg = separatesKg;
        SeparatesMeter = separatesMeter;
        SecondGradeKg = secondGradeKg;
        SecondGradeMeter = secondGradeMeter;
        SecondGradePieceCount = secondGradePieceCount;
        MachineId = machineId ?? MachineId;
        ProductionShiftId = productionShiftId ?? ProductionShiftId;
        Notes = notes;
        Operator = completedBy;

        LossKg = BaselineKg.HasValue && outputKg.HasValue ? BaselineKg - outputKg : null;
        LossMeter = BaselineMeter.HasValue && outputMeter.HasValue ? BaselineMeter - outputMeter : null;

        // Guard against a divide-by-zero on a zero baseline; a zero baseline
        // simply has no meaningful percentage and stays null.
        LossPercentKg = BaselineKg is > 0 && LossKg is not null
            ? Math.Round(LossKg.Value / BaselineKg.Value * 100, 2)
            : null;
        LossPercentMeter = BaselineMeter is > 0 && LossMeter is not null
            ? Math.Round(LossMeter.Value / BaselineMeter.Value * 100, 2)
            : null;

        Status = StageExecutionStatus.Completed;
        CompletedAtUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// Closes the operation as a ROUTE STEP ONLY: the Job Order moves on to the operation the user chose,
    /// and this one records no production figure at all - no output, no loss, no separates, no second grade.
    ///
    /// This is what makes "select the next operation" a routing decision rather than a production entry: a
    /// stage the floor simply passed through must not invent a weight, a waste percentage or a stock movement.
    /// Everything the operation already recorded earlier (an output edited while it was open, a drawing
    /// number, its machine and shift) is LEFT EXACTLY AS IT IS - this never clears a recorded figure - and the
    /// next operation inherits the material as this one received it (the caller passes this stage's baseline).
    ///
    /// Guessing an output here would fabricate production and, at the ready-goods stop, stock: that is why the
    /// transfer into the Ready Goods warehouse refuses to use this route and demands the final weights instead.
    /// </summary>
    public void CloseAsRouteStep(string? notes, string closedBy)
    {
        if (Status == StageExecutionStatus.Completed)
            throw new DomainException(
                "This stage is already closed and locked. A finished stage is corrected with a compensating movement, never by re-entering its figures.");

        // A printing operation must still name its drawing before it can be left, exactly as it must when
        // it records an output: the number identifies the run, not how much it produced.
        EnsureDrawingNumber();

        // Loss stays derived and therefore stays NULL: there is no final output to measure it against, and
        // a zero would read as "nothing was lost" instead of "nothing was recorded".
        Notes = notes;
        Operator = closedBy;
        Status = StageExecutionStatus.Completed;
        CompletedAtUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// The operation's WASTE (الهالك) - the figure the LAST operation before the Ready Goods warehouse is
    /// required to produce, derived exactly as the floor states it:
    ///
    ///     waste = original raw (KG) - ready weight (KG) - separates (KG)
    ///
    /// It is a WEIGHT, so it is computed in KILOGRAMS ONLY. The metre figure of the same operation is
    /// deliberately NOT turned into a "metre waste": a metre is a length, not a weight, and there is no
    /// conversion between the two anywhere in this system. Separates enter the equation only when they
    /// themselves were recorded in KG - if they were recorded in metres they stay a metre quantity and
    /// change nothing here (see <see cref="SeparatesKg"/>).
    ///
    /// Null (never a fabricated zero) while the KG input or output was not recorded, and null when the
    /// subtraction would go negative - produced material that outweighs its raw plus what was kept aside
    /// means the figures overlap or the raw figure is understated, which is a data problem and not a waste
    /// figure. Second grade is NOT subtracted, because it is a quantity of its own and is reported in its
    /// own column.
    /// </summary>
    public decimal? WasteKg
    {
        get
        {
            if (InputKg is null || OutputKg is null) return null;

            var waste = InputKg.Value - OutputKg.Value - (SeparatesKg ?? 0m);
            return waste < 0m ? null : waste;
        }
    }

    /// <summary>
    /// Guards the FINAL capture on the last operation before the Ready Goods warehouse: the waste may not be
    /// negative, which is the caller's check that "أصل الخام" (KG) is not smaller than the ready weight plus
    /// what was kept aside. The check is in KILOGRAMS, because the waste is a weight (see
    /// <see cref="WasteKg"/>): the metre figures the same operation may also record are a length, they form
    /// no part of the waste equation, and they therefore impose nothing here.
    ///
    /// Called by the transfer that moves the output into Ready Goods, where the original raw and ready
    /// weight are required in the first place.
    /// </summary>
    public void EnsureWasteIsPossible(decimal? rawKg, decimal? producedKg, decimal? separatesKg)
    {
        if (rawKg is > 0 && producedKg is > 0 && rawKg.Value - producedKg.Value - (separatesKg ?? 0m) < 0m)
            throw new DomainException(
                $"The original raw weight ({rawKg} KG) cannot be smaller than the ready weight ({producedKg} KG) plus the separates ({separatesKg ?? 0m} KG). " +
                "وزن أصل الخام لا يمكن أن يقل عن وزن الجاهز مضافًا إليه المنفصلات.");
    }

    /// <summary>
    /// Edits the stage's output WHILE it is still open (spec section 16). No
    /// approval is needed for this, but the baseline is untouched and the caller
    /// records the previous/new figures in the audit log. Once the stage is
    /// closed this is refused, which is what makes a completed stage locked.
    /// </summary>
    public void UpdateOutputWhileOpen(decimal? outputKg, decimal? outputMeter)
    {
        if (Status == StageExecutionStatus.Completed)
            throw new DomainException(
                "This stage is closed and locked; its output can no longer be edited.");
        if (outputKg is null && outputMeter is null)
            throw new DomainException("Enter the output quantity for this stage.");
        if (outputKg is < 0 || outputMeter is < 0)
            throw new DomainException("A stage output cannot be negative.");

        OutputKg = outputKg;
        OutputMeter = outputMeter;
    }

    /// <summary>
    /// Guards the ready-goods transfers linked to this stage (spec section 23): the
    /// quantities posted against a stage may never together exceed the stage's own
    /// recorded output, and a unit the stage never recorded can never be transferred.
    /// KG and Meter are checked independently - filling one says nothing about the other.
    /// </summary>
    public void EnsureTransfersWithinOutput(
        decimal alreadyTransferredKg, decimal alreadyTransferredMeter,
        decimal? transferKg, decimal? transferMeter)
    {
        if (Status != StageExecutionStatus.Completed || (OutputKg is null && OutputMeter is null))
            throw new DomainException(
                "This stage has not been closed, so it has no output to transfer to ready goods.");

        if (transferKg is > 0 && OutputKg is null)
            throw new DomainException(
                "This stage recorded no KG output, so KG cannot be transferred against it.");
        if (transferMeter is > 0 && OutputMeter is null)
            throw new DomainException(
                "This stage recorded no Meter output, so Meter cannot be transferred against it.");

        if (transferKg is > 0 && alreadyTransferredKg + transferKg.Value > OutputKg!.Value + 0.001m)
            throw new DomainException(
                $"Cumulative transfers can never exceed a stage's output: this stage produced {OutputKg} KG, {alreadyTransferredKg} KG has already been transferred and {transferKg} KG more was requested.");
        if (transferMeter is > 0 && alreadyTransferredMeter + transferMeter.Value > OutputMeter!.Value + 0.001m)
            throw new DomainException(
                $"Cumulative transfers can never exceed a stage's output: this stage produced {OutputMeter} Meter, {alreadyTransferredMeter} Meter has already been transferred and {transferMeter} Meter more was requested.");
    }

    /// <summary>
    /// Guards transfers by the independent output category recorded on this stage.
    /// First grade uses Output, second grade uses SecondGrade, and separates use Separates;
    /// waste is a production loss and can never be posted as deliverable stock.
    /// </summary>
    public void EnsureCategoryTransfersWithinOutput(
        ReadyGoodsCategory category,
        decimal alreadyTransferredKg, decimal alreadyTransferredMeter,
        decimal? transferKg, decimal? transferMeter)
    {
        if (Status != StageExecutionStatus.Completed)
            throw new DomainException("This stage has not been closed, so it has no output to transfer to ready goods.");

        var (availableKg, availableMeter, categoryName) = category switch
        {
            ReadyGoodsCategory.FirstGrade => (OutputKg, OutputMeter, "first-grade output"),
            ReadyGoodsCategory.SecondGrade => (SecondGradeKg, SecondGradeMeter, "second-grade output"),
            ReadyGoodsCategory.Separates => (SeparatesKg, SeparatesMeter, "separates output"),
            _ => throw new DomainException("Waste is a production loss and cannot be transferred to deliverable ready goods.")
        };

        if (availableKg is null && availableMeter is null)
            throw new DomainException($"This stage recorded no {categoryName}, so it cannot be transferred.");

        if (transferKg is > 0 && availableKg is null)
            throw new DomainException($"This stage recorded no KG {categoryName}, so KG cannot be transferred against it.");
        if (transferMeter is > 0 && availableMeter is null)
            throw new DomainException($"This stage recorded no Meter {categoryName}, so Meter cannot be transferred against it.");

        if (transferKg is > 0 && alreadyTransferredKg + transferKg.Value > availableKg!.Value + 0.001m)
            throw new DomainException($"Cumulative transfers can never exceed a stage's output for {categoryName}: requested KG exceeds the recorded category output.");
        if (transferMeter is > 0 && alreadyTransferredMeter + transferMeter.Value > availableMeter!.Value + 0.001m)
            throw new DomainException($"Cumulative transfers can never exceed a stage's output for {categoryName}: requested Meter exceeds the recorded category output.");
    }

    // Phase 4: Start(...), Complete(...) and Skip(...) were DELETED together with their endpoints.
    // An operation can only be closed through CloseWithOutput above, which the transfer transaction
    // calls at the same moment it activates the operation the user chose next. There is therefore no
    // domain route any more that advances or closes an operation outside that single transaction, and
    // no route at all that marks one Skipped.
}
