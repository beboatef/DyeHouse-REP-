using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.FormationRequests.DTOs;
using DyeHouseERP.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.FormationRequests.Queries;

/// <summary>
/// One projection used by the list, detail and traceability queries, so a formation request always looks the same
/// on every screen. Names come from the real customer/item/message/JO records - never duplicated onto the request -
/// which is what keeps the chain Customer -&gt; Message -&gt; Formation Request -&gt; Job Order navigable (spec section 31).
/// </summary>
internal static class FormationRequestDtoBuilder
{
    public static async Task<List<FormationRequestDto>> BuildManyAsync(
        IApplicationDbContext db, List<FormationRequest> requests, CancellationToken cancellationToken)
    {
        var customerIds = requests.Select(r => r.CustomerId).Distinct().ToList();
        var itemIds = requests.Select(r => r.ItemId).Distinct().ToList();
        var messageIds = requests.Where(r => r.RawMessageId.HasValue).Select(r => r.RawMessageId!.Value).Distinct().ToList();
        var orderIds = requests.Where(r => r.ProductionOrderId.HasValue).Select(r => r.ProductionOrderId!.Value).Distinct().ToList();

        var customers = await db.Customers.AsNoTracking()
            .Where(c => customerIds.Contains(c.Id))
            .Select(c => new { c.Id, c.Code, c.Name })
            .ToDictionaryAsync(c => c.Id, c => (c.Code, c.Name), cancellationToken);

        var items = await db.Items.AsNoTracking()
            .Where(i => itemIds.Contains(i.Id))
            .Select(i => new { i.Id, i.Code, i.Name })
            .ToDictionaryAsync(i => i.Id, i => (i.Code, i.Name), cancellationToken);

        var messages = await db.RawMessages.AsNoTracking()
            .Where(m => messageIds.Contains(m.Id))
            .Select(m => new { m.Id, m.MessageNumber })
            .ToDictionaryAsync(m => m.Id, m => m.MessageNumber, cancellationToken);

        var orders = await db.ProductionOrders.AsNoTracking()
            .Where(o => orderIds.Contains(o.Id))
            .Select(o => new { o.Id, o.OrderNumber })
            .ToDictionaryAsync(o => o.Id, o => o.OrderNumber, cancellationToken);

        return requests.Select(r =>
        {
            var customer = customers.TryGetValue(r.CustomerId, out var c) ? c : (Code: "", Name: "");
            var item = items.TryGetValue(r.ItemId, out var i) ? i : (Code: "", Name: "");

            return new FormationRequestDto
            {
                Id = r.Id,
                RequestNumber = r.RequestNumber,
                RequestDate = r.RequestDate,
                CustomerId = r.CustomerId,
                CustomerCode = customer.Code,
                CustomerName = customer.Name,
                ItemId = r.ItemId,
                ItemCode = item.Code,
                ItemName = item.Name,
                RawMessageId = r.RawMessageId,
                MessageNumber = r.RawMessageId.HasValue && messages.TryGetValue(r.RawMessageId.Value, out var num) ? num : null,
                TotalQuantity = r.TotalQuantity,
                Unit = r.Unit,
                Notes = r.Notes,
                Status = r.Status,
                ProductionOrderId = r.ProductionOrderId,
                ProductionOrderNumber = r.ProductionOrderId.HasValue && orders.TryGetValue(r.ProductionOrderId.Value, out var orderNumber) ? orderNumber : null,
                SubmittedBy = r.SubmittedBy,
                SubmittedAtUtc = r.SubmittedAtUtc,
                ApprovedBy = r.ApprovedBy,
                ApprovedAtUtc = r.ApprovedAtUtc,
                RejectedBy = r.RejectedBy,
                RejectedAtUtc = r.RejectedAtUtc,
                RejectionReason = r.RejectionReason,
                CancelledBy = r.CancelledBy,
                CancelledAtUtc = r.CancelledAtUtc,
                CancellationReason = r.CancellationReason,
                CreatedBy = r.CreatedBy,
                CreatedAtUtc = r.CreatedAtUtc,
                ModifiedBy = r.ModifiedBy,
                ModifiedAtUtc = r.ModifiedAtUtc,
                ProducedQuantity = r.Groups.Sum(g => g.ProducedQuantity),
                BasinCount = r.Groups.Sum(g => g.Basins.Count),
                Groups = r.Groups.OrderBy(g => g.GroupNumber).Select(MapGroup).ToList()
            };
        }).ToList();
    }

    public static async Task<FormationRequestDto> BuildAsync(
        IApplicationDbContext db, FormationRequest request, CancellationToken cancellationToken)
    {
        var list = await BuildManyAsync(db, new List<FormationRequest> { request }, cancellationToken);
        return list[0];
    }

    private static FormationGroupDto MapGroup(FormationGroup g) => new()
    {
        Id = g.Id,
        GroupNumber = g.GroupNumber,
        Name = g.Name,
        PlannedQuantity = g.PlannedQuantity,
        ProducedQuantity = g.ProducedQuantity,
        RemainingQuantity = g.RemainingQuantity,
        Unit = g.Unit,
        TubCount = g.TubCount,
        Color = g.Color,
        PlannedBasinQuantity = g.PlannedBasinQuantity,
        ProducedBasinQuantity = g.ProducedBasinQuantity,
        Basins = g.Basins.OrderBy(b => b.BasinNumber).Select(MapBasin).ToList(),
        WidthCm = g.WidthCm,
        MetersPerKg = g.MetersPerKg,
        Gsm = g.Gsm,
        TubFormat = g.TubFormat,
        WindingTapeFormat = g.WindingTapeFormat,
        QualityInstructions = g.QualityInstructions,
        LabInstructions = g.LabInstructions,
        InternalInstructions = g.InternalInstructions,
        CustomerInstructions = g.CustomerInstructions,
        Notes = g.Notes,
        SpecificationTemplateId = g.SpecificationTemplateId,
        SpecificationTemplateName = g.SpecificationTemplateName,
        SpecificationSnapshotAtUtc = g.SpecificationSnapshotAtUtc
    };

    private static FormationBasinDto MapBasin(FormationBasin b) => new()
    {
        Id = b.Id,
        FormationGroupId = b.FormationGroupId,
        BasinNumber = b.BasinNumber,
        Name = b.Name,
        PlannedQuantity = b.PlannedQuantity,
        ProducedQuantity = b.ProducedQuantity,
        RemainingQuantity = b.RemainingQuantity,
        Unit = b.Unit,
        TubCount = b.TubCount,
        Color = b.Color,
        WidthCm = b.WidthCm,
        MetersPerKg = b.MetersPerKg,
        Gsm = b.Gsm,
        TubFormat = b.TubFormat,
        WindingTapeFormat = b.WindingTapeFormat,
        QualityInstructions = b.QualityInstructions,
        LabInstructions = b.LabInstructions,
        InternalInstructions = b.InternalInstructions,
        CustomerInstructions = b.CustomerInstructions,
        Notes = b.Notes,
        SpecificationTemplateId = b.SpecificationTemplateId,
        SpecificationTemplateName = b.SpecificationTemplateName,
        SpecificationSnapshotAtUtc = b.SpecificationSnapshotAtUtc
    };
}
