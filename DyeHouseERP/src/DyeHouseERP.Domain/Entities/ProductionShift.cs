using DyeHouseERP.Domain.Common;

namespace DyeHouseERP.Domain.Entities;

/// <summary>
/// Production EXECUTION shift master data (final decision S-Production-Execution-Shifts).
///
/// This is execution data only. It is deliberately NOT the HR employee shift
/// model: it has no attendance, no check-in/check-out, no clock-in times and no
/// rest-day rules, and it is never connected to payroll. It simply names the
/// shift a stage execution ran on, plus the shift's planned production hours
/// per day (used only as the available-time denominator of machine utilisation).
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
    /// <summary>Planned production hours per calendar day. KPI input for machine utilisation; editable master data, never a guess.</summary>
    public decimal HoursPerDay { get; private set; } = 8m;

    private ProductionShift() { } // EF Core

    public ProductionShift(string code, string name, int sequence, string createdBy, bool isActive = true, decimal hoursPerDay = 8m)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentException("A production shift requires a code.", nameof(code));
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("A production shift requires a name.", nameof(name));

        Code = code.Trim();
        Name = name.Trim();
        Sequence = sequence;
        IsActive = isActive;
        SetHoursPerDay(hoursPerDay);
        CreatedBy = createdBy;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public void Update(string name, int sequence, string modifiedBy, decimal? hoursPerDay = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("A production shift requires a name.", nameof(name));

        Name = name.Trim();
        Sequence = sequence;
        if (hoursPerDay.HasValue) SetHoursPerDay(hoursPerDay.Value);
        ModifiedBy = modifiedBy;
        ModifiedAtUtc = DateTime.UtcNow;
    }

    private void SetHoursPerDay(decimal hoursPerDay)
    {
        if (hoursPerDay <= 0m || hoursPerDay > 24m)
            throw new ArgumentException("Planned hours per day must be between 0 (exclusive) and 24.", nameof(hoursPerDay));
        HoursPerDay = hoursPerDay;
    }

    public void Activate(string modifiedBy) { IsActive = true; ModifiedBy = modifiedBy; ModifiedAtUtc = DateTime.UtcNow; }
    public void Deactivate(string modifiedBy) { IsActive = false; ModifiedBy = modifiedBy; ModifiedAtUtc = DateTime.UtcNow; }
}
