using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.Treasury.DTOs;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.Treasury.Queries;

public record GetTreasuryAccountsQuery : IRequest<List<TreasuryAccountDto>>;

/// <summary>Lists treasury accounts with their live balance - always summed from TreasuryTransaction, never a stored field (spec sections 18, 34).</summary>
public class GetTreasuryAccountsQueryHandler : IRequestHandler<GetTreasuryAccountsQuery, List<TreasuryAccountDto>>
{
    private readonly IApplicationDbContext _db;
    public GetTreasuryAccountsQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<List<TreasuryAccountDto>> Handle(GetTreasuryAccountsQuery request, CancellationToken cancellationToken)
    {
        var accounts = await _db.TreasuryAccounts.AsNoTracking().OrderBy(a => a.Code).ToListAsync(cancellationToken);
        if (accounts.Count == 0) return new List<TreasuryAccountDto>();

        var balances = await _db.TreasuryTransactions.AsNoTracking()
            .GroupBy(t => t.TreasuryAccountId)
            .Select(g => new { AccountId = g.Key, Balance = g.Sum(t => t.Amount * (int)t.Direction) })
            .ToDictionaryAsync(x => x.AccountId, x => x.Balance, cancellationToken);

        return accounts.Select(a => new TreasuryAccountDto
        {
            Id = a.Id, Code = a.Code, Name = a.Name, Kind = a.Kind.ToString(), IsActive = a.IsActive,
            Balance = balances.GetValueOrDefault(a.Id)
        }).ToList();
    }
}

public record GetReceiptsQuery(Guid? CustomerId = null) : IRequest<List<ReceiptDto>>;

public class GetReceiptsQueryHandler : IRequestHandler<GetReceiptsQuery, List<ReceiptDto>>
{
    private readonly IApplicationDbContext _db;
    public GetReceiptsQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<List<ReceiptDto>> Handle(GetReceiptsQuery request, CancellationToken cancellationToken)
    {
        var query = _db.Receipts.AsNoTracking().AsQueryable();
        if (request.CustomerId.HasValue) query = query.Where(r => r.CustomerId == request.CustomerId);

        var receipts = await query.OrderByDescending(r => r.ReceiptDate).ToListAsync(cancellationToken);
        if (receipts.Count == 0) return new List<ReceiptDto>();

        var accounts = await _db.TreasuryAccounts.AsNoTracking().Where(a => receipts.Select(r => r.TreasuryAccountId).Contains(a.Id)).ToDictionaryAsync(a => a.Id, cancellationToken);
        var customers = await _db.Customers.AsNoTracking().Where(c => receipts.Select(r => r.CustomerId).Contains(c.Id)).ToDictionaryAsync(c => c.Id, cancellationToken);
        var invoiceIds = receipts.Where(r => r.InvoiceId.HasValue).Select(r => r.InvoiceId!.Value).Distinct().ToList();
        var invoices = await _db.Invoices.AsNoTracking().Where(i => invoiceIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, cancellationToken);

        return receipts.Select(r => new ReceiptDto
        {
            Id = r.Id, ReceiptNumber = r.ReceiptNumber, ReceiptDate = r.ReceiptDate,
            CustomerId = r.CustomerId, CustomerCode = r.CustomerId.HasValue ? customers.GetValueOrDefault(r.CustomerId.Value)?.Code : null,
            TreasuryAccountId = r.TreasuryAccountId, TreasuryAccountName = accounts.GetValueOrDefault(r.TreasuryAccountId)?.Name ?? "",
            InvoiceId = r.InvoiceId, InvoiceNumber = r.InvoiceId.HasValue ? invoices.GetValueOrDefault(r.InvoiceId.Value)?.InvoiceNumber : null,
            Amount = r.Amount, PaymentMethod = r.PaymentMethod, Description = r.Description
        }).ToList();
    }
}

public record GetPaymentsQuery : IRequest<List<PaymentDto>>;

public class GetPaymentsQueryHandler : IRequestHandler<GetPaymentsQuery, List<PaymentDto>>
{
    private readonly IApplicationDbContext _db;
    public GetPaymentsQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<List<PaymentDto>> Handle(GetPaymentsQuery request, CancellationToken cancellationToken)
    {
        var payments = await _db.Payments.AsNoTracking().OrderByDescending(p => p.PaymentDate).ToListAsync(cancellationToken);
        if (payments.Count == 0) return new List<PaymentDto>();

        var accounts = await _db.TreasuryAccounts.AsNoTracking().Where(a => payments.Select(p => p.TreasuryAccountId).Contains(a.Id)).ToDictionaryAsync(a => a.Id, cancellationToken);

        return payments.Select(p => new PaymentDto
        {
            Id = p.Id, PaymentNumber = p.PaymentNumber, PaymentDate = p.PaymentDate,
            TreasuryAccountId = p.TreasuryAccountId, TreasuryAccountName = accounts.GetValueOrDefault(p.TreasuryAccountId)?.Name ?? "",
            Amount = p.Amount, PayeeDescription = p.PayeeDescription, Description = p.Description
        }).ToList();
    }
}

public record GetTreasuryTransfersQuery : IRequest<List<TreasuryTransferDto>>;

public class GetTreasuryTransfersQueryHandler : IRequestHandler<GetTreasuryTransfersQuery, List<TreasuryTransferDto>>
{
    private readonly IApplicationDbContext _db;
    public GetTreasuryTransfersQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<List<TreasuryTransferDto>> Handle(GetTreasuryTransfersQuery request, CancellationToken cancellationToken)
    {
        var transfers = await _db.TreasuryTransfers.AsNoTracking().OrderByDescending(t => t.TransferDate).ToListAsync(cancellationToken);
        if (transfers.Count == 0) return new List<TreasuryTransferDto>();

        var accountIds = transfers.SelectMany(t => new[] { t.FromAccountId, t.ToAccountId }).Distinct().ToList();
        var accounts = await _db.TreasuryAccounts.AsNoTracking().Where(a => accountIds.Contains(a.Id)).ToDictionaryAsync(a => a.Id, cancellationToken);

        return transfers.Select(t => new TreasuryTransferDto
        {
            Id = t.Id, TransferNumber = t.TransferNumber, TransferDate = t.TransferDate,
            FromAccountId = t.FromAccountId, FromAccountName = accounts.GetValueOrDefault(t.FromAccountId)?.Name ?? "",
            ToAccountId = t.ToAccountId, ToAccountName = accounts.GetValueOrDefault(t.ToAccountId)?.Name ?? "",
            Amount = t.Amount, Description = t.Description
        }).ToList();
    }
}
