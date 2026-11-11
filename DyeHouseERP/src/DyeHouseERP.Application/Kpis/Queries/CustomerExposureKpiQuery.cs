using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.Kpis.DTOs;
using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.Kpis.Queries;

/// <summary>
/// Customer exposure (spec Part 1 metric 5), read-only and computed from the
/// append-only ledgers, never from stored balances: the open balance is the
/// live sum of CustomerLedgerEntry debits minus credits per customer, and the
/// open-invoice figures come from Issued/PartiallyPaid invoices minus their
/// posted linked receipts. The oldest-unpaid age counts days from the invoice
/// date (invoices carry no due date in this system) for open invoices with a
/// positive remaining balance.
/// </summary>
public record GetCustomerExposureKpiQuery(Guid? CustomerId = null) : IRequest<CustomerExposureKpiResult>;

public class GetCustomerExposureKpiQueryHandler : IRequestHandler<GetCustomerExposureKpiQuery, CustomerExposureKpiResult>
{
    private readonly IApplicationDbContext _db;
    public GetCustomerExposureKpiQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<CustomerExposureKpiResult> Handle(GetCustomerExposureKpiQuery request, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var today = now.Date;

        var ledgerQuery = _db.CustomerLedgerEntries.AsNoTracking().AsQueryable();
        if (request.CustomerId.HasValue) ledgerQuery = ledgerQuery.Where(e => e.CustomerId == request.CustomerId.Value);
        var balances = await ledgerQuery
            .GroupBy(e => e.CustomerId)
            .Select(g => new { CustomerId = g.Key, Balance = g.Sum(e => e.Debit - e.Credit) })
            .ToDictionaryAsync(x => x.CustomerId, x => x.Balance, cancellationToken);

        var invoicesQuery = _db.Invoices.AsNoTracking()
            .Where(i => i.Status == InvoiceStatus.Issued || i.Status == InvoiceStatus.PartiallyPaid);
        if (request.CustomerId.HasValue) invoicesQuery = invoicesQuery.Where(i => i.CustomerId == request.CustomerId.Value);
        // Invoice.Total is an unmapped computed property, so the subtotal is
        // aggregated from the mapped line columns here and Total is derived
        // client-side exactly as the entity defines it (lines - discount + tax).
        var invoices = await invoicesQuery
            .Select(i => new { i.Id, i.InvoiceNumber, i.InvoiceDate, i.CustomerId, i.Discount, i.Tax })
            .ToListAsync(cancellationToken);

        var invoiceIds = invoices.Select(i => i.Id).ToList();
        var lineTotals = invoiceIds.Count == 0
            ? new Dictionary<Guid, decimal>()
            : await _db.InvoiceLines.AsNoTracking()
                .Where(l => invoiceIds.Contains(l.InvoiceId))
                .GroupBy(l => l.InvoiceId)
                .Select(g => new { InvoiceId = g.Key, SubTotal = g.Sum(l => l.Quantity * l.ProcessingPrice) })
                .ToDictionaryAsync(x => x.InvoiceId, x => x.SubTotal, cancellationToken);
        var paidByInvoice = invoiceIds.Count == 0
            ? new Dictionary<Guid, decimal>()
            : await _db.Receipts.AsNoTracking()
                .Where(r => r.InvoiceId != null
                    && invoiceIds.Contains(r.InvoiceId.Value)
                    && r.Status == TreasuryDocumentStatus.Posted)
                .GroupBy(r => r.InvoiceId!.Value)
                .Select(g => new { InvoiceId = g.Key, Paid = g.Sum(r => r.Amount) })
                .ToDictionaryAsync(x => x.InvoiceId, x => x.Paid, cancellationToken);

        var customerIds = balances.Keys
            .Concat(invoices.Select(i => i.CustomerId))
            .Distinct().ToList();
        var customers = await _db.Customers.AsNoTracking()
            .Where(c => customerIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => new { c.Code, c.Name }, cancellationToken);

        var result = new CustomerExposureKpiResult
        {
            From = null,
            To = null,
            TimeZone = KpiQueryHelpers.KpiTimeZone,
            ComputedAtUtc = now,
            AsOfUtc = now
        };

        foreach (var id in customerIds)
        {
            customers.TryGetValue(id, out var customer);
            var openInvoices = invoices
                .Where(i => i.CustomerId == id)
                .Select(i => new
                {
                    i.InvoiceNumber,
                    i.InvoiceDate,
                    Balance = lineTotals.GetValueOrDefault(i.Id) - i.Discount + i.Tax - paidByInvoice.GetValueOrDefault(i.Id)
                })
                .Where(i => i.Balance > 0)
                .OrderBy(i => i.InvoiceDate)
                .ToList();

            var oldest = openInvoices.FirstOrDefault();
            result.Customers.Add(new CustomerExposureRow
            {
                CustomerId = id,
                CustomerCode = customer?.Code ?? "?",
                CustomerName = customer?.Name ?? "?",
                OpenBalance = balances.GetValueOrDefault(id),
                OpenInvoicesCount = openInvoices.Count,
                OpenInvoicesTotal = openInvoices.Sum(i => i.Balance),
                OldestUnpaidInvoiceNumber = oldest?.InvoiceNumber,
                OldestUnpaidInvoiceAgeDays = oldest is null ? null : (int)(today - oldest.InvoiceDate.Date).TotalDays
            });
        }

        result.Customers = result.Customers
            .Where(c => c.OpenBalance != 0 || c.OpenInvoicesCount > 0)
            .OrderByDescending(c => c.OpenBalance)
            .ToList();
        result.TotalOpenBalance = result.Customers.Sum(c => c.OpenBalance);
        result.TotalOpenInvoices = result.Customers.Sum(c => c.OpenInvoicesTotal);
        return result;
    }
}
