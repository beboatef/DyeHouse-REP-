using DyeHouseERP.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DyeHouseERP.Persistence.Configurations;

public class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.ToTable("Customers");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Code).HasMaxLength(30).IsRequired();
        builder.Property(c => c.Name).HasMaxLength(200).IsRequired();
        // AccountNumber is assigned in the Customer constructor (mirrors Code);
        // a SQL DEFAULT cannot reference another column ([Code]), so no
        // HasDefaultValueSql here - the value always arrives from the domain layer.
        builder.Property(c => c.AccountNumber).HasMaxLength(50).IsRequired();
        builder.HasIndex(c => c.AccountNumber).IsUnique();

        // Contact/tax details (spec section 7). All optional and nullable, so every
        // existing customer row stays valid without a data backfill.
        builder.Property(c => c.Phone).HasMaxLength(50);
        builder.Property(c => c.Address).HasMaxLength(500);
        builder.Property(c => c.ContactPerson).HasMaxLength(200);
        builder.Property(c => c.TaxNumber).HasMaxLength(50);
        builder.Property(c => c.CreatedBy).HasMaxLength(100).IsRequired();
        builder.Property(c => c.ModifiedBy).HasMaxLength(100);

        builder.HasIndex(c => c.Code).IsUnique();

        // Explicit primary -> sub-customer relationship (spec section 7). Self-referencing FK,
        // Restrict so a parent with sub-customers cannot be hard-deleted out from under them.
        builder.HasOne(c => c.ParentCustomer)
            .WithMany()
            .HasForeignKey(c => c.ParentCustomerId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(c => c.ParentCustomerId);

        // Data-level guard mirroring the domain rule: a customer is never its own parent.
        builder.ToTable(t => t.HasCheckConstraint(
            "CK_Customers_ParentNotSelf",
            "[ParentCustomerId] IS NULL OR [ParentCustomerId] <> [Id]"));
    }
}
