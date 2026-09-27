using DyeHouseERP.Domain.Enums;

namespace DyeHouseERP.Application.FormationRequests.DTOs;

public class FormationGroupDto
{
    public Guid Id { get; set; }
    public int GroupNumber { get; set; }
    public string? Name { get; set; }
    public decimal PlannedQuantity { get; set; }
    public decimal ProducedQuantity { get; set; }
    public decimal RemainingQuantity { get; set; }
    public UnitOfMeasure Unit { get; set; }
    public int? TubCount { get; set; }
    public string? Color { get; set; }

    public decimal? WidthCm { get; set; }
    public decimal? MetersPerKg { get; set; }
    public decimal? Gsm { get; set; }
    public string? TubFormat { get; set; }
    public string? WindingTapeFormat { get; set; }

    public string? QualityInstructions { get; set; }
    public string? LabInstructions { get; set; }
    public string? InternalInstructions { get; set; }
    public string? CustomerInstructions { get; set; }
    public string? Notes { get; set; }

    /// <summary>Where this group's specification snapshot came from (specification master code/name), if a template was used.</summary>
    public Guid? SpecificationTemplateId { get; set; }
    public string? SpecificationTemplateName { get; set; }
    /// <summary>Set when the request was approved - proves the specification was frozen at that moment.</summary>
    public DateTime? SpecificationSnapshotAtUtc { get; set; }
}

public class FormationRequestDto
{
    public Guid Id { get; set; }
    public string RequestNumber { get; set; } = string.Empty;
    public DateTime RequestDate { get; set; }
    public Guid CustomerId { get; set; }
    public string CustomerCode { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public Guid ItemId { get; set; }
    public string ItemCode { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public Guid? RawMessageId { get; set; }
    public string? MessageNumber { get; set; }
    public decimal TotalQuantity { get; set; }
    public UnitOfMeasure Unit { get; set; }
    public string? Notes { get; set; }
    public FormationRequestStatus Status { get; set; }

    public Guid? ProductionOrderId { get; set; }
    public string? ProductionOrderNumber { get; set; }

    public string? SubmittedBy { get; set; }
    public DateTime? SubmittedAtUtc { get; set; }
    public string? ApprovedBy { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }
    public string? RejectedBy { get; set; }
    public DateTime? RejectedAtUtc { get; set; }
    public string? RejectionReason { get; set; }
    public string? CancelledBy { get; set; }
    public DateTime? CancelledAtUtc { get; set; }
    public string? CancellationReason { get; set; }

    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public string? ModifiedBy { get; set; }
    public DateTime? ModifiedAtUtc { get; set; }

    public decimal ProducedQuantity { get; set; }

    public List<FormationGroupDto> Groups { get; set; } = new();
}

/// <summary>
/// One step of the end-to-end chain (spec section 31): Customer -&gt; Raw Material Message -&gt; Formation Request -&gt;
/// Job Order -&gt; Production -&gt; Winding/Packing -&gt; Ready Goods -&gt; Delivery. Built as a real list of links so the UI
/// can move forward and backward through the chain.
/// </summary>
public class FormationTraceabilityLinkDto
{
    public string Stage { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;
    public string? Detail { get; set; }
    public string? Route { get; set; }
    public Guid? EntityId { get; set; }
    public DateTime? DateUtc { get; set; }
}

public class FormationTraceabilityDto
{
    public FormationRequestDto Request { get; set; } = new();
    public List<FormationTraceabilityLinkDto> Links { get; set; } = new();
}

/// <summary>Reusable specification master record (spec section 29 "cells") that can be selected on a new request.</summary>
public class FormationSpecTemplateDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public decimal? WidthCm { get; set; }
    public decimal? MetersPerKg { get; set; }
    public decimal? Gsm { get; set; }
    public string? TubFormat { get; set; }
    public string? WindingTapeFormat { get; set; }
    public string? Notes { get; set; }
    public string? QualityInstructions { get; set; }
    public string? LabInstructions { get; set; }
    public string? InternalInstructions { get; set; }
    public string? CustomerInstructions { get; set; }
    public bool IsActive { get; set; }
}
