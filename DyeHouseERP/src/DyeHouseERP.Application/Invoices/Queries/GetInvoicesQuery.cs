using DyeHouseERP.Application.Common.Exceptions;
using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.Invoices.DTOs;
using DyeHouseERP.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.Invoices.Queries;

public record GetInvoicesQuery(Guid? CustomerId = null, InvoiceStatus? Status = null) : IRequest<List<InvoiceDto>>;
public record GetInvoiceByIdQuery(Guid Id) : IRequest<InvoiceDto>;

public class GetInvoicesQueryHandler : IRequestHandler<GetInvoicesQuery, List<InvoiceDto>>
{
    private readonly IApplicationDbContext _db;
    public GetInvoicesQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<List<InvoiceDto>> Handle(GetInvoicesQuery request, CancellationToken cancellationToken)
    {
        var query = _db.Invoices.AsNoTracking().AsQueryable();
        if (request.CustomerId.HasValue) query = query.Where(i => i.CustomerId == request.CustomerId);
        if (request.Status.HasValue) query = query.Where(i => i.Status == request.Status);

        var ids = await query.OrderByDescending(i => i.InvoiceDate).Select(i => i.Id).ToListAsync(cancellationToken);
        var result = new List<InvoiceDto>();
        foreach (var id in ids) result.Add(await LoadDtoAsync(_db, id, cancellationToken));
        return result;
    }

    internal static async Task<InvoiceDto> LoadDtoAsync(IApplicationDbContext db, Guid id, CancellationToken cancellationToken)
    {
        var invoice = await db.Invoices.AsNoTracking().FirstOrDefaultAsync(i => i.Id == id, cancellationToken)
            ?? throw new NotFoundException("Invoice", id);
        var lines = await db.InvoiceLines.AsNoTracking().Where(l => l.InvoiceId == id).ToListAsync(cancellationToken);

        var customer = await db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == invoice.CustomerId, cancellationToken);
        var orderIds = lines.Where(l => l.ProductionOrderId.HasValue).Select(l => l.ProductionOrderId!.Value).Distinct().ToList();
        var orders = await db.ProductionOrders.AsNoTracking().Where(o => orderIds.Contains(o.Id)).ToDictionaryAsync(o => o.Id, cancellationToken);
        var itemIds = lines.Select(l => l.ItemId).Distinct().ToList();
        var items = await db.Items.AsNoTracking().Where(i => itemIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, cancellationToken);

        var subTotal = lines.Sum(l => l.Quantity * l.ProcessingPrice);

        return new InvoiceDto
        {
            Id = invoice.Id, InvoiceNumber = invoice.InvoiceNumber, InvoiceDate = invoice.InvoiceDate,
            CustomerId = invoice.CustomerId, CustomerCode = customer?.Code ?? "", CustomerName = customer?.Name ?? "",
            Status = invoice.Status, Discount = invoice.Discount, Tax = invoice.Tax,
            SubTotal = subTotal, Total = subTotal - invoice.Discount + invoice.Tax, Notes = invoice.Notes,
            Lines = lines.Select(l => new InvoiceLineDto
            {
                Id = l.Id, ProductionOrderId = l.ProductionOrderId,
                ProductionOrderNumber = l.ProductionOrderId.HasValue ? orders.GetValueOrDefault(l.ProductionOrderId.Value)?.OrderNumber : null,
                ItemId = l.ItemId, ItemCode = items.GetValueOrDefault(l.ItemId)?.Code ?? "", Color = l.Color,
                Quantity = l.Quantity, ProcessingPrice = l.ProcessingPrice, Value = l.Quantity * l.ProcessingPrice
            }).ToList()
        };
    }
}

public class GetInvoiceByIdQueryHandler : IRequestHandler<GetInvoiceByIdQuery, InvoiceDto>
{
    private readonly IApplicationDbContext _db;
    public GetInvoiceByIdQueryHandler(IApplicationDbContext db) => _db = db;
    public Task<InvoiceDto> Handle(GetInvoiceByIdQuery request, CancellationToken cancellationToken)
        => GetInvoicesQueryHandler.LoadDtoAsync(_db, request.Id, cancellationToken);
}
