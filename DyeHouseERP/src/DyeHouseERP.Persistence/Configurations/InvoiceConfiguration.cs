using DyeHouseERP.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DyeHouseERP.Persistence.Configurations;

public class InvoiceConfiguration : IEntityTypeConfiguration<Invoice>
{
    public void Configure(EntityTypeBuilder<Invoice> builder)
    {
        builder.ToTable("Invoices");
        builder.HasKey(i => i.Id);
        builder.Property(i => i.InvoiceNumber).HasMaxLength(30).IsRequired();
        builder.HasIndex(i => i.InvoiceNumber).IsUnique();
        builder.Property(i => i.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(i => i.Discount).HasPrecision(18, 2);
        builder.Property(i => i.Tax).HasPrecision(18, 2);
        builder.Property(i => i.Notes).HasMaxLength(2000);
        builder.Property(i => i.CreatedBy).HasMaxLength(100).IsRequired();
        builder.Property(i => i.ModifiedBy).HasMaxLength(100);
        builder.Ignore(i => i.SubTotal);
        builder.Ignore(i => i.Total);

        builder.Metadata.FindNavigation(nameof(Invoice.Lines))!.SetPropertyAccessMode(PropertyAccessMode.Field);
        builder.HasMany(i => i.Lines).WithOne().HasForeignKey(l => l.InvoiceId).OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(i => i.CustomerId);
        builder.HasIndex(i => i.Status);
    }
}

public class InvoiceLineConfiguration : IEntityTypeConfiguration<InvoiceLine>
{
    public void Configure(EntityTypeBuilder<InvoiceLine> builder)
    {
        builder.ToTable("InvoiceLines");
        builder.HasKey(l => l.Id);
        builder.Property(l => l.Color).HasMaxLength(100);
        builder.Property(l => l.Quantity).HasPrecision(18, 3);
        builder.Property(l => l.ProcessingPrice).HasPrecision(18, 4);
        builder.Property(l => l.Notes).HasMaxLength(1000);
        builder.Ignore(l => l.Value);
    }
}

public class CustomerLedgerEntryConfiguration : IEntityTypeConfiguration<CustomerLedgerEntry>
{
    public void Configure(EntityTypeBuilder<CustomerLedgerEntry> builder)
    {
        builder.ToTable("CustomerLedgerEntries");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.SourceDocumentType).HasConversion<string>().HasMaxLength(40).IsRequired();
        builder.Property(e => e.SourceDocumentNumber).HasMaxLength(30).IsRequired();
        builder.Property(e => e.Debit).HasPrecision(18, 2);
        builder.Property(e => e.Credit).HasPrecision(18, 2);
        builder.Property(e => e.Description).HasMaxLength(500).IsRequired();
        builder.Property(e => e.CreatedBy).HasMaxLength(100).IsRequired();
        builder.HasIndex(e => e.CustomerId);
        builder.HasIndex(e => e.EntryDate);
    }
}
