using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.Purchases.DTOs;
using DyeHouseERP.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.Purchases.Queries;

/// <summary>
/// Maps purchase aggregates to DTOs. Derived amounts (line value, order total,
/// invoice total) are deliberately computed here after loading rather than inside
/// a LINQ-to-Entities projection, because they are unmapped domain members that
/// EF cannot translate. Lookups are batched into dictionaries to avoid N+1.
/// </summary>
internal static class PurchaseDtoBuilder
{
    public static async Task<List<PurchaseOrderDto>> MapOrdersAsync(
        IApplicationDbContext db, IReadOnlyCollection<PurchaseOrder> orders, CancellationToken cancellationToken)
    {
        if (orders.Count == 0) return new List<PurchaseOrderDto>();

        var supplierIds = orders.Select(o => o.SupplierId).Distinct().ToList();
        var warehouseIds = orders.Select(o => o.WarehouseId).Distinct().ToList();
        var materialIds = orders.SelectMany(o => o.Lines).Select(l => l.MaterialId).Distinct().ToList();

        var suppliers = await db.Suppliers.AsNoTracking().Where(s => supplierIds.Contains(s.Id))
            .Select(s => new { s.Id, s.Code, s.Name })
            .ToDictionaryAsync(s => s.Id, s => (s.Code, s.Name), cancellationToken);

        var warehouses = await db.Warehouses.AsNoTracking().Where(w => warehouseIds.Contains(w.Id))
            .ToDictionaryAsync(w => w.Id, w => w.Name, cancellationToken);

        var materials = await db.Materials.AsNoTracking().Where(m => materialIds.Contains(m.Id))
            .Select(m => new { m.Id, m.Code, m.Name })
            .ToDictionaryAsync(m => m.Id, m => (m.Code, m.Name), cancellationToken);

        return orders.Select(o =>
        {
            var supplier = suppliers.TryGetValue(o.SupplierId, out var s) ? s : (Code: "", Name: "");
            var lines = o.Lines.OrderBy(l => l.MaterialId).Select(l => new PurchaseOrderLineDto
            {
                Id = l.Id,
                MaterialId = l.MaterialId,
                MaterialCode = materials.TryGetValue(l.MaterialId, out var m) ? m.Code : string.Empty,
                MaterialName = materials.TryGetValue(l.MaterialId, out var m2) ? m2.Name : string.Empty,
                Quantity = l.Quantity,
                Unit = l.Unit,
                UnitPrice = l.UnitPrice,
                LineValue = l.LineValue,
                ReceivedQuantity = l.ReceivedQuantity,
                OutstandingQuantity = l.OutstandingQuantity,
                Notes = l.Notes
            }).ToList();

            return new PurchaseOrderDto
            {
                Id = o.Id,
                OrderNumber = o.OrderNumber,
                OrderDate = o.OrderDate,
                SupplierId = o.SupplierId,
                SupplierCode = supplier.Code,
                SupplierName = supplier.Name,
                WarehouseId = o.WarehouseId,
                WarehouseName = warehouses.GetValueOrDefault(o.WarehouseId) ?? string.Empty,
                ExpectedDeliveryDate = o.ExpectedDeliveryDate,
                Notes = o.Notes,
                Status = o.Status,
                TotalValue = o.TotalValue,
                ReceivedValue = lines.Sum(l => l.ReceivedQuantity * l.UnitPrice),
                SubmittedBy = o.SubmittedBy,
                SubmittedAtUtc = o.SubmittedAtUtc,
                ApprovedBy = o.ApprovedBy,
                ApprovedAtUtc = o.ApprovedAtUtc,
                CancelledBy = o.CancelledBy,
                CancellationReason = o.CancellationReason,
                CreatedAtUtc = o.CreatedAtUtc,
                CreatedBy = o.CreatedBy,
                Lines = lines
            };
        }).ToList();
    }

    public static async Task<List<PurchaseReceiptDto>> MapReceiptsAsync(
        IApplicationDbContext db, IReadOnlyCollection<PurchaseReceipt> receipts, CancellationToken cancellationToken)
    {
        if (receipts.Count == 0) return new List<PurchaseReceiptDto>();

        var supplierIds = receipts.Select(r => r.SupplierId).Distinct().ToList();
        var warehouseIds = receipts.Select(r => r.WarehouseId).Distinct().ToList();
        var materialIds = receipts.SelectMany(r => r.Lines).Select(l => l.MaterialId).Distinct().ToList();
        var orderIds = receipts.Where(r => r.PurchaseOrderId.HasValue)
            .Select(r => r.PurchaseOrderId!.Value).Distinct().ToList();

        var suppliers = await db.Suppliers.AsNoTracking().Where(s => supplierIds.Contains(s.Id))
            .Select(s => new { s.Id, s.Code, s.Name })
            .ToDictionaryAsync(s => s.Id, s => (s.Code, s.Name), cancellationToken);

        var warehouses = await db.Warehouses.AsNoTracking().Where(w => warehouseIds.Contains(w.Id))
            .ToDictionaryAsync(w => w.Id, w => w.Name, cancellationToken);

        var materials = await db.Materials.AsNoTracking().Where(m => materialIds.Contains(m.Id))
            .Select(m => new { m.Id, m.Code, m.Name })
            .ToDictionaryAsync(m => m.Id, m => (m.Code, m.Name), cancellationToken);

        var orderNumbers = await db.PurchaseOrders.AsNoTracking().Where(o => orderIds.Contains(o.Id))
            .ToDictionaryAsync(o => o.Id, o => o.OrderNumber, cancellationToken);

        return receipts.Select(r =>
        {
            var supplier = suppliers.TryGetValue(r.SupplierId, out var s) ? s : (Code: "", Name: "");

            return new PurchaseReceiptDto
            {
                Id = r.Id,
                ReceiptNumber = r.ReceiptNumber,
                ReceiptDate = r.ReceiptDate,
                SupplierId = r.SupplierId,
                SupplierCode = supplier.Code,
                SupplierName = supplier.Name,
                WarehouseId = r.WarehouseId,
                WarehouseName = warehouses.GetValueOrDefault(r.WarehouseId) ?? string.Empty,
                PurchaseOrderId = r.PurchaseOrderId,
                OrderNumber = r.PurchaseOrderId.HasValue ? orderNumbers.GetValueOrDefault(r.PurchaseOrderId.Value) : null,
                ReceivedBy = r.ReceivedBy,
                SupplierDocumentNumber = r.SupplierDocumentNumber,
                Notes = r.Notes,
                TotalValue = r.TotalValue,
                CreatedAtUtc = r.CreatedAtUtc,
                CreatedBy = r.CreatedBy,
                Lines = r.Lines.Select(l => new PurchaseReceiptLineDto
                {
                    Id = l.Id,
                    MaterialId = l.MaterialId,
                    MaterialCode = materials.TryGetValue(l.MaterialId, out var m) ? m.Code : string.Empty,
                    MaterialName = materials.TryGetValue(l.MaterialId, out var m2) ? m2.Name : string.Empty,
                    Quantity = l.Quantity,
                    Unit = l.Unit,
                    UnitCost = l.UnitCost,
                    LineValue = l.LineValue,
                    Notes = l.Notes
                }).ToList()
            };
        }).ToList();
    }

    public static async Task<List<SupplierInvoiceDto>> MapInvoicesAsync(
        IApplicationDbContext db, IReadOnlyCollection<SupplierInvoice> invoices, CancellationToken cancellationToken)
    {
        if (invoices.Count == 0) return new List<SupplierInvoiceDto>();

        var supplierIds = invoices.Select(i => i.SupplierId).Distinct().ToList();
        var materialIds = invoices.SelectMany(i => i.Lines)
            .Where(l => l.MaterialId.HasValue).Select(l => l.MaterialId!.Value).Distinct().ToList();
        var orderIds = invoices.Where(i => i.PurchaseOrderId.HasValue)
            .Select(i => i.PurchaseOrderId!.Value).Distinct().ToList();
        var receiptIds = invoices.Where(i => i.PurchaseReceiptId.HasValue)
            .Select(i => i.PurchaseReceiptId!.Value).Distinct().ToList();

        var suppliers = await db.Suppliers.AsNoTracking().Where(s => supplierIds.Contains(s.Id))
            .Select(s => new { s.Id, s.Code, s.Name })
            .ToDictionaryAsync(s => s.Id, s => (s.Code, s.Name), cancellationToken);

        var materials = await db.Materials.AsNoTracking().Where(m => materialIds.Contains(m.Id))
            .Select(m => new { m.Id, m.Code })
            .ToDictionaryAsync(m => m.Id, m => m.Code, cancellationToken);

        var orderNumbers = await db.PurchaseOrders.AsNoTracking().Where(o => orderIds.Contains(o.Id))
            .ToDictionaryAsync(o => o.Id, o => o.OrderNumber, cancellationToken);

        var receiptNumbers = await db.PurchaseReceipts.AsNoTracking().Where(r => receiptIds.Contains(r.Id))
            .ToDictionaryAsync(r => r.Id, r => r.ReceiptNumber, cancellationToken);

        return invoices.Select(i =>
        {
            var supplier = suppliers.TryGetValue(i.SupplierId, out var s) ? s : (Code: "", Name: "");

            return new SupplierInvoiceDto
            {
                Id = i.Id,
                InvoiceNumber = i.InvoiceNumber,
                InternalNumber = i.InternalNumber,
                InvoiceDate = i.InvoiceDate,
                DueDate = i.DueDate,
                SupplierId = i.SupplierId,
                SupplierCode = supplier.Code,
                SupplierName = supplier.Name,
                PurchaseOrderId = i.PurchaseOrderId,
                OrderNumber = i.PurchaseOrderId.HasValue ? orderNumbers.GetValueOrDefault(i.PurchaseOrderId.Value) : null,
                PurchaseReceiptId = i.PurchaseReceiptId,
                ReceiptNumber = i.PurchaseReceiptId.HasValue ? receiptNumbers.GetValueOrDefault(i.PurchaseReceiptId.Value) : null,
                SubTotal = i.SubTotal,
                Discount = i.Discount,
                Tax = i.Tax,
                Total = i.Total,
                Currency = i.Currency,
                Status = i.Status,
                Notes = i.Notes,
                PostedBy = i.PostedBy,
                PostedAtUtc = i.PostedAtUtc,
                CancellationReason = i.CancellationReason,
                CreatedAtUtc = i.CreatedAtUtc,
                CreatedBy = i.CreatedBy,
                Lines = i.Lines.Select(l => new SupplierInvoiceLineDto
                {
                    Id = l.Id,
                    MaterialId = l.MaterialId,
                    MaterialCode = l.MaterialId.HasValue ? materials.GetValueOrDefault(l.MaterialId.Value) : null,
                    Description = l.Description,
                    Quantity = l.Quantity,
                    Unit = l.Unit,
                    UnitPrice = l.UnitPrice,
                    LineValue = l.LineValue
                }).ToList()
            };
        }).ToList();
    }
}
