using DyeHouseERP.Application.Common.Exceptions;
using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.Payroll.DTOs;
using DyeHouseERP.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.Payroll.Queries;

/// <summary>
/// Builds the accounting export for one payroll run (spec section 37): every amount
/// the run produced is posted to the account configured for its kind. Amounts whose
/// kind has no mapping are listed in UnmappedKinds instead of silently disappearing,
/// so the accountant sees exactly what still needs configuring.
/// </summary>
public record GetPayrollAccountingExportQuery(Guid RunId) : IRequest<PayrollAccountingExportDto>;

public class GetPayrollAccountingExportQueryHandler
    : IRequestHandler<GetPayrollAccountingExportQuery, PayrollAccountingExportDto>
{
    private readonly IApplicationDbContext _db;
    public GetPayrollAccountingExportQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<PayrollAccountingExportDto> Handle(GetPayrollAccountingExportQuery r, CancellationToken ct)
    {
        var run = await _db.PayrollRuns.AsNoTracking().Include(x => x.Lines)
            .FirstOrDefaultAsync(x => x.Id == r.RunId, ct)
            ?? throw new NotFoundException("PayrollRun", r.RunId);

        var mappings = await _db.PayrollAccountMappings.AsNoTracking()
            .Where(m => m.IsActive)
            .ToDictionaryAsync(m => m.Kind, ct);

        var employeeInsurance = run.TotalInsurance;
        var employerInsurance = run.TotalEmployerInsurance;

        var amounts = new (PayrollAccountKind Kind, decimal Amount)[]
        {
            (PayrollAccountKind.BasicSalary, run.Lines.Sum(l => l.BasicSalary)),
            (PayrollAccountKind.Allowance, run.Lines.Sum(l => l.Allowances + l.Incentives + l.Bonuses + l.Commissions + l.OtherEarnings)),
            (PayrollAccountKind.Deduction, run.Lines.Sum(l => l.Deductions)),
            (PayrollAccountKind.Tax, run.TotalTax),
            (PayrollAccountKind.Insurance, employeeInsurance + employerInsurance),
            (PayrollAccountKind.Advance, run.TotalAdvances),
            (PayrollAccountKind.NetPay, run.TotalNet)
        };

        var dto = new PayrollAccountingExportDto
        {
            RunId = run.Id, RunNumber = run.RunNumber, PeriodYear = run.PeriodYear, PeriodMonth = run.PeriodMonth
        };

        foreach (var (kind, amount) in amounts)
        {
            if (amount == 0) continue;

            if (!mappings.TryGetValue(kind, out var mapping))
            {
                dto.UnmappedKinds.Add(kind.ToString());
                continue;
            }

            dto.Lines.Add(new PayrollAccountingLineDto
            {
                Kind = kind,
                AccountCode = mapping.AccountCode,
                AccountName = mapping.AccountName,
                // Earnings and amounts owed to third parties/employees are debits; the
                // net payable released from the bank is the credit that balances it.
                Debit = kind == PayrollAccountKind.NetPay ? 0m : amount,
                Credit = kind == PayrollAccountKind.NetPay ? amount : 0m
            });
        }

        return dto;
    }
}
