using DyeHouseERP.Domain.Enums;

namespace DyeHouseERP.Application.RawExternalReleases.DTOs;

public class RawExternalReleaseDto
{
    public Guid Id { get; set; }
    public string ReleaseNumber { get; set; } = string.Empty;
    public DateTime ReleaseDate { get; set; }
    public Guid CustomerId { get; set; }
    public string CustomerCode { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public Guid ItemId { get; set; }
    public string ItemCode { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public Guid RawMessageId { get; set; }
    public string MessageNumber { get; set; } = string.Empty;
    public decimal? QuantityKg { get; set; }
    public decimal? QuantityMeter { get; set; }
    public RawReleaseReason Reason { get; set; }
    public string? ExternalParty { get; set; }
    public string? Notes { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
}
