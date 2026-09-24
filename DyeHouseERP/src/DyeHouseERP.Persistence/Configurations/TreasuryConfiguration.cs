using DyeHouseERP.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DyeHouseERP.Persistence.Configurations;

public class TreasuryAccountConfiguration : IEntityTypeConfiguration<TreasuryAccount>
{
    public void Configure(EntityTypeBuilder<TreasuryAccount> builder)
    {
        builder.ToTable("TreasuryAccounts");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Code).HasMaxLength(30).IsRequired();
        builder.Property(a => a.Name).HasMaxLength(200).IsRequired();
        builder.Property(a => a.Kind).HasConversion<string>().HasMaxLength(20);
        builder.Property(a => a.CreatedBy).HasMaxLength(100).IsRequired();
        builder.Property(a => a.ModifiedBy).HasMaxLength(100);
        builder.HasIndex(a => a.Code).IsUnique();
    }
}

public class TreasuryTransactionConfiguration : IEntityTypeConfiguration<TreasuryTransaction>
{
    public void Configure(EntityTypeBuilder<TreasuryTransaction> builder)
    {
        builder.ToTable("TreasuryTransactions");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.SourceDocumentType).HasConversion<string>().HasMaxLength(40).IsRequired();
        builder.Property(t => t.SourceDocumentNumber).HasMaxLength(30).IsRequired();
        builder.Property(t => t.Amount).HasPrecision(18, 2);
        builder.Property(t => t.Direction).HasConversion<string>().HasMaxLength(10);
        builder.Property(t => t.Description).HasMaxLength(500);
        builder.Property(t => t.CreatedBy).HasMaxLength(100).IsRequired();
        builder.HasIndex(t => t.TreasuryAccountId);
    }
}

public class ReceiptConfiguration : IEntityTypeConfiguration<Receipt>
{
    public void Configure(EntityTypeBuilder<Receipt> builder)
    {
        builder.ToTable("Receipts");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.ReceiptNumber).HasMaxLength(30).IsRequired();
        builder.HasIndex(r => r.ReceiptNumber).IsUnique();
        builder.Property(r => r.Amount).HasPrecision(18, 2);
        builder.Property(r => r.PaymentMethod).HasMaxLength(50);
        builder.Property(r => r.Description).HasMaxLength(500);
        builder.Property(r => r.CreatedBy).HasMaxLength(100).IsRequired();
        builder.Property(r => r.ModifiedBy).HasMaxLength(100);
        builder.HasIndex(r => r.CustomerId);
        builder.HasIndex(r => r.InvoiceId);
    }
}

public class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("Payments");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.PaymentNumber).HasMaxLength(30).IsRequired();
        builder.HasIndex(p => p.PaymentNumber).IsUnique();
        builder.Property(p => p.Amount).HasPrecision(18, 2);
        builder.Property(p => p.PayeeDescription).HasMaxLength(200).IsRequired();
        builder.Property(p => p.PaymentMethod).HasMaxLength(50);
        builder.Property(p => p.Description).HasMaxLength(500);
        builder.Property(p => p.CreatedBy).HasMaxLength(100).IsRequired();
        builder.Property(p => p.ModifiedBy).HasMaxLength(100);
    }
}

public class TreasuryTransferConfiguration : IEntityTypeConfiguration<TreasuryTransfer>
{
    public void Configure(EntityTypeBuilder<TreasuryTransfer> builder)
    {
        builder.ToTable("TreasuryTransfers");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.TransferNumber).HasMaxLength(30).IsRequired();
        builder.HasIndex(t => t.TransferNumber).IsUnique();
        builder.Property(t => t.Amount).HasPrecision(18, 2);
        builder.Property(t => t.Description).HasMaxLength(500);
        builder.Property(t => t.CreatedBy).HasMaxLength(100).IsRequired();
        builder.Property(t => t.ModifiedBy).HasMaxLength(100);
    }
}
