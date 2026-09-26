using DyeHouseERP.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DyeHouseERP.Persistence.Configurations;

public sealed class ReadyGoodsSourceConfiguration : IEntityTypeConfiguration<ReadyGoodsSource>
{
    public void Configure(EntityTypeBuilder<ReadyGoodsSource> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.QuantityKg).HasPrecision(18, 3);
        builder.Property(x => x.QuantityMeter).HasPrecision(18, 3);

        builder.HasOne(x => x.ReadyGoodsTransfer)
            .WithMany()
            .HasForeignKey(x => x.ReadyGoodsTransferId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => new { x.ReadyGoodsTransferId, x.RawMessageId });
    }
}
