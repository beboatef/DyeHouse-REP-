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
    }
}
