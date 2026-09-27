using DyeHouseERP.Application.Common.Exceptions;
using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.Purchases.DTOs;
using DyeHouseERP.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.Purchases.Queries;

// ------------------------------------------------------------------ orders

/// <summary>Purchase orders list (spec section 35), filterable by supplier, status and date.</summary>
public record GetPurchaseOrdersQuery(
    Guid? SupplierId = null,
    PurchaseOrderStatus? Status = null,
    DateTime? From = null,
    DateTime? To = null) : IRequest<List<PurchaseOrderDto>>;

public class GetPurchaseOrdersQueryHandler : IRequestHandler<GetPurchaseOrdersQuery, List<PurchaseOrderDto>>
{
    private readonly IApplicationDbContext _db;
    public GetPurchaseOrdersQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<List<PurchaseOrderDto>> Handle(GetPurchaseOrdersQuery request, CancellationToken cancellationToken)
    {
        var query = _db.PurchaseOrders.AsNoTracking().Include(o => o.Lines).AsQueryable();

        if (request.SupplierId.HasValue) query = query.Where(o => o.SupplierId == request.SupplierId);
        if (request.Status.HasValue) query = query.Where(o => o.Status == request.Status);
        if (request.From.HasValue) query = query.Where(o => o.OrderDate >= request.From);
        if (request.To.HasValue) query = query.Where(o => o.OrderDate <= request.To);

        var orders = await query
            .OrderByDescending(o => o.OrderDate)
            .ThenByDescending(o => o.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        return await PurchaseDtoBuilder.MapOrdersAsync(_db, orders, cancellationToken);
    }
}

public record GetPurchaseOrderByIdQuery(Guid Id) : IRequest<PurchaseOrderDto>;

public class GetPurchaseOrderByIdQueryHandler : IRequestHandler<GetPurchaseOrderByIdQuery, PurchaseOrderDto>
{
    private readonly IApplicationDbContext _db;
    public GetPurchaseOrderByIdQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<PurchaseOrderDto> Handle(GetPurchaseOrderByIdQuery request, CancellationToken cancellationToken)
    {
        var order = await _db.PurchaseOrders.AsNoTracking().Include(o => o.Lines)
            .FirstOrDefaultAsync(o => o.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("PurchaseOrder", request.Id);

        var mapped = await PurchaseDtoBuilder.MapOrdersAsync(_db, new[] { order }, cancellationToken);
        return mapped.Single();
    }
}

// ---------------------------------------------------------------- receipts

/// <summary>Goods receipts (spec section 35): what actually arrived from which supplier into which warehouse.</summary>
public record GetPurchaseReceiptsQuery(
    Guid? SupplierId = null,
    Guid? PurchaseOrderId = null,
    DateTime? From = null,
    DateTime? To = null) : IRequest<List<PurchaseReceiptDto>>;

public class GetPurchaseReceiptsQueryHandler : IRequestHandler<GetPurchaseReceiptsQuery, List<PurchaseReceiptDto>>
{
    private readonly IApplicationDbContext _db;
    public GetPurchaseReceiptsQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<List<PurchaseReceiptDto>> Handle(GetPurchaseReceiptsQuery request, CancellationToken cancellationToken)
    {
        var query = _db.PurchaseReceipts.AsNoTracking().Include(r => r.Lines).AsQueryable();

        if (request.SupplierId.HasValue) query = query.Where(r => r.SupplierId == request.SupplierId);
        if (request.PurchaseOrderId.HasValue) query = query.Where(r => r.PurchaseOrderId == request.PurchaseOrderId);
        if (request.From.HasValue) query = query.Where(r => r.ReceiptDate >= request.From);
        if (request.To.HasValue) query = query.Where(r => r.ReceiptDate <= request.To);

        var receipts = await query
            .OrderByDescending(r => r.ReceiptDate)
            .ThenByDescending(r => r.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        return await PurchaseDtoBuilder.MapReceiptsAsync(_db, receipts, cancellationToken);
    }
}

public record GetPurchaseReceiptByIdQuery(Guid Id) : IRequest<PurchaseReceiptDto>;

public class GetPurchaseReceiptByIdQueryHandler : IRequestHandler<GetPurchaseReceiptByIdQuery, PurchaseReceiptDto>
{
    private readonly IApplicationDbContext _db;
    public GetPurchaseReceiptByIdQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<PurchaseReceiptDto> Handle(GetPurchaseReceiptByIdQuery request, CancellationToken cancellationToken)
    {
        var receipt = await _db.PurchaseReceipts.AsNoTracking().Include(r => r.Lines)
            .FirstOrDefaultAsync(r => r.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("PurchaseReceipt", request.Id);

        var mapped = await PurchaseDtoBuilder.MapReceiptsAsync(_db, new[] { receipt }, cancellationToken);
        return mapped.Single();
    }
}

// -------------------------------------------------------- supplier invoices

/// <summary>Supplier invoices (spec sections 35 and 41), filterable by supplier and status.</summary>
public record GetSupplierInvoicesQuery(
    Guid? SupplierId = null,
    SupplierInvoiceStatus? Status = null,
    bool? OverdueOnly = null,
    DateTime? AsOf = null) : IRequest<List<SupplierInvoiceDto>>;

public class GetSupplierInvoicesQueryHandler : IRequestHandler<GetSupplierInvoicesQuery, List<SupplierInvoiceDto>>
{
    private readonly IApplicationDbContext _db;
    public GetSupplierInvoicesQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<List<SupplierInvoiceDto>> Handle(GetSupplierInvoicesQuery request, CancellationToken cancellationToken)
    {
        var query = _db.SupplierInvoices.AsNoTracking().Include(i => i.Lines).AsQueryable();

        if (request.SupplierId.HasValue) query = query.Where(i => i.SupplierId == request.SupplierId);
        if (request.Status.HasValue) query = query.Where(i => i.Status == request.Status);

        if (request.OverdueOnly == true)
        {
            var asOf = request.AsOf ?? DateTime.UtcNow;
            query = query
                .Where(i => i.Status == SupplierInvoiceStatus.Posted && i.DueDate < asOf);
        }

        var invoices = await query
            .OrderByDescending(i => i.InvoiceDate)
            .ThenByDescending(i => i.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        return await PurchaseDtoBuilder.MapInvoicesAsync(_db, invoices, cancellationToken);
    }
}

public record GetSupplierInvoiceByIdQuery(Guid Id) : IRequest<SupplierInvoiceDto>;

public class GetSupplierInvoiceByIdQueryHandler : IRequestHandler<GetSupplierInvoiceByIdQuery, SupplierInvoiceDto>
{
    private readonly IApplicationDbContext _db;
    public GetSupplierInvoiceByIdQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<SupplierInvoiceDto> Handle(GetSupplierInvoiceByIdQuery request, CancellationToken cancellationToken)
    {
        var invoice = await _db.SupplierInvoices.AsNoTracking().Include(i => i.Lines)
            .FirstOrDefaultAsync(i => i.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("SupplierInvoice", request.Id);

        var mapped = await PurchaseDtoBuilder.MapInvoicesAsync(_db, new[] { invoice }, cancellationToken);
        return mapped.Single();
    }
}

// ------------------------------------------------------ supplier statement

/// <summary>
/// Supplier account statement (spec sections 41 and 48). The balance is always a
/// live sum over the append-only ledger - there is no stored balance column.
/// </summary>
public record GetSupplierLedgerQuery(
    Guid SupplierId,
    DateTime? From = null,
    DateTime? To = null) : IRequest<List<SupplierLedgerEntryDto>>;

public class GetSupplierLedgerQueryHandler : IRequestHandler<GetSupplierLedgerQuery, List<SupplierLedgerEntryDto>>
{
    private readonly IApplicationDbContext _db;
    public GetSupplierLedgerQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<List<SupplierLedgerEntryDto>> Handle(GetSupplierLedgerQuery request, CancellationToken cancellationToken)
    {
        var supplier = await _db.Suppliers.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == request.SupplierId, cancellationToken)
            ?? throw new NotFoundException("Supplier", request.SupplierId);

        var query = _db.SupplierLedgerEntries.AsNoTracking()
            .Where(e => e.SupplierId == request.SupplierId);

        if (request.From.HasValue) query = query.Where(e => e.EntryDate >= request.From);
        if (request.To.HasValue) query = query.Where(e => e.EntryDate <= request.To);

        var entries = await query
            .OrderBy(e => e.EntryDate)
            .ThenBy(e => e.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        decimal running = 0;
        return entries.Select(e =>
        {
            running += e.Debit - e.Credit;
            return new SupplierLedgerEntryDto
            {
                Id = e.Id,
                SupplierId = e.SupplierId,
                SupplierCode = supplier.Code,
                SupplierName = supplier.Name,
                EntryDate = e.EntryDate,
                SourceDocumentType = e.SourceDocumentType.ToString(),
                SourceDocumentNumber = e.SourceDocumentNumber,
                SourceDocumentId = e.SourceDocumentId,
                Debit = e.Debit,
                Credit = e.Credit,
                Description = e.Description,
                CreatedBy = e.CreatedBy,
                CreatedAtUtc = e.CreatedAtUtc,
                RunningBalance = running
            };
        }).ToList();
    }
}

// ------------------------------------------------------- supplier balances

/// <summary>Outstanding supplier balances (spec sections 41 and 51) - what the factory still owes.</summary>
public record GetSupplierBalancesQuery(Guid? SupplierId = null, bool? WithBalanceOnly = null)
    : IRequest<List<SupplierBalanceDto>>;

public class GetSupplierBalancesQueryHandler : IRequestHandler<GetSupplierBalancesQuery, List<SupplierBalanceDto>>
{
    private readonly IApplicationDbContext _db;
    public GetSupplierBalancesQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<List<SupplierBalanceDto>> Handle(GetSupplierBalancesQuery request, CancellationToken cancellationToken)
    {
        var suppliers = await _db.Suppliers.AsNoTracking().ToListAsync(cancellationToken);
        if (request.SupplierId.HasValue)
            suppliers = suppliers.Where(s => s.Id == request.SupplierId).ToList();

        var ledger = await _db.SupplierLedgerEntries.AsNoTracking()
            .Select(e => new { e.SupplierId, e.Debit, e.Credit })
            .ToListAsync(cancellationToken);

        var openInvoices = await _db.SupplierInvoices.AsNoTracking()
            .Where(i => i.Status == SupplierInvoiceStatus.Posted)
            .Select(i => new { i.SupplierId, i.DueDate, i.SubTotal, i.Discount, i.Tax })
            .ToListAsync(cancellationToken);

        var today = DateTime.UtcNow.Date;

        var result = suppliers.Select(s =>
        {
            var rows = ledger.Where(l => l.SupplierId == s.Id).ToList();
            var invoiced = rows.Sum(r => r.Debit);
            var paid = rows.Sum(r => r.Credit);

            var invoices = openInvoices.Where(i => i.SupplierId == s.Id).ToList();

            return new SupplierBalanceDto
            {
                SupplierId = s.Id,
                SupplierCode = s.Code,
                SupplierName = s.Name,
                TotalInvoiced = invoiced,
                TotalPaid = paid,
                Outstanding = invoiced - paid,
                OpenInvoiceCount = invoices.Count,
                OverdueAmount = invoices.Where(i => i.DueDate.Date < today)
                    .Sum(i => i.SubTotal - i.Discount + i.Tax)
            };
        });

        if (request.WithBalanceOnly == true)
            result = result.Where(b => Math.Abs(b.Outstanding) > 0.001m);

        return result
            .OrderByDescending(b => b.Outstanding)
            .ThenBy(b => b.SupplierCode)
            .ToList();
    }
}
