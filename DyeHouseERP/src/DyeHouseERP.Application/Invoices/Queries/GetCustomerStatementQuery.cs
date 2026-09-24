using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.Invoices.DTOs;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.Invoices.Queries;

/// <summary>Customer statement (spec section 33) - running balance computed live from CustomerLedgerEntries, never a stored balance field.</summary>
public record GetCustomerStatementQuery(Guid CustomerId, DateTime? From = null, DateTime? To = null) : IRequest<List<CustomerStatementLineDto>>;

public class GetCustomerStatementQueryHandler : IRequestHandler<GetCustomerStatementQuery, List<CustomerStatementLineDto>>
{
    private readonly IApplicationDbContext _db;
    public GetCustomerStatementQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<List<CustomerStatementLineDto>> Handle(GetCustomerStatementQuery request, CancellationToken cancellationToken)
    {
        var query = _db.CustomerLedgerEntries.AsNoTracking().Where(e => e.CustomerId == request.CustomerId);
        if (request.From.HasValue) query = query.Where(e => e.EntryDate >= request.From);
        if (request.To.HasValue) query = query.Where(e => e.EntryDate <= request.To);

        var entries = await query.OrderBy(e => e.EntryDate).ThenBy(e => e.CreatedAtUtc).ToListAsync(cancellationToken);

        var result = new List<CustomerStatementLineDto>();
        decimal running = 0;
        foreach (var e in entries)
        {
            running += e.Debit - e.Credit;
            result.Add(new CustomerStatementLineDto
            {
                Date = e.EntryDate, Description = e.Description, DocumentNumber = e.SourceDocumentNumber,
                Debit = e.Debit, Credit = e.Credit, RunningBalance = running
            });
        }
        return result;
    }
}
