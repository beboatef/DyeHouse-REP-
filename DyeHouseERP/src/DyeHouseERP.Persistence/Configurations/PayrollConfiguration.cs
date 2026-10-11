using DyeHouseERP.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DyeHouseERP.Persistence.Configurations;

public class DepartmentConfiguration : IEntityTypeConfiguration<Department>
{
    public void Configure(EntityTypeBuilder<Department> builder)
    {
        builder.ToTable("Departments");
        builder.HasKey(d => d.Id);

        builder.Property(d => d.Code).HasMaxLength(30).IsRequired();
        builder.HasIndex(d => d.Code).IsUnique();

        builder.Property(d => d.Name).HasMaxLength(200).IsRequired();
        builder.Property(d => d.NameAr).HasMaxLength(200).IsRequired();
        builder.Property(d => d.NameEn).HasMaxLength(200).IsRequired();
        builder.Property(d => d.Notes).HasMaxLength(1000);
        builder.Property(d => d.CreatedBy).HasMaxLength(100).IsRequired();
        builder.Property(d => d.ModifiedBy).HasMaxLength(100);
    }
}

public class EmployeeConfiguration : IEntityTypeConfiguration<Employee>
{
    public void Configure(EntityTypeBuilder<Employee> builder)
    {
        builder.ToTable("Employees");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Code).HasMaxLength(30).IsRequired();
        builder.HasIndex(e => e.Code).IsUnique();

        builder.Property(e => e.Name).HasMaxLength(200).IsRequired();
        builder.Property(e => e.NameAr).HasMaxLength(200).IsRequired();
        builder.Property(e => e.NameEn).HasMaxLength(200).IsRequired();
        builder.Property(e => e.JobTitle).HasMaxLength(150);
        builder.Property(e => e.BasicSalary).HasPrecision(18, 4);
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(e => e.Phone).HasMaxLength(40);
        builder.Property(e => e.NationalId).HasMaxLength(40);
        builder.Property(e => e.BankAccountNumber).HasMaxLength(60);
        builder.Property(e => e.Notes).HasMaxLength(1000);
        builder.Property(e => e.CreatedBy).HasMaxLength(100).IsRequired();
        builder.Property(e => e.ModifiedBy).HasMaxLength(100);

        builder.Ignore(e => e.IsPayable);

        builder.HasIndex(e => e.DepartmentId);
        builder.HasIndex(e => e.Status);

        // HR classification is filtered and reported on directly (spec sections 28-34), so the
        // four nullable classification columns are indexed even though they are optional.
        builder.HasIndex(e => e.HrSectionId);
        builder.HasIndex(e => e.JobPositionId);
        builder.HasIndex(e => e.CostCenterId);
        builder.HasIndex(e => e.HrShiftId);

        builder.ToTable(t => t.HasCheckConstraint("CK_Employees_BasicSalaryNotNegative", "[BasicSalary] >= 0"));
    }
}

public class PayrollRunConfiguration : IEntityTypeConfiguration<PayrollRun>
{
    public void Configure(EntityTypeBuilder<PayrollRun> builder)
    {
        builder.ToTable("PayrollRuns");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.RunNumber).HasMaxLength(30).IsRequired();
        builder.HasIndex(r => r.RunNumber).IsUnique();

        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(r => r.Notes).HasMaxLength(2000);
        builder.Property(r => r.ApprovedBy).HasMaxLength(100);
        builder.Property(r => r.PostedBy).HasMaxLength(100);
        builder.Property(r => r.CancelledBy).HasMaxLength(100);
        builder.Property(r => r.CancellationReason).HasMaxLength(1000);

        // Calculation / review / reversal workflow (spec section 39). These are the same
        // 100-character "who" fields and 1000-character "why" fields the rest of the
        // run uses, so no workflow string is ever silently left unbounded.
        builder.Property(r => r.CalculatedBy).HasMaxLength(100);
        builder.Property(r => r.SubmittedBy).HasMaxLength(100);
        builder.Property(r => r.ReviewedBy).HasMaxLength(100);
        builder.Property(r => r.ReviewNotes).HasMaxLength(1000);
        builder.Property(r => r.ReversedBy).HasMaxLength(100);
        builder.Property(r => r.ReversalReason).HasMaxLength(1000);

        builder.Property(r => r.CreatedBy).HasMaxLength(100).IsRequired();
        builder.Property(r => r.ModifiedBy).HasMaxLength(100);

        builder.Ignore(r => r.TotalGross);
        builder.Ignore(r => r.TotalDeductions);
        builder.Ignore(r => r.TotalNet);
        builder.Ignore(r => r.IsEditable);

        builder.HasIndex(r => r.TreasuryAccountId);

        // Reversal traceability (spec section 39): both sides of the original <-> adjustment
        // link are queried directly to show a run's correction history.
        builder.HasIndex(r => r.ReversesPayrollRunId);
        builder.HasIndex(r => r.ReversedByPayrollRunId);

        // One ACTIVE payroll run per period - a month is never paid twice (spec section 36).
        // The index is filtered so neither a cancelled run nor a REVERSED run blocks rebuilding
        // the payroll for that month (a reversal is corrected by its own adjustment run), which is
        // exactly why a run can be cancelled or reversed in the first place. This keeps the database
        // constraint and CreatePayrollRunCommandHandler in agreement.
        builder.HasIndex(r => new { r.PeriodYear, r.PeriodMonth })
            .IsUnique()
            .HasFilter("[Status] <> 'Cancelled' AND [Status] <> 'Reversed'");

        builder.Metadata.FindNavigation(nameof(PayrollRun.Lines))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(r => r.Lines)
            .WithOne()
            .HasForeignKey(l => l.PayrollRunId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class PayrollRunLineConfiguration : IEntityTypeConfiguration<PayrollRunLine>
{
    public void Configure(EntityTypeBuilder<PayrollRunLine> builder)
    {
        builder.ToTable("PayrollRunLines");
        builder.HasKey(l => l.Id);

        builder.Property(l => l.EmployeeCode).HasMaxLength(30).IsRequired();
        builder.Property(l => l.EmployeeName).HasMaxLength(200).IsRequired();
        builder.Property(l => l.DepartmentName).HasMaxLength(200);
        // Monetary amounts keep full 18,4 precision; day/hour counts keep 9,2.
        builder.Property(l => l.BasicSalary).HasPrecision(18, 4);
        builder.Property(l => l.Allowances).HasPrecision(18, 4);
        builder.Property(l => l.Incentives).HasPrecision(18, 4);
        builder.Property(l => l.Bonuses).HasPrecision(18, 4);
        builder.Property(l => l.Commissions).HasPrecision(18, 4);
        builder.Property(l => l.OvertimeAmount).HasPrecision(18, 4);
        builder.Property(l => l.OtherEarnings).HasPrecision(18, 4);
        builder.Property(l => l.Deductions).HasPrecision(18, 4);
        builder.Property(l => l.Tax).HasPrecision(18, 4);
        builder.Property(l => l.Insurance).HasPrecision(18, 4);
        builder.Property(l => l.EmployerInsurance).HasPrecision(18, 4);
        builder.Property(l => l.Advances).HasPrecision(18, 4);
        builder.Property(l => l.AttendanceDays).HasPrecision(9, 2);
        builder.Property(l => l.OvertimeHours).HasPrecision(9, 2);
        builder.Property(l => l.LeaveDays).HasPrecision(9, 2);
        builder.Property(l => l.Notes).HasMaxLength(1000);

        builder.Ignore(l => l.GrossPay);
        builder.Ignore(l => l.NetPay);

        builder.HasIndex(l => l.EmployeeId);
        builder.HasIndex(l => l.DepartmentId);
        builder.HasIndex(l => l.PayrollRunId);

        // Labor-cost reporting reads the snapshotted classification directly.
        builder.HasIndex(l => l.CostCenterId);
        builder.HasIndex(l => l.HrSectionId);

        // An employee can only be paid once per run (spec section 53 - never double count).
        builder.HasIndex(l => new { l.PayrollRunId, l.EmployeeId }).IsUnique();

        // No payroll line may carry a negative amount in ANY of its monetary columns,
        // not just the three hand-editable ones - a calculated breakdown is included too.
        builder.ToTable(t => t.HasCheckConstraint(
            "CK_PayrollRunLines_AmountsNotNegative",
            "[BasicSalary] >= 0 AND [Allowances] >= 0 AND [Deductions] >= 0 AND [Incentives] >= 0 AND [Bonuses] >= 0 AND [Commissions] >= 0 AND [OvertimeAmount] >= 0 AND [Tax] >= 0 AND [Insurance] >= 0 AND [Advances] >= 0"));
    }
}
