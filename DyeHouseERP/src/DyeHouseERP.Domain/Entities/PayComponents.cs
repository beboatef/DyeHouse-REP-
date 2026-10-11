using DyeHouseERP.Domain.Common;
using DyeHouseERP.Domain.Enums;
using DyeHouseERP.Domain.Exceptions;

namespace DyeHouseERP.Domain.Entities;

/// <summary>
/// A configurable pay component (spec section 35): the definition of an allowance,
/// incentive, bonus, commission or deduction and HOW it is computed. This is the
/// whole point of the section - the factory's wage rules are master data, so no
/// factory-specific amount or rate is ever hard-coded in the calculation.
/// </summary>
public class PayComponent : AuditableEntity
{
    public string Code { get; private set; } = string.Empty;
    public string NameAr { get; private set; } = string.Empty;
    public string NameEn { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;

    public PayComponentKind Kind { get; private set; } = PayComponentKind.Allowance;
    public PayComponentCalculation Calculation { get; private set; } = PayComponentCalculation.FixedAmount;

    /// <summary>Used when Calculation is FixedAmount / PerAttendanceDay / PerOvertimeHour.</summary>
    public decimal DefaultAmount { get; private set; }

    /// <summary>Used when Calculation is PercentOfBasic / PercentOfGross.</summary>
    public decimal DefaultPercent { get; private set; }

    /// <summary>Whether the component is part of the taxable base (spec section 36).</summary>
    public bool IsTaxable { get; private set; } = true;

    /// <summary>Whether the component is part of the insured base (spec section 36).</summary>
    public bool IsInsured { get; private set; } = true;

    public int SortOrder { get; private set; }
    public string? Notes { get; private set; }
    public bool IsActive { get; private set; } = true;

    private PayComponent() { } // EF Core

    public PayComponent(string code, PayComponentKind kind, PayComponentCalculation calculation,
        string createdBy, string? nameAr = null, string? nameEn = null,
        decimal defaultAmount = 0m, decimal defaultPercent = 0m,
        bool isTaxable = true, bool isInsured = true, int sortOrder = 0, string? notes = null)
    {
        SetCode(code);
        SetNames(nameAr, nameEn);
        Kind = kind;
        Configure(kind, calculation, defaultAmount, defaultPercent, isTaxable, isInsured, sortOrder);
        Notes = Blank(notes);
        CreatedBy = createdBy;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public void SetCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new DomainException("A pay component requires a code.");
        Code = code.Trim();
    }

    public void SetNames(string? nameAr, string? nameEn)
    {
        if (string.IsNullOrWhiteSpace(nameAr) && string.IsNullOrWhiteSpace(nameEn))
            throw new DomainException("A pay component requires a name (Arabic and/or English).");
        NameAr = (nameAr ?? nameEn)!.Trim();
        NameEn = (nameEn ?? nameAr)!.Trim();
        Name = NameEn;
    }

    public void Configure(PayComponentKind kind, PayComponentCalculation calculation,
        decimal defaultAmount, decimal defaultPercent,
        bool isTaxable = true, bool isInsured = true, int sortOrder = 0)
    {
        if (defaultAmount < 0) throw new DomainException("A default amount cannot be negative.");
        if (defaultPercent < 0) throw new DomainException("A default percentage cannot be negative.");
        if (defaultPercent > 1000) throw new DomainException("A default percentage looks out of range.");

        Kind = kind;
        Calculation = calculation;
        DefaultAmount = defaultAmount;
        DefaultPercent = defaultPercent;
        IsTaxable = isTaxable;
        IsInsured = isInsured;
        SortOrder = sortOrder;
    }

    public bool IsDeduction => Kind == PayComponentKind.Deduction;

    public void SetNotes(string? notes) => Notes = Blank(notes);
    public void Activate(string modifiedBy) { IsActive = true; Touch(modifiedBy); }
    public void Deactivate(string modifiedBy) { IsActive = false; Touch(modifiedBy); }

    private void Touch(string modifiedBy) { ModifiedBy = modifiedBy; ModifiedAtUtc = DateTime.UtcNow; }
    private static string? Blank(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();
}

/// <summary>
/// Assigns a pay component to one employee (spec section 35), optionally
/// overriding the default amount/percentage and with an effective period, so a
/// raise or a temporary bonus is a dated assignment rather than an edit.
/// </summary>
public class EmployeePayComponent : AuditableEntity
{
    public Guid EmployeeId { get; private set; }
    public Guid PayComponentId { get; private set; }
    public decimal? Amount { get; private set; }
    public decimal? Percent { get; private set; }
    public DateTime EffectiveFrom { get; private set; }
    public DateTime? EffectiveTo { get; private set; }
    public bool IsActive { get; private set; } = true;

    private EmployeePayComponent() { } // EF Core

    public EmployeePayComponent(Guid employeeId, Guid payComponentId, DateTime effectiveFrom, string createdBy,
        decimal? amount = null, decimal? percent = null, DateTime? effectiveTo = null)
    {
        if (employeeId == Guid.Empty) throw new DomainException("A pay component assignment must name the employee.");
        if (payComponentId == Guid.Empty) throw new DomainException("A pay component assignment must name the component.");
        if (amount.HasValue && amount.Value < 0) throw new DomainException("An assignment amount cannot be negative.");
        if (percent.HasValue && (percent.Value < 0 || percent.Value > 1000))
            throw new DomainException("An assignment percentage looks out of range.");
        if (effectiveTo.HasValue && effectiveTo.Value.Date < effectiveFrom.Date)
            throw new DomainException("The effective end date cannot precede the start date.");

        EmployeeId = employeeId;
        PayComponentId = payComponentId;
        EffectiveFrom = effectiveFrom.Date;
        EffectiveTo = effectiveTo?.Date;
        Amount = amount;
        Percent = percent;
        CreatedBy = createdBy;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public void Update(decimal? amount, decimal? percent, DateTime effectiveFrom, DateTime? effectiveTo, string modifiedBy)
    {
        if (amount.HasValue && amount.Value < 0) throw new DomainException("An assignment amount cannot be negative.");
        if (percent.HasValue && (percent.Value < 0 || percent.Value > 1000))
            throw new DomainException("An assignment percentage looks out of range.");
        if (effectiveTo.HasValue && effectiveTo.Value.Date < effectiveFrom.Date)
            throw new DomainException("The effective end date cannot precede the start date.");

        Amount = amount;
        Percent = percent;
        EffectiveFrom = effectiveFrom.Date;
        EffectiveTo = effectiveTo?.Date;
        Touch(modifiedBy);
    }

    public bool IsEffectiveOn(DateTime date)
        => IsActive && date.Date >= EffectiveFrom && (!EffectiveTo.HasValue || date.Date <= EffectiveTo.Value);

    public void Deactivate(string modifiedBy) { IsActive = false; Touch(modifiedBy); }
    public void Activate(string modifiedBy) { IsActive = true; Touch(modifiedBy); }

    private void Touch(string modifiedBy) { ModifiedBy = modifiedBy; ModifiedAtUtc = DateTime.UtcNow; }
}

/// <summary>
/// A configurable tax or insurance rule (spec section 36): kind, base and the
/// employee/employer percentages, with an optional insurable-salary floor/ceiling.
/// Rates are never hard-coded - a new statutory rate is a new row.
/// </summary>
public class ContributionRule : AuditableEntity
{
    public string Code { get; private set; } = string.Empty;
    public string NameAr { get; private set; } = string.Empty;
    public string NameEn { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;

    public ContributionKind Kind { get; private set; } = ContributionKind.Tax;
    public ContributionBase Base { get; private set; } = ContributionBase.BasicSalary;

    public decimal EmployeeRatePercent { get; private set; }
    public decimal EmployerRatePercent { get; private set; }

    /// <summary>Minimum base the contribution is computed on (insurable floor). Null = no floor.</summary>
    public decimal? MinBase { get; private set; }
    /// <summary>Maximum base the contribution is computed on (insurable ceiling). Null = no ceiling.</summary>
    public decimal? MaxBase { get; private set; }

    public bool IsActive { get; private set; } = true;

    private ContributionRule() { } // EF Core

    public ContributionRule(string code, ContributionKind kind, ContributionBase basis,
        decimal employeeRatePercent, decimal employerRatePercent, string createdBy,
        string? nameAr = null, string? nameEn = null, decimal? minBase = null, decimal? maxBase = null)
    {
        SetCode(code);
        SetNames(nameAr, nameEn);
        Configure(kind, basis, employeeRatePercent, employerRatePercent, minBase, maxBase);
        CreatedBy = createdBy;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public void SetCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new DomainException("A contribution rule requires a code.");
        Code = code.Trim();
    }

    public void SetNames(string? nameAr, string? nameEn)
    {
        if (string.IsNullOrWhiteSpace(nameAr) && string.IsNullOrWhiteSpace(nameEn))
            throw new DomainException("A contribution rule requires a name (Arabic and/or English).");
        NameAr = (nameAr ?? nameEn)!.Trim();
        NameEn = (nameEn ?? nameAr)!.Trim();
        Name = NameEn;
    }

    public void Configure(ContributionKind kind, ContributionBase basis,
        decimal employeeRatePercent, decimal employerRatePercent, decimal? minBase, decimal? maxBase)
    {
        if (employeeRatePercent < 0 || employerRatePercent < 0)
            throw new DomainException("Contribution rates cannot be negative.");
        if (employeeRatePercent > 100 || employerRatePercent > 100)
            throw new DomainException("Contribution rates cannot exceed 100%.");
        if (minBase.HasValue && minBase.Value < 0) throw new DomainException("The minimum base cannot be negative.");
        if (maxBase.HasValue && minBase.HasValue && maxBase.Value < minBase.Value)
            throw new DomainException("The maximum base cannot be below the minimum base.");

        Kind = kind;
        Base = basis;
        EmployeeRatePercent = employeeRatePercent;
        EmployerRatePercent = employerRatePercent;
        MinBase = minBase;
        MaxBase = maxBase;
    }

    /// <summary>Applies the insurable floor/ceiling to a raw base amount.</summary>
    public decimal InsurableBase(decimal rawBase)
    {
        var value = Math.Max(0, rawBase);
        if (MinBase.HasValue) value = Math.Max(value, MinBase.Value);
        if (MaxBase.HasValue) value = Math.Min(value, MaxBase.Value);
        return value;
    }

    public decimal EmployeeContribution(decimal rawBase)
        => Math.Round(InsurableBase(rawBase) * EmployeeRatePercent / 100m, 4, MidpointRounding.AwayFromZero);

    public decimal EmployerContribution(decimal rawBase)
        => Math.Round(InsurableBase(rawBase) * EmployerRatePercent / 100m, 4, MidpointRounding.AwayFromZero);

    public void Activate(string modifiedBy) { IsActive = true; Touch(modifiedBy); }
    public void Deactivate(string modifiedBy) { IsActive = false; Touch(modifiedBy); }

    private void Touch(string modifiedBy) { ModifiedBy = modifiedBy; ModifiedAtUtc = DateTime.UtcNow; }
}

/// <summary>
/// Maps a payroll amount to an accounting account (spec section 37). The system has
/// no general ledger of its own, so this is the configurable bridge used by the
/// accounting export and the labor-cost report - never a hard-coded account number.
/// </summary>
public class PayrollAccountMapping : AuditableEntity
{
    public PayrollAccountKind Kind { get; private set; }
    public string AccountCode { get; private set; } = string.Empty;
    public string AccountName { get; private set; } = string.Empty;
    public string? Notes { get; private set; }
    public bool IsActive { get; private set; } = true;

    private PayrollAccountMapping() { } // EF Core

    public PayrollAccountMapping(PayrollAccountKind kind, string accountCode, string accountName,
        string createdBy, string? notes = null)
    {
        Kind = kind;
        SetAccount(accountCode, accountName);
        Notes = Blank(notes);
        CreatedBy = createdBy;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public void SetAccount(string accountCode, string accountName)
    {
        if (string.IsNullOrWhiteSpace(accountCode))
            throw new DomainException("An accounting mapping requires an account code.");
        if (string.IsNullOrWhiteSpace(accountName))
            throw new DomainException("An accounting mapping requires an account name.");

        AccountCode = accountCode.Trim();
        AccountName = accountName.Trim();
    }

    public void SetNotes(string? notes) => Notes = Blank(notes);
    public void Deactivate(string modifiedBy) { IsActive = false; Touch(modifiedBy); }
    public void Activate(string modifiedBy) { IsActive = true; Touch(modifiedBy); }

    private void Touch(string modifiedBy) { ModifiedBy = modifiedBy; ModifiedAtUtc = DateTime.UtcNow; }
    private static string? Blank(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();
}
