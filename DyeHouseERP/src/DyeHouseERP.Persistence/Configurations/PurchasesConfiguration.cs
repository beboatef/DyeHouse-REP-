using DyeHouseERP.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DyeHouseERP.Persistence.Configurations;

public class PurchaseOrderConfiguration : IEntityTypeConfiguration<PurchaseOrder>
{
    public void Configure(EntityTypeBuilder<PurchaseOrder> builder)
    {
        builder.ToTable("PurchaseOrders");
        builder.HasKey(o => o.Id);

        builder.Property(o => o.OrderNumber).HasMaxLength(30).IsRequired();
        builder.HasIndex(o => o.OrderNumber).IsUnique();

        builder.Property(o => o.Status).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(o => o.Notes).HasMaxLength(2000);
        builder.Property(o => o.SubmittedBy).HasMaxLength(100);
        builder.Property(o => o.ApprovedBy).HasMaxLength(100);
        builder.Property(o => o.CancelledBy).HasMaxLength(100);
        builder.Property(o => o.CancellationReason).HasMaxLength(1000);
        builder.Property(o => o.CreatedBy).HasMaxLength(100).IsRequired();
        builder.Property(o => o.ModifiedBy).HasMaxLength(100);

        // Derived members - never columns that could drift from the lines.
        builder.Ignore(o => o.TotalValue);
        builder.Ignore(o => o.IsEditable);

        builder.HasIndex(o => o.SupplierId);
        builder.HasIndex(o => o.WarehouseId);
        builder.HasIndex(o => o.Status);
        builder.HasIndex(o => o.OrderDate);

        builder.Metadata.FindNavigation(nameof(PurchaseOrder.Lines))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(o => o.Lines)
            .WithOne()
            .HasForeignKey(l => l.PurchaseOrderId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class PurchaseOrderLineConfiguration : IEntityTypeConfiguration<PurchaseOrderLine>
{
    public void Configure(EntityTypeBuilder<PurchaseOrderLine> builder)
    {
        builder.ToTable("PurchaseOrderLines");
        builder.HasKey(l => l.Id);

        builder.Property(l => l.Quantity).HasPrecision(18, 3);
        builder.Property(l => l.ReceivedQuantity).HasPrecision(18, 3);
        builder.Property(l => l.UnitPrice).HasPrecision(18, 4);
        builder.Property(l => l.Unit).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(l => l.Notes).HasMaxLength(1000);

        builder.Ignore(l => l.LineValue);
        builder.Ignore(l => l.OutstandingQuantity);

        builder.HasIndex(l => l.MaterialId);
        builder.HasIndex(l => l.PurchaseOrderId);

        // Data-level guard mirroring the domain rule (spec section 55).
        builder.ToTable(t => t.HasCheckConstraint(
            "CK_PurchaseOrderLines_QuantityPositive",
            "[Quantity] > 0 AND [ReceivedQuantity] >= 0 AND [ReceivedQuantity] <= [Quantity]"));
    }
}

public class PurchaseReceiptConfiguration : IEntityTypeConfiguration<PurchaseReceipt>
{
    public void Configure(EntityTypeBuilder<PurchaseReceipt> builder)
    {
        builder.ToTable("PurchaseReceipts");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.ReceiptNumber).HasMaxLength(30).IsRequired();
        builder.HasIndex(r => r.ReceiptNumber).IsUnique();

        builder.Property(r => r.ReceivedBy).HasMaxLength(100).IsRequired();
        builder.Property(r => r.SupplierDocumentNumber).HasMaxLength(60);
        builder.Property(r => r.Notes).HasMaxLength(2000);
        builder.Property(r => r.CreatedBy).HasMaxLength(100).IsRequired();
        builder.Property(r => r.ModifiedBy).HasMaxLength(100);

        builder.Ignore(r => r.TotalValue);

        builder.HasIndex(r => r.SupplierId);
        builder.HasIndex(r => r.WarehouseId);
        builder.HasIndex(r => r.PurchaseOrderId);
        builder.HasIndex(r => r.ReceiptDate);

        builder.Metadata.FindNavigation(nameof(PurchaseReceipt.Lines))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(r => r.Lines)
            .WithOne()
            .HasForeignKey(l => l.PurchaseReceiptId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class PurchaseReceiptLineConfiguration : IEntityTypeConfiguration<PurchaseReceiptLine>
{
    public void Configure(EntityTypeBuilder<PurchaseReceiptLine> builder)
    {
        builder.ToTable("PurchaseReceiptLines");
        builder.HasKey(l => l.Id);

        builder.Property(l => l.Quantity).HasPrecision(18, 3);
        builder.Property(l => l.UnitCost).HasPrecision(18, 4);
        builder.Property(l => l.Unit).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(l => l.Notes).HasMaxLength(1000);

        builder.Ignore(l => l.LineValue);

        builder.HasIndex(l => l.MaterialId);
        builder.HasIndex(l => l.PurchaseReceiptId);
        builder.HasIndex(l => l.PurchaseOrderLineId);

        builder.ToTable(t => t.HasCheckConstraint("CK_PurchaseReceiptLines_QuantityPositive", "[Quantity] > 0"));
    }
}

public class SupplierInvoiceConfiguration : IEntityTypeConfiguration<SupplierInvoice>
{
    public void Configure(EntityTypeBuilder<SupplierInvoice> builder)
    {
        builder.ToTable("SupplierInvoices");
        builder.HasKey(i => i.Id);

        builder.Property(i => i.InvoiceNumber).HasMaxLength(60).IsRequired();
        builder.Property(i => i.InternalNumber).HasMaxLength(40);
        builder.Property(i => i.Currency).HasMaxLength(10).IsRequired();
        builder.Property(i => i.Status).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(i => i.SubTotal).HasPrecision(18, 4);
        builder.Property(i => i.Discount).HasPrecision(18, 4);
        builder.Property(i => i.Tax).HasPrecision(18, 4);
        builder.Property(i => i.Notes).HasMaxLength(2000);
        builder.Property(i => i.PostedBy).HasMaxLength(100);
        builder.Property(i => i.CancelledBy).HasMaxLength(100);
        builder.Property(i => i.CancellationReason).HasMaxLength(1000);
        builder.Property(i => i.CreatedBy).HasMaxLength(100).IsRequired();
        builder.Property(i => i.ModifiedBy).HasMaxLength(100);

        builder.Ignore(i => i.Total);
        builder.Ignore(i => i.IsEditable);

        // A supplier's own invoice number is unique per supplier, not globally.
        builder.HasIndex(i => new { i.SupplierId, i.InvoiceNumber }).IsUnique();
        builder.HasIndex(i => i.PurchaseOrderId);
        builder.HasIndex(i => i.PurchaseReceiptId);
        builder.HasIndex(i => i.InvoiceDate);
        builder.HasIndex(i => i.DueDate);

        builder.Metadata.FindNavigation(nameof(SupplierInvoice.Lines))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(i => i.Lines)
            .WithOne()
            .HasForeignKey(l => l.SupplierInvoiceId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class SupplierInvoiceLineConfiguration : IEntityTypeConfiguration<SupplierInvoiceLine>
{
    public void Configure(EntityTypeBuilder<SupplierInvoiceLine> builder)
    {
        builder.ToTable("SupplierInvoiceLines");
        builder.HasKey(l => l.Id);

        builder.Property(l => l.Description).HasMaxLength(300).IsRequired();
        builder.Property(l => l.Quantity).HasPrecision(18, 3);
        builder.Property(l => l.UnitPrice).HasPrecision(18, 4);
        builder.Property(l => l.Unit).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(l => l.Notes).HasMaxLength(1000);

        builder.Ignore(l => l.LineValue);

        builder.HasIndex(l => l.MaterialId);
        builder.HasIndex(l => l.SupplierInvoiceId);

        builder.ToTable(t => t.HasCheckConstraint("CK_SupplierInvoiceLines_QuantityPositive", "[Quantity] > 0"));
    }
}

public class SupplierPaymentConfiguration : IEntityTypeConfiguration<SupplierPayment>
{
    public void Configure(EntityTypeBuilder<SupplierPayment> builder)
    {
        builder.ToTable("SupplierPayments");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.PaymentNumber).HasMaxLength(30).IsRequired();
        builder.HasIndex(p => p.PaymentNumber).IsUnique();

        builder.Property(p => p.Amount).HasPrecision(18, 4);
        builder.Property(p => p.Currency).HasMaxLength(10).IsRequired();
        builder.Property(p => p.PaymentMethod).HasMaxLength(60);
        builder.Property(p => p.CheckNumber).HasMaxLength(60);
        builder.Property(p => p.Description).HasMaxLength(1000);
        builder.Property(p => p.CreatedBy).HasMaxLength(100).IsRequired();
        builder.Property(p => p.ModifiedBy).HasMaxLength(100);

        builder.HasIndex(p => p.SupplierId);
        builder.HasIndex(p => p.TreasuryAccountId);
        builder.HasIndex(p => p.CheckId);
        builder.HasIndex(p => p.SupplierInvoiceId);
        builder.HasIndex(p => p.PaymentDate);

        builder.ToTable(t => t.HasCheckConstraint("CK_SupplierPayments_AmountPositive", "[Amount] > 0"));
    }
}

public class SupplierLedgerEntryConfiguration : IEntityTypeConfiguration<SupplierLedgerEntry>
{
    public void Configure(EntityTypeBuilder<SupplierLedgerEntry> builder)
    {
        builder.ToTable("SupplierLedgerEntries");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.SourceDocumentNumber).HasMaxLength(40).IsRequired();
        builder.Property(e => e.SourceDocumentType).HasConversion<string>().HasMaxLength(40).IsRequired();
        builder.Property(e => e.Debit).HasPrecision(18, 4);
        builder.Property(e => e.Credit).HasPrecision(18, 4);
        builder.Property(e => e.Description).HasMaxLength(300);
        builder.Property(e => e.CreatedBy).HasMaxLength(100).IsRequired();

        builder.HasIndex(e => e.SupplierId);
        builder.HasIndex(e => e.EntryDate);
        builder.HasIndex(e => e.SourceDocumentId);
    }
}
