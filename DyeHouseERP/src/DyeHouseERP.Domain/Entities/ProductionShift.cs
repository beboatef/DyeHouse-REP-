using DyeHouseERP.Domain.Common;

namespace DyeHouseERP.Domain.Entities;

/// <summary>
/// Production EXECUTION shift master data (final decision S-Production-Execution-Shifts).
///
/// This is execution data only. It is deliberately NOT the HR employee shift
/// model: it has no attendance, no check-in/check-out, no start/end hours and no
/// rest-day rules, and it is never connected to payroll. It simply names the
/// shift a stage execution ran on.
///
/// The initial rows are "First / Second / Third", seeded as ordinary master
/// data. The factory can rename them, deactivate them, or add more - there is
/// no fixed three-shift limit in the model.
/// </summary>
public class ProductionShift : AuditableEntity
{
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public int Sequence { get; private set; }
    public bool IsActive { get; private set; } = true;

    private ProductionShift() { } // EF Core

    public ProductionShift(string code, string name, int sequence, string createdBy, bool isActive = true)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentException("A production shift requires a code.", nameof(code));
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("A production shift requires a name.", nameof(name));

        Code = code.Trim();
        Name = name.Trim();
        Sequence = sequence;
        IsActive = isActive;
        CreatedBy = createdBy;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public void Update(string name, int sequence, string modifiedBy)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("A production shift requires a name.", nameof(name));

        Name = name.Trim();
        Sequence = sequence;
        ModifiedBy = modifiedBy;
        ModifiedAtUtc = DateTime.UtcNow;
    }

    public void Activate(string modifiedBy) { IsActive = true; ModifiedBy = modifiedBy; ModifiedAtUtc = DateTime.UtcNow; }
    public void Deactivate(string modifiedBy) { IsActive = false; ModifiedBy = modifiedBy; ModifiedAtUtc = DateTime.UtcNow; }
}
