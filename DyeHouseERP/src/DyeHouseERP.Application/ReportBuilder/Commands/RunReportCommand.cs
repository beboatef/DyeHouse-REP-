using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.ReportBuilder.DTOs;
using DyeHouseERP.Application.ReportBuilder.Queries;
using DyeHouseERP.Domain.Exceptions;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.ReportBuilder.Commands;

/// <summary>
/// Executes a report against ONE whitelisted entity with ONLY whitelisted
/// columns (spec section 36 - never raw/dynamic SQL). Filters are a fixed,
/// typed shape (ReportFilters), not free-form text, so there is no
/// injection surface at all - this is a safe subset builder, not a query
/// console.
/// </summary>
public record RunReportCommand(string EntityKey, List<string> Columns, ReportFilters? Filters) : IRequest<ReportResultDto>;

public class RunReportCommandValidator : AbstractValidator<RunReportCommand>
{
    public RunReportCommandValidator()
    {
        RuleFor(x => x.EntityKey).NotEmpty().Must(k => ReportableEntitiesRegistry.Entities.ContainsKey(k))
            .WithMessage("Unknown or non-whitelisted entity.");
        RuleFor(x => x.Columns).NotEmpty();
        RuleForEach(x => x.Columns).Must((command, column) =>
            ReportableEntitiesRegistry.Entities.TryGetValue(command.EntityKey, out var entity) && entity.Columns.Contains(column))
            .WithMessage("Column is not whitelisted for this entity.");
    }
}

public class RunReportCommandHandler : IRequestHandler<RunReportCommand, ReportResultDto>
{
    private readonly IApplicationDbContext _db;
    public RunReportCommandHandler(IApplicationDbContext db) => _db = db;

    public async Task<ReportResultDto> Handle(RunReportCommand request, CancellationToken cancellationToken)
    {
        var filters = request.Filters ?? new ReportFilters();

        var allRows = request.EntityKey switch
        {
            "ProductionOrders" => await BuildProductionOrdersAsync(filters, cancellationToken),
            "Invoices" => await BuildInvoicesAsync(filters, cancellationToken),
            "Customers" => await BuildCustomersAsync(filters, cancellationToken),
            _ => throw new DomainException("Unknown reportable entity.")
        };

        var projected = allRows.Select(row => request.Columns.Select(c => row.GetValueOrDefault(c)).ToList()).ToList();

        return new ReportResultDto { Headers = request.Columns, Rows = projected };
    }

    private async Task<List<Dictionary<string, string?>>> BuildProductionOrdersAsync(ReportFilters filters, CancellationToken ct)
    {
        var query = _db.ProductionOrders.AsNoTracking().AsQueryable();
        if (filters.CustomerId.HasValue) query = query.Where(o => o.CustomerId == filters.CustomerId);
        if (filters.ItemId.HasValue) query = query.Where(o => o.ItemId == filters.ItemId);
        if (filters.From.HasValue) query = query.Where(o => o.OrderDate >= filters.From);
        if (filters.To.HasValue) query = query.Where(o => o.OrderDate <= filters.To);

        var orders = await query.ToListAsync(ct);
        var customers = await _db.Customers.AsNoTracking().Where(c => orders.Select(o => o.CustomerId).Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct);
        var items = await _db.Items.AsNoTracking().Where(i => orders.Select(o => o.ItemId).Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct);

        return orders.Select(o => new Dictionary<string, string?>
        {
            ["OrderNumber"] = o.OrderNumber,
            ["CustomerCode"] = customers.GetValueOrDefault(o.CustomerId)?.Code,
            ["ItemCode"] = items.GetValueOrDefault(o.ItemId)?.Code,
            ["Color"] = o.Color,
            ["Status"] = o.Status.ToString(),
            ["Priority"] = o.Priority.ToString(),
            ["OrderDate"] = o.OrderDate.ToString("yyyy-MM-dd"),
            ["RequestedQuantityKg"] = o.RequestedQuantityKg?.ToString(),
            ["RequestedQuantityMeter"] = o.RequestedQuantityMeter?.ToString()
        }).ToList();
    }

    private async Task<List<Dictionary<string, string?>>> BuildInvoicesAsync(ReportFilters filters, CancellationToken ct)
    {
        var query = _db.Invoices.AsNoTracking().AsQueryable();
        if (filters.CustomerId.HasValue) query = query.Where(i => i.CustomerId == filters.CustomerId);
        if (filters.From.HasValue) query = query.Where(i => i.InvoiceDate >= filters.From);
        if (filters.To.HasValue) query = query.Where(i => i.InvoiceDate <= filters.To);

        var invoices = await query.ToListAsync(ct);
        var customers = await _db.Customers.AsNoTracking().Where(c => invoices.Select(i => i.CustomerId).Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct);
        var invoiceIds = invoices.Select(i => i.Id).ToList();
        var lines = await _db.InvoiceLines.AsNoTracking().Where(l => invoiceIds.Contains(l.InvoiceId)).ToListAsync(ct);

        return invoices.Select(i =>
        {
            var subTotal = lines.Where(l => l.InvoiceId == i.Id).Sum(l => l.Quantity * l.ProcessingPrice);
            return new Dictionary<string, string?>
            {
                ["InvoiceNumber"] = i.InvoiceNumber,
                ["CustomerCode"] = customers.GetValueOrDefault(i.CustomerId)?.Code,
                ["InvoiceDate"] = i.InvoiceDate.ToString("yyyy-MM-dd"),
                ["Status"] = i.Status.ToString(),
                ["SubTotal"] = subTotal.ToString(),
                ["Discount"] = i.Discount.ToString(),
                ["Tax"] = i.Tax.ToString(),
                ["Total"] = (subTotal - i.Discount + i.Tax).ToString()
            };
        }).ToList();
    }

    private async Task<List<Dictionary<string, string?>>> BuildCustomersAsync(ReportFilters filters, CancellationToken ct)
    {
        var customers = await _db.Customers.AsNoTracking().ToListAsync(ct);
        return customers.Select(c => new Dictionary<string, string?>
        {
            ["Code"] = c.Code,
            ["Name"] = c.Name,
            ["IsActive"] = c.IsActive.ToString()
        }).ToList();
    }
}
