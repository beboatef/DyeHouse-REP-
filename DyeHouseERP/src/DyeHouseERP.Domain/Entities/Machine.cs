using DyeHouseERP.Domain.Common;

namespace DyeHouseERP.Domain.Entities;

/// <summary>
/// Machine master data (final decision S-Dynamic-Machines). Machines are real,
/// configurable master data - never hard-coded, and never limited to a fixed
/// count. New machines can be added from inside the ERP with no source-code
/// change.
///
/// A machine is linked to one or more production stages through
/// <see cref="StageMachine"/>; a stage may have zero, one or many machines.
/// A deactivated machine is not offered for NEW execution, while the historical
/// references kept on completed stage executions stay intact.
///
/// Deliberately NO numbered "machine requirement" is baked into a stage: the
/// initial mapping (الجيت = 15, الرام التثبيت = 4, الرام التجهيز = 4,
/// المثبت المائي = 2, المثبت الحراري = 1) is seeded as ordinary master data and
/// can be changed by the user at any time.
/// </summary>
public class Machine : AuditableEntity
{
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public bool IsActive { get; private set; } = true;
    public string? Notes { get; private set; }

    private readonly List<StageMachine> _stageLinks = new();

    /// <summary>Stages this machine can be used on. Many-to-many with <see cref="ProductionStageDefinition"/>.</summary>
    public IReadOnlyCollection<StageMachine> StageLinks => _stageLinks.AsReadOnly();

    private Machine() { } // EF Core

    public Machine(string code, string name, string createdBy, string? notes = null, bool isActive = true)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentException("A machine requires a code.", nameof(code));
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("A machine requires a name.", nameof(name));

        Code = code.Trim();
        Name = name.Trim();
        IsActive = isActive;
        Notes = notes;
        CreatedBy = createdBy;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public void Update(string name, string? notes, string modifiedBy)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("A machine requires a name.", nameof(name));

        Name = name.Trim();
        Notes = notes;
        ModifiedBy = modifiedBy;
        ModifiedAtUtc = DateTime.UtcNow;
    }

    public void Activate(string modifiedBy) { IsActive = true; ModifiedBy = modifiedBy; ModifiedAtUtc = DateTime.UtcNow; }
    public void Deactivate(string modifiedBy) { IsActive = false; ModifiedBy = modifiedBy; ModifiedAtUtc = DateTime.UtcNow; }
}
