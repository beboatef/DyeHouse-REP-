using DyeHouseERP.Domain.Common;

namespace DyeHouseERP.Domain.Entities;

public class ReadyGoodsSource : BaseEntity
{
    private ReadyGoodsSource() { }

    public ReadyGoodsSource(
        Guid readyGoodsTransferId,
        Guid rawMessageId,
        Guid itemId,
        decimal? quantityKg,
        decimal? quantityMeter)
    {
        if (quantityKg is null && quantityMeter is null)
            throw new ArgumentException("KG and/or Meter quantity is required.");

        ReadyGoodsTransferId = readyGoodsTransferId;
        RawMessageId = rawMessageId;
        ItemId = itemId;
        QuantityKg = quantityKg;
        QuantityMeter = quantityMeter;
    }

    public Guid ReadyGoodsTransferId { get; private set; }
    public Guid RawMessageId { get; private set; }
    public Guid ItemId { get; private set; }
    public decimal? QuantityKg { get; private set; }
    public decimal? QuantityMeter { get; private set; }

    public ReadyGoodsTransfer ReadyGoodsTransfer { get; private set; } = null!;
}
