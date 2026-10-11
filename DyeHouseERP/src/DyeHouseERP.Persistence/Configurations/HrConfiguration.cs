using DyeHouseERP.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DyeHouseERP.Persistence.Configurations;

public class HrSectionConfiguration : IEntityTypeConfiguration<HrSection>
{
    public void Configure(EntityTypeBuilder<HrSection> builder)
    {
        builder.ToTable("HrSections");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.Code).HasMaxLength(30).IsRequired();
        builder.HasIndex(s => s.Code).IsUnique();

        builder.Property(s => s.NameAr).HasMaxLength(200).IsRequired();
        builder.Property(s => s.NameEn).HasMaxLength(200).IsRequired();
        builder.Property(s => s.Name).HasMaxLength(200).IsRequired();
        builder.Property(s => s.Notes).HasMaxLength(1000);
        builder.Property(s => s.CreatedBy).HasMaxLength(100).IsRequired();
        builder.Property(s => s.ModifiedBy).HasMaxLength(100);

        builder.HasIndex(s => s.DepartmentId);
        builder.HasIndex(s => s.IsActive);
    }
}

public class JobPositionConfiguration : IEntityTypeConfiguration<JobPosition>
{
    public void Configure(EntityTypeBuilder<JobPosition> builder)
    {
        builder.ToTable("JobPositions");
        builder.HasKey(j => j.Id);

        builder.Property(j => j.Code).HasMaxLength(30).IsRequired();
        builder.HasIndex(j => j.Code).IsUnique();

        builder.Property(j => j.NameAr).HasMaxLength(200).IsRequired();
        builder.Property(j => j.NameEn).HasMaxLength(200).IsRequired();
        builder.Property(j => j.Name).HasMaxLength(200).IsRequired();
        builder.Property(j => j.DefaultBasicSalary).HasPrecision(18, 4);
        builder.Property(j => j.Notes).HasMaxLength(1000);
        builder.Property(j => j.CreatedBy).HasMaxLength(100).IsRequired();
        builder.Property(j => j.ModifiedBy).HasMaxLength(100);

        builder.HasIndex(j => j.DepartmentId);
        builder.HasIndex(j => j.IsActive);
    }
}

public class CostCenterConfiguration : IEntityTypeConfiguration<CostCenter>
{
    public void Configure(EntityTypeBuilder<CostCenter> builder)
    {
        builder.ToTable("CostCenters");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Code).HasMaxLength(30).IsRequired();
        builder.HasIndex(c => c.Code).IsUnique();

        builder.Property(c => c.NameAr).HasMaxLength(200).IsRequired();
        builder.Property(c => c.NameEn).HasMaxLength(200).IsRequired();
        builder.Property(c => c.Name).HasMaxLength(200).IsRequired();
        builder.Property(c => c.Notes).HasMaxLength(1000);
        builder.Property(c => c.CreatedBy).HasMaxLength(100).IsRequired();
        builder.Property(c => c.ModifiedBy).HasMaxLength(100);

        builder.HasIndex(c => c.IsActive);
    }
}

public class HrShiftConfiguration : IEntityTypeConfiguration<HrShift>
{
    public void Configure(EntityTypeBuilder<HrShift> builder)
    {
        builder.ToTable("HrShifts");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.Code).HasMaxLength(30).IsRequired();
        builder.HasIndex(s => s.Code).IsUnique();

        builder.Property(s => s.NameAr).HasMaxLength(200).IsRequired();
        builder.Property(s => s.NameEn).HasMaxLength(200).IsRequired();
        builder.Property(s => s.Name).HasMaxLength(200).IsRequired();
        builder.Property(s => s.WorkingDays).HasMaxLength(30).IsRequired();
        builder.Property(s => s.CreatedBy).HasMaxLength(100).IsRequired();
        builder.Property(s => s.ModifiedBy).HasMaxLength(100);

        builder.Ignore(s => s.PlannedMinutes);

        builder.HasIndex(s => s.IsActive);
    }
}

public class AttendanceRuleConfiguration : IEntityTypeConfiguration<AttendanceRule>
{
    public void Configure(EntityTypeBuilder<AttendanceRule> builder)
    {
        builder.ToTable("AttendanceRules");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.Name).HasMaxLength(150).IsRequired();
        builder.Property(a => a.StandardDailyHours).HasPrecision(9, 2);
        builder.Property(a => a.StandardWorkingDaysPerMonth).HasPrecision(9, 2);
        builder.Property(a => a.LatePenaltyPerMinute).HasPrecision(18, 4);
        builder.Property(a => a.EarlyLeavePenaltyPerMinute).HasPrecision(18, 4);
        builder.Property(a => a.AbsenceDayDeductionFactor).HasPrecision(9, 4);
        builder.Property(a => a.OvertimeNormalMultiplier).HasPrecision(9, 4);
        builder.Property(a => a.OvertimeWeekendMultiplier).HasPrecision(9, 4);
        builder.Property(a => a.OvertimeHolidayMultiplier).HasPrecision(9, 4);
        builder.Property(a => a.WeekendDays).HasMaxLength(30);
        builder.Property(a => a.CreatedBy).HasMaxLength(100).IsRequired();
        builder.Property(a => a.ModifiedBy).HasMaxLength(100);

        builder.HasIndex(a => a.IsActive);
    }
}

public class AttendanceEventConfiguration : IEntityTypeConfiguration<AttendanceEvent>
{
    public void Configure(EntityTypeBuilder<AttendanceEvent> builder)
    {
        builder.ToTable("AttendanceEvents");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.Status).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(a => a.Source).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(a => a.ShiftName).HasMaxLength(150);
        builder.Property(a => a.AbsenceDays).HasPrecision(9, 2);
        builder.Property(a => a.Notes).HasMaxLength(1000);
        builder.Property(a => a.CreatedBy).HasMaxLength(100).IsRequired();
        builder.Property(a => a.ModifiedBy).HasMaxLength(100);

        // One attendance record per employee per day - a correction updates it.
        builder.HasIndex(a => new { a.EmployeeId, a.WorkDate }).IsUnique();
        builder.HasIndex(a => a.WorkDate);
        builder.HasIndex(a => a.Status);
        builder.HasIndex(a => a.LeaveRequestId);
    }
}

public class LeaveTypeConfiguration : IEntityTypeConfiguration<LeaveType>
{
    public void Configure(EntityTypeBuilder<LeaveType> builder)
    {
        builder.ToTable("LeaveTypes");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.Code).HasMaxLength(30).IsRequired();
        builder.HasIndex(t => t.Code).IsUnique();

        builder.Property(t => t.NameAr).HasMaxLength(200).IsRequired();
        builder.Property(t => t.NameEn).HasMaxLength(200).IsRequired();
        builder.Property(t => t.Name).HasMaxLength(200).IsRequired();
        builder.Property(t => t.AnnualEntitlementDays).HasPrecision(9, 2);
        builder.Property(t => t.CreatedBy).HasMaxLength(100).IsRequired();
        builder.Property(t => t.ModifiedBy).HasMaxLength(100);

        builder.HasIndex(t => t.IsActive);
    }
}

public class LeaveRequestConfiguration : IEntityTypeConfiguration<LeaveRequest>
{
    public void Configure(EntityTypeBuilder<LeaveRequest> builder)
    {
        builder.ToTable("LeaveRequests");
        builder.HasKey(l => l.Id);

        builder.Property(l => l.Status).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(l => l.Days).HasPrecision(9, 2);
        builder.Property(l => l.Reason).HasMaxLength(1000).IsRequired();
        builder.Property(l => l.DecidedBy).HasMaxLength(100);
        builder.Property(l => l.DecisionReason).HasMaxLength(1000);
        builder.Property(l => l.CreatedBy).HasMaxLength(100).IsRequired();
        builder.Property(l => l.ModifiedBy).HasMaxLength(100);

        builder.HasIndex(l => l.EmployeeId);
        builder.HasIndex(l => l.Status);
        builder.HasIndex(l => l.LeaveTypeId);
        builder.HasIndex(l => new { l.FromDate, l.ToDate });
    }
}

public class OvertimeEntryConfiguration : IEntityTypeConfiguration<OvertimeEntry>
{
    public void Configure(EntityTypeBuilder<OvertimeEntry> builder)
    {
        builder.ToTable("OvertimeEntries");
        builder.HasKey(o => o.Id);

        builder.Property(o => o.Hours).HasPrecision(9, 2);
        builder.Property(o => o.Category).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(o => o.Status).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(o => o.ApprovedMultiplier).HasPrecision(9, 4);
        builder.Property(o => o.Reason).HasMaxLength(1000).IsRequired();
        builder.Property(o => o.DecidedBy).HasMaxLength(100);
        builder.Property(o => o.DecisionReason).HasMaxLength(1000);
        builder.Property(o => o.CreatedBy).HasMaxLength(100).IsRequired();
        builder.Property(o => o.ModifiedBy).HasMaxLength(100);

        builder.HasIndex(o => o.EmployeeId);
        builder.HasIndex(o => o.Status);
        builder.HasIndex(o => new { o.WorkDate, o.Status });
        builder.HasIndex(o => o.PayrollRunId);
    }
}

public class EmployeeAdvanceConfiguration : IEntityTypeConfiguration<EmployeeAdvance>
{
    public void Configure(EntityTypeBuilder<EmployeeAdvance> builder)
    {
        builder.ToTable("EmployeeAdvances");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.Amount).HasPrecision(18, 4);
        builder.Property(a => a.InstalmentAmount).HasPrecision(18, 4);
        builder.Property(a => a.RecoveredAmount).HasPrecision(18, 4);
        builder.Property(a => a.Reason).HasMaxLength(1000).IsRequired();
        builder.Property(a => a.Status).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(a => a.CreatedBy).HasMaxLength(100).IsRequired();
        builder.Property(a => a.ModifiedBy).HasMaxLength(100);

        builder.Ignore(a => a.RemainingAmount);

        builder.HasIndex(a => a.EmployeeId);
        builder.HasIndex(a => a.Status);
    }
}

public class PayComponentConfiguration : IEntityTypeConfiguration<PayComponent>
{
    public void Configure(EntityTypeBuilder<PayComponent> builder)
    {
        builder.ToTable("PayComponents");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Code).HasMaxLength(30).IsRequired();
        builder.HasIndex(c => c.Code).IsUnique();

        builder.Property(c => c.NameAr).HasMaxLength(200).IsRequired();
        builder.Property(c => c.NameEn).HasMaxLength(200).IsRequired();
        builder.Property(c => c.Name).HasMaxLength(200).IsRequired();
        builder.Property(c => c.Kind).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(c => c.Calculation).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(c => c.DefaultAmount).HasPrecision(18, 4);
        builder.Property(c => c.DefaultPercent).HasPrecision(9, 4);
        builder.Property(c => c.Notes).HasMaxLength(1000);
        builder.Property(c => c.CreatedBy).HasMaxLength(100).IsRequired();
        builder.Property(c => c.ModifiedBy).HasMaxLength(100);

        builder.Ignore(c => c.IsDeduction);

        builder.HasIndex(c => c.IsActive);
    }
}

public class EmployeePayComponentConfiguration : IEntityTypeConfiguration<EmployeePayComponent>
{
    public void Configure(EntityTypeBuilder<EmployeePayComponent> builder)
    {
        builder.ToTable("EmployeePayComponents");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.Amount).HasPrecision(18, 4);
        builder.Property(a => a.Percent).HasPrecision(9, 4);
        builder.Property(a => a.CreatedBy).HasMaxLength(100).IsRequired();
        builder.Property(a => a.ModifiedBy).HasMaxLength(100);

        builder.HasIndex(a => a.EmployeeId);
        builder.HasIndex(a => a.PayComponentId);
        builder.HasIndex(a => new { a.EmployeeId, a.PayComponentId, a.EffectiveFrom }).IsUnique();
    }
}

public class ContributionRuleConfiguration : IEntityTypeConfiguration<ContributionRule>
{
    public void Configure(EntityTypeBuilder<ContributionRule> builder)
    {
        builder.ToTable("ContributionRules");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Code).HasMaxLength(30).IsRequired();
        builder.HasIndex(c => c.Code).IsUnique();

        builder.Property(c => c.NameAr).HasMaxLength(200).IsRequired();
        builder.Property(c => c.NameEn).HasMaxLength(200).IsRequired();
        builder.Property(c => c.Name).HasMaxLength(200).IsRequired();
        builder.Property(c => c.Kind).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(c => c.Base).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(c => c.EmployeeRatePercent).HasPrecision(9, 4);
        builder.Property(c => c.EmployerRatePercent).HasPrecision(9, 4);
        builder.Property(c => c.MinBase).HasPrecision(18, 4);
        builder.Property(c => c.MaxBase).HasPrecision(18, 4);
        builder.Property(c => c.CreatedBy).HasMaxLength(100).IsRequired();
        builder.Property(c => c.ModifiedBy).HasMaxLength(100);

        builder.HasIndex(c => c.IsActive);
    }
}

public class PayrollAccountMappingConfiguration : IEntityTypeConfiguration<PayrollAccountMapping>
{
    public void Configure(EntityTypeBuilder<PayrollAccountMapping> builder)
    {
        builder.ToTable("PayrollAccountMappings");
        builder.HasKey(m => m.Id);

        builder.Property(m => m.Kind).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(m => m.AccountCode).HasMaxLength(50).IsRequired();
        builder.Property(m => m.AccountName).HasMaxLength(200).IsRequired();
        builder.Property(m => m.Notes).HasMaxLength(1000);
        builder.Property(m => m.CreatedBy).HasMaxLength(100).IsRequired();
        builder.Property(m => m.ModifiedBy).HasMaxLength(100);

        // One account per amount kind - the export can never find two.
        builder.HasIndex(m => m.Kind).IsUnique();
    }
}
