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

    public string? Operator { get; private set; }
    public string? Notes { get; private set; }
    public DateTime? StartedAtUtc { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }
    public bool ApprovalRequired { get; private set; }
    public string? ApprovedBy { get; private set; }
    public DateTime? ApprovedAtUtc { get; private set; }

    private ProductionOrderStageExecution() { } // EF Core

    internal ProductionOrderStageExecution(Guid productionOrderId, Guid stageDefinitionId, int sequence)
    {
        ProductionOrderId = productionOrderId;
        StageDefinitionId = stageDefinitionId;
        Sequence = sequence;
    }

    public void Start(string operatorName)
    {
        if (Status != StageExecutionStatus.Pending)
            throw new DomainException($"Stage is already {Status} and cannot be started again.");

        Status = StageExecutionStatus.InProgress;
        Operator = operatorName;
        StartedAtUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// Records the stage's recorded quantities and marks it complete. Whether
    /// input/output are required is enforced by the caller against the
    /// matching ProductionStageDefinition (RequiresInputQuantity /
    /// RequiresOutputQuantity) - the entity itself stays agnostic about
    /// which stage type it is.
    /// </summary>
    public void Complete(
        decimal? inputKg, decimal? inputMeter, decimal? outputKg, decimal? outputMeter,
        decimal? lossKg, decimal? lossMeter, decimal? separatesKg, decimal? separatesMeter,
        string? notes, bool approvalRequired, string? approvedBy)
    {
        if (Status == StageExecutionStatus.Completed)
            throw new DomainException("Stage is already completed.");

        if (approvalRequired && string.IsNullOrWhiteSpace(approvedBy))
            throw new DomainException("This stage requires approval before it can be completed.");

        InputKg = inputKg; InputMeter = inputMeter;
        OutputKg = outputKg; OutputMeter = outputMeter;
        LossKg = lossKg; LossMeter = lossMeter;
        SeparatesKg = separatesKg; SeparatesMeter = separatesMeter;
        Notes = notes;
        ApprovalRequired = approvalRequired;
        ApprovedBy = approvedBy;
        ApprovedAtUtc = approvalRequired ? DateTime.UtcNow : null;

        Status = StageExecutionStatus.Completed;
        CompletedAtUtc = DateTime.UtcNow;
    }

    public void Skip(bool stageAllowsSkip, string reason, string modifiedBy)
    {
        if (!stageAllowsSkip)
            throw new DomainException("This stage is not configured to allow being skipped.");

        Status = StageExecutionStatus.Skipped;
        Notes = string.IsNullOrWhiteSpace(Notes) ? $"[Skipped by {modifiedBy}] {reason}" : $"{Notes}\n[Skipped by {modifiedBy}] {reason}";
        CompletedAtUtc = DateTime.UtcNow;
    }
}
