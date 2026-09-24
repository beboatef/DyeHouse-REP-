using DyeHouseERP.Domain.Common;

namespace DyeHouseERP.Domain.Entities;

/// <summary>
/// Configurable production stage definition (spec section 19). The system
/// deliberately does NOT hard-code stage names like "preparation/dyeing/
/// washing/drying/finishing" - administrators define whatever sequence
/// actually matches the factory's process. A Production Order's stage
/// executions are generated from whichever definitions are Active, in
/// Sequence order, at the moment the order is created.
/// </summary>
public class ProductionStageDefinition : AuditableEntity
{
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public int Sequence { get; private set; }
    public bool IsActive { get; private set; } = true;

    public bool RequiresInputQuantity { get; private set; }
    public bool RequiresOutputQuantity { get; private set; }
    public bool RequiresApproval { get; private set; }
    public bool AllowSkip { get; private set; }
    public bool AllowRepeat { get; private set; }
    public bool AllowRework { get; private set; }
    public bool AllowReturn { get; private set; }
    public string? Notes { get; private set; }

    private ProductionStageDefinition() { } // EF Core

    public ProductionStageDefinition(
        string code, string name, int sequence, string createdBy,
        bool requiresInputQuantity = true, bool requiresOutputQuantity = true,
        bool requiresApproval = false, bool allowSkip = false, bool allowRepeat = false,
        bool allowRework = true, bool allowReturn = false, string? notes = null)
    {
        Code = code.Trim();
        Name = name.Trim();
        Sequence = sequence;
        RequiresInputQuantity = requiresInputQuantity;
        RequiresOutputQuantity = requiresOutputQuantity;
        RequiresApproval = requiresApproval;
        AllowSkip = allowSkip;
        AllowRepeat = allowRepeat;
        AllowRework = allowRework;
        AllowReturn = allowReturn;
        Notes = notes;
        CreatedBy = createdBy;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public void Deactivate(string modifiedBy) { IsActive = false; ModifiedBy = modifiedBy; ModifiedAtUtc = DateTime.UtcNow; }
    public void Activate(string modifiedBy) { IsActive = true; ModifiedBy = modifiedBy; ModifiedAtUtc = DateTime.UtcNow; }

    public void Reorder(int sequence, string modifiedBy)
    {
        Sequence = sequence;
        ModifiedBy = modifiedBy;
        ModifiedAtUtc = DateTime.UtcNow;
    }
}
