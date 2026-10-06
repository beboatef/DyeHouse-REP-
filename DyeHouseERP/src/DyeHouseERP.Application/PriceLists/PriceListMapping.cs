using DyeHouseERP.Application.PriceLists.DTOs;
using DyeHouseERP.Domain.Entities;

namespace DyeHouseERP.Application.PriceLists;

/// <summary>
/// One place that turns a price-list row into its DTO, so the commands and the
/// queries cannot disagree about what a row means.
/// </summary>
internal static class PriceListMapping
{
    internal static StageCostRateDto ToDto(StageCostRate rate, ProductionStageDefinition? stage) => new()
    {
        Id = rate.Id,
        StageDefinitionId = rate.StageDefinitionId,
        StageCode = stage?.Code ?? string.Empty,
        StageName = stage?.Name ?? string.Empty,
        Unit = rate.Unit,
        CostPerUnit = rate.CostPerUnit,
        IsActive = rate.IsActive,
        Notes = rate.Notes
    };

    internal static CustomerServicePriceDto ToDto(
        CustomerServicePrice price, ProductionStageDefinition? stage, Customer? customer) => new()
    {
        Id = price.Id,
        StageDefinitionId = price.StageDefinitionId,
        StageCode = stage?.Code ?? string.Empty,
        StageName = stage?.Name ?? string.Empty,
        CustomerId = price.CustomerId,
        CustomerCode = customer?.Code,
        CustomerName = customer?.Name,
        IsGeneralDefault = price.IsGeneralDefault,
        Unit = price.Unit,
        PricePerUnit = price.PricePerUnit,
        IsActive = price.IsActive,
        Notes = price.Notes
    };

    internal static ProductionOrderServicePriceDto ToDto(
        ProductionOrderServicePrice snapshot, ProductionStageDefinition? stage, Customer? customer) => new()
    {
        Id = snapshot.Id,
        ProductionOrderId = snapshot.ProductionOrderId,
        StageDefinitionId = snapshot.StageDefinitionId,
        StageCode = stage?.Code ?? string.Empty,
        StageName = stage?.Name ?? string.Empty,
        Unit = snapshot.Unit,
        PricePerUnit = snapshot.PricePerUnit,
        IsOverride = snapshot.IsOverride,
        SourceCustomerId = snapshot.SourceCustomerId,
        SourceCustomerName = snapshot.SourceCustomerId is null ? null : customer?.Name,
        PreviousPricePerUnit = snapshot.PreviousPricePerUnit,
        OverrideReason = snapshot.OverrideReason,
        PricedBy = snapshot.PricedBy,
        PricedAtUtc = snapshot.PricedAtUtc
    };
}
