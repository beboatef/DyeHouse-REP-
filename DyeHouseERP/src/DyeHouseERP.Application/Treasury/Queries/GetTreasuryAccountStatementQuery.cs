using DyeHouseERP.Application.Common.Exceptions;
using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.Treasury.Queries;

/// <summary>One line of a treasury/bank account statement (spec section 41).</summary>
public class TreasuryStatementLineDto
{
    public DateTime TransactionDate { get; set; }
    public string SourceDocumentNumber { get; set; } = string.Empty;
    public DocumentType SourceDocumentType { get; set; }
    public string? Description { get; set; }
    public decimal Debit { get; set; }
    public decimal Credit { get; set; }
    public decimal RunningBalance { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
}

public class TreasuryAccountStatementDto
{
    public Guid AccountId { get; set; }
    public string AccountCode { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;

    /// <summary>Balance carried into the requested window - the live sum over everything before From.</summary>
    public decimal OpeningBalance { get; set; }

    public decimal TotalIn { get; set; }
    public decimal TotalOut { get; set; }
    public decimal ClosingBalance { get; set; }
    public List<TreasuryStatementLineDto> Lines { get; set; } = new();
}

/// <summary>
/// Account statement for one cash/bank account. Like every other statement in
/// this system it is computed from the append-only ledger - there is no stored
/// balance anywhere that could disagree with it.
/// </summary>
public record GetTreasuryAccountStatementQuery(Guid AccountId, DateTime? From = null, DateTime? To = null)
    : IRequest<TreasuryAccountStatementDto>;

public class GetTreasuryAccountStatementQueryHandler
    : IRequestHandler<GetTreasuryAccountStatementQuery, TreasuryAccountStatementDto>
{
    private readonly IApplicationDbContext _db;
    public GetTreasuryAccountStatementQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<TreasuryAccountStatementDto> Handle(GetTreasuryAccountStatementQuery request, CancellationToken cancellationToken)
    {
        var account = await _db.TreasuryAccounts.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == request.AccountId, cancellationToken)
            ?? throw new NotFoundException("TreasuryAccount", request.AccountId);

        var to = request.To?.Date.AddDays(1).AddTicks(-1) ?? DateTime.MaxValue;

        var opening = await NetAsync(request.AccountId, request.From, null, cancellationToken);

        var rows = await _db.TreasuryTransactions.AsNoTracking()
            .Where(t => t.TreasuryAccountId == request.AccountId)
            .Where(t => (request.From == null || t.TransactionDate >= request.From) && t.TransactionDate <= to)
            .OrderBy(t => t.TransactionDate).ThenBy(t => t.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        var lines = new List<TreasuryStatementLineDto>();
        var running = opening;

        foreach (var row in rows)
        {
            var debit = row.Direction == TreasuryDirection.Out ? row.Amount : 0m;
            var credit = row.Direction == TreasuryDirection.In ? row.Amount : 0m;
            running += credit - debit;

            lines.Add(new TreasuryStatementLineDto
            {
                TransactionDate = row.TransactionDate,
                SourceDocumentNumber = row.SourceDocumentNumber,
                SourceDocumentType = row.SourceDocumentType,
                Description = row.Description,
                Debit = debit,
                Credit = credit,
                RunningBalance = running,
                CreatedBy = row.CreatedBy
            });
        }

        return new TreasuryAccountStatementDto
        {
            AccountId = account.Id,
            AccountCode = account.Code,
            AccountName = account.Name,
            Kind = account.Kind.ToString(),
            OpeningBalance = opening,
            TotalIn = lines.Sum(l => l.Credit),
            TotalOut = lines.Sum(l => l.Debit),
            ClosingBalance = running,
            Lines = lines
        };
    }

    private async Task<decimal> NetAsync(Guid accountId, DateTime? from, DateTime? to, CancellationToken ct)
    {
        var query = _db.TreasuryTransactions.AsNoTracking().Where(t => t.TreasuryAccountId == accountId);
        if (to is not null) query = query.Where(t => t.TransactionDate <= to.Value);
        if (from is not null) query = query.Where(t => t.TransactionDate < from.Value);

        var rows = await query.Select(t => new { t.Amount, t.Direction }).ToListAsync(ct);
        return rows.Sum(r => r.Direction == TreasuryDirection.In ? r.Amount : -r.Amount);
    }
}
