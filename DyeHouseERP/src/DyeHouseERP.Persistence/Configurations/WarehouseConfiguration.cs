using DyeHouseERP.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DyeHouseERP.Persistence.Configurations;

public class WarehouseConfiguration : IEntityTypeConfiguration<Warehouse>
{
    public void Configure(EntityTypeBuilder<Warehouse> builder)
    {
        builder.ToTable("Warehouses");
        builder.HasKey(w => w.Id);

        builder.Property(w => w.Code).HasMaxLength(30).IsRequired();
        builder.Property(w => w.Name).HasMaxLength(200).IsRequired();
        builder.Property(w => w.Kind).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(w => w.CreatedBy).HasMaxLength(100).IsRequired();
        builder.Property(w => w.ModifiedBy).HasMaxLength(100);

        builder.HasIndex(w => w.Code).IsUnique();
    }
}
