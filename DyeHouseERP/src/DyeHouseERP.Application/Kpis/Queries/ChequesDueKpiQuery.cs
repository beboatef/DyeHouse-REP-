using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.Kpis.DTOs;
using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.Kpis.Queries;

/// <summary>
/// Cheques due in the next N days (spec Part 1 metric 6, default 7): customer
/// and supplier cheques whose DueDate falls in [today, today + Days] and whose
/// status is neither Cleared nor Cancelled. Bounced cheques are included - a
/// bounced cheque is still outstanding until it is replaced or cancelled.
/// </summary>
public record GetChequesDueKpiQuery(int Days = 7) : IRequest<ChequesDueKpiResult>;

public class GetChequesDueKpiQueryValidator : AbstractValidator<GetChequesDueKpiQuery>
{
    public GetChequesDueKpiQueryValidator()
    {
        RuleFor(x => x.Days).InclusiveBetween(1, 90);
    }
}

public class GetChequesDueKpiQueryHandler : IRequestHandler<GetChequesDueKpiQuery, ChequesDueKpiResult>
{
    private readonly IApplicationDbContext _db;
    public GetChequesDueKpiQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<ChequesDueKpiResult> Handle(GetChequesDueKpiQuery request, CancellationToken cancellationToken)
    {
        var today = DateTime.UtcNow.Date;
        var horizon = today.AddDays(request.Days);

        var cheques = await _db.Checks.AsNoTracking()
            .Where(c => c.DueDate >= today && c.DueDate <= horizon
                && c.Status != CheckStatus.Cleared && c.Status != CheckStatus.Cancelled)
            .OrderBy(c => c.DueDate)
            .Select(c => new { c.Id, c.CheckNumber, c.Direction, c.CustomerId, c.SupplierId, c.DueDate, c.Amount, c.Status })
            .ToListAsync(cancellationToken);

        var customerIds = cheques.Where(c => c.CustomerId.HasValue).Select(c => c.CustomerId!.Value).Distinct().ToList();
        var supplierIds = cheques.Where(c => c.SupplierId.HasValue).Select(c => c.SupplierId!.Value).Distinct().ToList();
        var customers = customerIds.Count == 0
            ? new Dictionary<Guid, (string Code, string Name)>()
            : await _db.Customers.AsNoTracking().Where(c => customerIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, c => (c.Code, c.Name), cancellationToken);
        var suppliers = supplierIds.Count == 0
            ? new Dictionary<Guid, (string Code, string Name)>()
            : await _db.Suppliers.AsNoTracking().Where(s => supplierIds.Contains(s.Id))
                .ToDictionaryAsync(s => s.Id, s => (s.Code, s.Name), cancellationToken);

        var result = new ChequesDueKpiResult
        {
            From = today,
            To = horizon,
            TimeZone = KpiQueryHelpers.KpiTimeZone,
            ComputedAtUtc = DateTime.UtcNow,
            Days = request.Days
        };

        foreach (var c in cheques)
        {
            var isCustomer = c.Direction == CheckDirection.CustomerCheck;
            customers.TryGetValue(c.CustomerId ?? Guid.Empty, out var customer);
            suppliers.TryGetValue(c.SupplierId ?? Guid.Empty, out var supplier);
            result.Cheques.Add(new ChequeDueRow
            {
                CheckId = c.Id,
                CheckNumber = c.CheckNumber,
                Kind = isCustomer ? "Customer" : "Supplier",
                PartyCode = (isCustomer ? customer.Code : supplier.Code) ?? "?",
                PartyName = (isCustomer ? customer.Name : supplier.Name) ?? "?",
                DueDate = c.DueDate,
                Amount = c.Amount,
                Status = c.Status.ToString(),
                DaysUntilDue = (int)(c.DueDate.Date - today).TotalDays
            });
        }

        result.ChequesCount = result.Cheques.Count;
        result.TotalAmount = result.Cheques.Sum(c => c.Amount);
        return result;
    }
}
