using DyeHouseERP.Domain.Common;
using DyeHouseERP.Domain.Enums;
using DyeHouseERP.Domain.Exceptions;

namespace DyeHouseERP.Domain.Entities;

/// <summary>
/// Formation Request - طلب تشكيل (spec sections 28-33).
///
/// The real-world case this models: a customer delivers e.g. 5 tons of yarn
/// and asks for several different formations out of it - 6 tubs x 500 KG in
/// one specification, 8 x 250 KG in another, each with its own width, colour,
/// meter-per-kg, tub format and instructions. One request therefore owns MANY
/// groups/cells, each with its own specification.
///
/// This is a planning/authorisation document: it moves no stock by itself.
/// Stock only moves when the grouped material is actually issued to the Job
/// Order it is converted into, through the normal inventory ledger.
/// </summary>
public class FormationRequest : AuditableEntity
{
    public string RequestNumber { get; private set; } = string.Empty; // system-generated, e.g. "FRM-2026-000014"
    public DateTime RequestDate { get; private set; }
    public Guid CustomerId { get; private set; }
    public Guid ItemId { get; private set; }

    /// <summary>Customer-owned raw material message this request draws from (spec section 31). Required before submission.</summary>
    public Guid? RawMessageId { get; private set; }

    public decimal TotalQuantity { get; private set; }
    public UnitOfMeasure Unit { get; private set; }
    public string? Notes { get; private set; }
    public FormationRequestStatus Status { get; private set; } = FormationRequestStatus.Draft;

    /// <summary>Set when an approved request is converted into a Job Order (spec section 16/31).</summary>
    public Guid? ProductionOrderId { get; private set; }

    public string? SubmittedBy { get; private set; }
    public DateTime? SubmittedAtUtc { get; private set; }
    public string? ApprovedBy { get; private set; }
    public DateTime? ApprovedAtUtc { get; private set; }
    public string? RejectedBy { get; private set; }
    public DateTime? RejectedAtUtc { get; private set; }
    public string? RejectionReason { get; private set; }
    public string? CancelledBy { get; private set; }
    public DateTime? CancelledAtUtc { get; private set; }
    public string? CancellationReason { get; private set; }

    private readonly List<FormationGroup> _groups = new();
    public IReadOnlyCollection<FormationGroup> Groups => _groups.AsReadOnly();

    private FormationRequest() { } // EF Core

    public FormationRequest(
        string requestNumber, DateTime requestDate, Guid customerId, Guid itemId,
        UnitOfMeasure unit, string createdBy, Guid? rawMessageId = null,
        decimal totalQuantity = 0, string? notes = null)
    {
        if (string.IsNullOrWhiteSpace(requestNumber))
            throw new ArgumentException("Formation request number is required.", nameof(requestNumber));
        if (customerId == Guid.Empty) throw new ArgumentException("Customer is required.", nameof(customerId));
        if (itemId == Guid.Empty) throw new ArgumentException("Item is required.", nameof(itemId));
        if (totalQuantity < 0) throw new ArgumentException("Total quantity cannot be negative.", nameof(totalQuantity));

        RequestNumber = requestNumber;
        RequestDate = requestDate;
        CustomerId = customerId;
        ItemId = itemId;
        Unit = unit;
        RawMessageId = rawMessageId;
        TotalQuantity = totalQuantity;
        Notes = notes;
        CreatedBy = createdBy;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public bool IsEditable => Status is FormationRequestStatus.Draft or FormationRequestStatus.Rejected;

    /// <summary>Header fields may only change while the request is still a draft/rejected (never after it was submitted or approved).</summary>
    public void UpdateHeader(DateTime requestDate, Guid? rawMessageId, string? notes, decimal totalQuantity, string modifiedBy)
    {
        EnsureEditable();
        if (totalQuantity < 0) throw new ArgumentException("Total quantity cannot be negative.", nameof(totalQuantity));

        RequestDate = requestDate;
        RawMessageId = rawMessageId;
        Notes = notes;
        TotalQuantity = totalQuantity;
        Touch(modifiedBy);
    }

    // ---------------------------------------------------------------- groups

    public FormationGroup AddGroup(
        string? name, decimal plannedQuantity, UnitOfMeasure unit, int? tubCount, string? color,
        string? notes, string createdBy)
    {
        EnsureEditable();
        if (plannedQuantity <= 0) throw new DomainException("A formation group quantity must be greater than zero.");
        if (tubCount is < 0) throw new DomainException("The number of tubs cannot be negative.");

        var group = new FormationGroup(
            Id, _groups.Count + 1, name, plannedQuantity, unit, tubCount, color, notes);
        _groups.Add(group);
        Touch(createdBy);
        return group;
    }

    /// <summary>Applies every field of one group. Only allowed while the request is editable, so an approved request's cells can never drift.</summary>
    public FormationGroup UpdateGroup(
        Guid groupId, string? name, decimal plannedQuantity, UnitOfMeasure unit, int? tubCount, string? color,
        decimal? widthCm, decimal? metersPerKg, decimal? gsm, string? tubFormat, string? windingTapeFormat,
        string? qualityInstructions, string? labInstructions, string? internalInstructions,
        string? customerInstructions, string? notes, string modifiedBy)
    {
        EnsureEditable();
        var group = FindGroup(groupId);

        if (plannedQuantity <= 0) throw new DomainException("A formation group quantity must be greater than zero.");

        group.Update(name, plannedQuantity, unit, tubCount, color, notes);
        group.SetSpecification(widthCm, metersPerKg, gsm, tubFormat, windingTapeFormat);
        group.SetInstructions(qualityInstructions, labInstructions, internalInstructions, customerInstructions);
        Touch(modifiedBy);
        return group;
    }

    /// <summary>Removes a group - only from a draft (an approved/completed request's groups are history and stay until the request is cancelled).</summary>
    public void RemoveGroup(Guid groupId, string modifiedBy)
    {
        EnsureEditable();

        var group = FindGroup(groupId);
        if (group.ProducedQuantity > 0)
            throw new DomainException("A group that has already produced quantity cannot be removed.");

        _groups.Remove(group);
        RenumberGroups(modifiedBy);
    }

    /// <summary>
    /// Selects a reusable specification template onto one group (spec section 29). The template's values are COPIED
    /// onto the group - that copy is the snapshot, so later edits to the template never change this request.
    /// Basins under that group receive their own copy of the same snapshot, so a basin can still be
    /// adjusted individually without ever losing the record of where its specification came from.
    /// </summary>
    public void ApplySpecificationTemplate(Guid groupId, FormationSpecTemplate template, string modifiedBy)
    {
        EnsureEditable();
        var group = FindGroup(groupId);

        group.ApplySpecificationSnapshot(template);
        group.ApplySpecificationSnapshotToBasins(template);
        Touch(modifiedBy);
    }

    // ----------------------------------------------------------- basins (spec sections 10-11)

    /// <summary>
    /// Adds one basin / detail under a group (spec sections 10-11). The real-world case: one customer order
    /// arrives as several basins - 500 KG, 750 KG, 620 KG, 400 KG - and each basin is planned, produced,
    /// costed and traced on its own. The basin is NOT a second group: it lives inside the group it belongs to,
    /// keeps the group's cell as its parent, and the group keeps its own quantity as the sum of its basins.
    /// </summary>
    public FormationBasin AddBasin(
        Guid groupId, string? name, decimal plannedQuantity, UnitOfMeasure unit,
        int? tubCount, string? color, string? notes, string createdBy)
    {
        EnsureEditable();
        var basin = FindGroup(groupId).AddBasin(name, plannedQuantity, unit, tubCount, color, notes);
        Touch(createdBy);
        return basin;
    }

    public FormationBasin UpdateBasin(
        Guid groupId, Guid basinId, string? name, decimal plannedQuantity, UnitOfMeasure unit,
        int? tubCount, string? color, decimal? widthCm, decimal? metersPerKg, decimal? gsm,
        string? tubFormat, string? windingTapeFormat, string? qualityInstructions, string? labInstructions,
        string? internalInstructions, string? customerInstructions, string? notes, string modifiedBy)
    {
        EnsureEditable();
        var basin = FindGroup(groupId).UpdateBasin(
            basinId, name, plannedQuantity, unit, tubCount, color, widthCm, metersPerKg, gsm,
            tubFormat, windingTapeFormat, qualityInstructions, labInstructions,
            internalInstructions, customerInstructions, notes);
        Touch(modifiedBy);
        return basin;
    }

    /// <summary>Removes a basin - only while the request is still editable, and never once it produced quantity.</summary>
    public void RemoveBasin(Guid groupId, Guid basinId, string modifiedBy)
    {
        EnsureEditable();
        FindGroup(groupId).RemoveBasin(basinId);
        Touch(modifiedBy);
    }

    /// <summary>
    /// Records production against ONE basin. The group's produced quantity is re-derived from its basins
    /// immediately afterwards, so the group and the request can never report more than the basins hold.
    /// </summary>
    public void RecordBasinProduction(Guid groupId, Guid basinId, decimal quantity, string modifiedBy)
    {
        if (quantity <= 0) throw new DomainException("A production quantity must be greater than zero.");
        if (Status is FormationRequestStatus.Draft or FormationRequestStatus.Submitted or FormationRequestStatus.Rejected)
            throw new DomainException("Production cannot be recorded against a request that has not been approved.");
        if (Status == FormationRequestStatus.Cancelled)
            throw new DomainException("Production cannot be recorded against a cancelled request.");

        FindGroup(groupId).AddBasinProducedQuantity(basinId, quantity);
        RecalculateCompletion();
        Touch(modifiedBy);
    }

    private FormationGroup FindGroup(Guid groupId) => _groups.FirstOrDefault(g => g.Id == groupId)
        ?? throw new DomainException($"Formation group ({groupId}) does not belong to request {RequestNumber}.");

    // -------------------------------------------------------------- workflow

    public void Submit(string submittedBy)
    {
        if (Status is not (FormationRequestStatus.Draft or FormationRequestStatus.Rejected))
            throw new DomainException("Only a draft or rejected formation request can be submitted.");

        if (_groups.Count == 0)
            throw new DomainException("A formation request must contain at least one group/cell before it can be submitted.");

        if (!RawMessageId.HasValue)
            throw new DomainException("A raw material message must be selected before submitting - customer-owned raw material must stay traceable (spec section 31).");

        var groupTotal = _groups.Sum(g => g.PlannedQuantity);
        if (Math.Abs(groupTotal - TotalQuantity) > 0.001m)
            throw new DomainException(
                $"The total quantity ({TotalQuantity}) must equal the sum of the group quantities ({groupTotal}).");

        Status = FormationRequestStatus.Submitted;
        SubmittedBy = submittedBy;
        SubmittedAtUtc = DateTime.UtcNow;
        RejectionReason = null;
        Touch(submittedBy);
    }

    /// <summary>
    /// Approves the request and freezes its specification snapshot (spec section 29). From this point the cells are
    /// historical: changing a specification template in master data can never silently rewrite this request.
    /// </summary>
    public void Approve(string approvedBy)
    {
        if (Status != FormationRequestStatus.Submitted)
            throw new DomainException("Only a submitted formation request can be approved.");

        Status = FormationRequestStatus.Approved;
        ApprovedBy = approvedBy;
        ApprovedAtUtc = DateTime.UtcNow;
        RejectedBy = null;
        RejectedAtUtc = null;
        RejectionReason = null;

        var snapshotAt = ApprovedAtUtc.Value;
        foreach (var group in _groups)
        {
            group.FreezeSpecificationSnapshot(snapshotAt);
            group.FreezeBasinSnapshots(snapshotAt);
        }

        Touch(approvedBy);
    }

    public void Reject(string rejectedBy, string reason)
    {
        if (Status != FormationRequestStatus.Submitted)
            throw new DomainException("Only a submitted formation request can be rejected.");
        if (string.IsNullOrWhiteSpace(reason))
            throw new DomainException("A rejection reason is required.");

        Status = FormationRequestStatus.Rejected;
        RejectedBy = rejectedBy;
        RejectedAtUtc = DateTime.UtcNow;
        RejectionReason = reason.Trim();
        Touch(rejectedBy);
    }

    public void Cancel(string cancelledBy, string reason)
    {
        if (Status is FormationRequestStatus.Completed or FormationRequestStatus.Cancelled)
            throw new DomainException("A completed or already-cancelled formation request cannot be cancelled.");
        if (string.IsNullOrWhiteSpace(reason))
            throw new DomainException("A cancellation reason is required.");
        if (Status == FormationRequestStatus.Approved && ProductionOrderId.HasValue)
            throw new DomainException("An approved request that was already converted to a Job Order cannot be cancelled here - cancel the production order instead.");

        Status = FormationRequestStatus.Cancelled;
        CancelledBy = cancelledBy;
        CancelledAtUtc = DateTime.UtcNow;
        CancellationReason = reason.Trim();
        Touch(cancelledBy);
    }

    public void StartProduction(string modifiedBy)
    {
        if (Status != FormationRequestStatus.Approved)
            throw new DomainException("Only an approved formation request can move into production.");

        Status = FormationRequestStatus.InProgress;
        Touch(modifiedBy);
    }

    /// <summary>
    /// Links a Job Order converted from this request (spec sections 16 and 31).
    ///
    /// The header keeps the FIRST Job Order it produced; the per-group and per-basin links live on the Job
    /// Orders themselves (FormationRequestId / FormationGroupId / FormationBasinId). That is what allows one
    /// request to fan out into several Job Orders - one per group or per basin - without the header having to
    /// remember them all, and without any quantity being planned twice.
    /// </summary>
    public void LinkProductionOrder(Guid productionOrderId, string modifiedBy)
    {
        if (Status is not (FormationRequestStatus.Approved or FormationRequestStatus.InProgress))
            throw new DomainException("A Job Order can only be created from an approved formation request.");
        if (ProductionOrderId.HasValue && ProductionOrderId.Value != productionOrderId)
        {
            // The header keeps the FIRST Job Order this request produced. Additional per-group / per-basin
            // Job Orders are linked on the orders themselves, so one request can fan out into several
            // without the header losing track of where it started - and without any quantity being planned twice.
            Touch(modifiedBy);
            return;
        }

        ProductionOrderId = productionOrderId;
        if (Status == FormationRequestStatus.Approved)
            Status = FormationRequestStatus.InProgress;
        Touch(modifiedBy);
    }

    /// <summary>
    /// Records production against one group and recalculates the request's completion status (spec section 32:
    /// Approved / In Progress / Partially Completed / Completed). Quantities are accumulated, never overwritten, so
    /// the same physical quantity can never be counted twice.
    /// </summary>
    public void RecordGroupProduction(Guid groupId, decimal quantity, string modifiedBy)
    {
        if (quantity <= 0) throw new DomainException("A production quantity must be greater than zero.");
        if (Status is FormationRequestStatus.Draft or FormationRequestStatus.Submitted or FormationRequestStatus.Rejected)
            throw new DomainException("Production cannot be recorded against a request that has not been approved.");
        if (Status == FormationRequestStatus.Cancelled)
            throw new DomainException("Production cannot be recorded against a cancelled request.");

        var group = FindGroup(groupId);

        group.AddProducedQuantity(quantity);
        RecalculateCompletion();
        Touch(modifiedBy);
    }

    public void RecalculateCompletion()
    {
        if (Status is FormationRequestStatus.Draft or FormationRequestStatus.Submitted
            or FormationRequestStatus.Rejected or FormationRequestStatus.Cancelled)
            return;

        var produced = _groups.Sum(g => g.ProducedQuantity);
        var planned = _groups.Sum(g => g.PlannedQuantity);

        Status = (produced <= 0, planned > 0 && produced + 0.001m >= planned) switch
        {
            (true, _) => FormationRequestStatus.Approved,
            (_, true) => FormationRequestStatus.Completed,
            _ => FormationRequestStatus.PartiallyCompleted
        };
    }

    private void EnsureEditable()
    {
        if (!IsEditable)
            throw new DocumentLockedException("Formation Request", RequestNumber);
    }

    /// <summary>
    /// Re-numbers the groups 1..N in their current order. Called after any add/remove so the
    /// (request, group number) pair stays unique and the printed document reads in a stable order.
    /// </summary>
    public void RenumberGroups(string modifiedBy)
    {
        var number = 1;
        foreach (var group in _groups.OrderBy(g => g.GroupNumber).ToList())
            group.SetGroupNumber(number++);
        Touch(modifiedBy);
    }

    private void Touch(string modifiedBy)
    {
        ModifiedBy = modifiedBy;
        ModifiedAtUtc = DateTime.UtcNow;
    }
}

/// <summary>
/// One group / cell / line inside a Formation Request (spec section 30). A request can carry as many of these as
/// the customer needs (e.g. "6 tubs x 500 KG, width 160, 5 m/kg" and "8 tubs x 250 KG, width 200, 8 m/kg").
///
/// Every specification field on this row is the SNAPSHOT of whatever template (or manual entry) was used - the
/// template is only ever read once, at selection time.
/// </summary>
public class FormationGroup : BaseEntity
{
    public Guid FormationRequestId { get; private set; }
    public int GroupNumber { get; private set; }
    public string? Name { get; private set; }
    public decimal PlannedQuantity { get; private set; }
    public UnitOfMeasure Unit { get; private set; }
    public int? TubCount { get; private set; }
    public string? Color { get; private set; }

    public decimal? WidthCm { get; private set; }
    public decimal? MetersPerKg { get; private set; }
    public decimal? Gsm { get; private set; }
    public string? TubFormat { get; private set; }
    public string? WindingTapeFormat { get; private set; }

    public string? QualityInstructions { get; private set; }
    public string? LabInstructions { get; private set; }
    public string? InternalInstructions { get; private set; }
    public string? CustomerInstructions { get; private set; }
    public string? Notes { get; private set; }

    /// <summary>Quantity actually produced for this group so far (accumulated, never overwritten).</summary>
    public decimal ProducedQuantity { get; private set; }

    /// <summary>Which specification master record this group's snapshot came from, if any.</summary>
    public Guid? SpecificationTemplateId { get; private set; }
    public string? SpecificationTemplateName { get; private set; }
    public DateTime? SpecificationSnapshotAtUtc { get; private set; }

    /// <summary>
    /// The basins / details inside this group (spec sections 10-11): 500 KG, 750 KG, 620 KG, 400 KG ...
    /// A group with no basins behaves exactly as it always did - its own quantity and specification ARE the
    /// basin, so nothing about the existing model or its historical rows changes.
    /// </summary>
    private readonly List<FormationBasin> _basins = new();
    public IReadOnlyCollection<FormationBasin> Basins => _basins.AsReadOnly();
    public bool HasBasins => _basins.Count > 0;

    /// <summary>Total planned across the basins; falls back to the group's own quantity when it has none.</summary>
    public decimal PlannedBasinQuantity => _basins.Count > 0 ? _basins.Sum(b => b.PlannedQuantity) : PlannedQuantity;

    /// <summary>Total produced across the basins; falls back to the group's own produced quantity when it has none.</summary>
    public decimal ProducedBasinQuantity => _basins.Count > 0 ? _basins.Sum(b => b.ProducedQuantity) : ProducedQuantity;

    public decimal RemainingBasinQuantity => PlannedBasinQuantity - ProducedBasinQuantity;

    private FormationGroup() { } // EF Core

    internal FormationGroup(
        Guid formationRequestId, int groupNumber, string? name, decimal plannedQuantity,
        UnitOfMeasure unit, int? tubCount, string? color, string? notes)
    {
        FormationRequestId = formationRequestId;
        GroupNumber = groupNumber;
        Name = name;
        PlannedQuantity = plannedQuantity;
        Unit = unit;
        TubCount = tubCount;
        Color = color;
        Notes = notes;
    }

    internal void SetGroupNumber(int groupNumber) => GroupNumber = groupNumber;

    internal void Update(string? name, decimal plannedQuantity, UnitOfMeasure unit, int? tubCount, string? color, string? notes)
    {
        Name = name;

        // Once a group carries basins its quantity IS their sum - a typed-in figure must never be able to
        // disagree with them, or the request total and the basin totals would drift apart.
        if (_basins.Count == 0) PlannedQuantity = plannedQuantity;

        Unit = unit;
        TubCount = tubCount;
        Color = color;
        Notes = notes;
    }

    internal void SetSpecification(
        decimal? widthCm, decimal? metersPerKg, decimal? gsm, string? tubFormat, string? windingTapeFormat)
    {
        if (widthCm is < 0) throw new DomainException("Width cannot be negative.");
        if (metersPerKg is < 0) throw new DomainException("Meter per KG cannot be negative.");
        if (gsm is < 0) throw new DomainException("Weight per square meter cannot be negative.");

        WidthCm = widthCm;
        MetersPerKg = metersPerKg;
        Gsm = gsm;
        TubFormat = tubFormat;
        WindingTapeFormat = windingTapeFormat;
    }

    internal void SetInstructions(string? quality, string? lab, string? internalInstructions, string? customer)
    {
        QualityInstructions = quality;
        LabInstructions = lab;
        InternalInstructions = internalInstructions;
        CustomerInstructions = customer;
    }

    /// <summary>
    /// Copies a specification template's values onto this group and remembers where they came from. This IS the
    /// snapshot required by spec section 29 - template edits afterwards cannot reach back into this row.
    /// </summary>
    internal void ApplySpecificationSnapshot(FormationSpecTemplate template)
    {
        SpecificationTemplateId = template.Id;
        SpecificationTemplateName = string.IsNullOrWhiteSpace(template.NameEn) ? template.NameAr : template.NameEn;

        WidthCm = template.WidthCm ?? WidthCm;
        MetersPerKg = template.MetersPerKg ?? MetersPerKg;
        Gsm = template.Gsm ?? Gsm;
        TubFormat = template.TubFormat ?? TubFormat;
        WindingTapeFormat = template.WindingTapeFormat ?? WindingTapeFormat;

        QualityInstructions = template.QualityInstructions ?? QualityInstructions;
        LabInstructions = template.LabInstructions ?? LabInstructions;
        InternalInstructions = template.InternalInstructions ?? InternalInstructions;
        CustomerInstructions = template.CustomerInstructions ?? CustomerInstructions;
        Notes = template.Notes ?? Notes;
    }

    /// <summary>Marks the moment this group's specification was frozen (called on approval).</summary>
    internal void FreezeSpecificationSnapshot(DateTime snapshotAtUtc) => SpecificationSnapshotAtUtc = snapshotAtUtc;

    internal void AddProducedQuantity(decimal quantity)
    {
        if (_basins.Count > 0)
            throw new DomainException(
                "Production must be recorded against a basin of this group, not against the group itself - " +
                "recording it on both would count the same physical quantity twice.");

        ProducedQuantity += quantity;
    }

    // ---------------------------------------------------------------- basins (spec sections 10-11)

    internal FormationBasin AddBasin(
        string? name, decimal plannedQuantity, UnitOfMeasure unit, int? tubCount, string? color, string? notes)
    {
        if (plannedQuantity <= 0) throw new DomainException("A formation basin quantity must be greater than zero.");
        if (tubCount is < 0) throw new DomainException("The number of tubs cannot be negative.");

        var basin = new FormationBasin(Id, _basins.Count + 1, name, plannedQuantity, unit, tubCount, color, notes);

        // A new basin starts from the group's own cell specification and can then be adjusted on its own.
        basin.InheritFrom(this);
        _basins.Add(basin);
        SyncQuantitiesFromBasins();
        return basin;
    }

    internal FormationBasin UpdateBasin(
        Guid basinId, string? name, decimal plannedQuantity, UnitOfMeasure unit, int? tubCount, string? color,
        decimal? widthCm, decimal? metersPerKg, decimal? gsm, string? tubFormat, string? windingTapeFormat,
        string? qualityInstructions, string? labInstructions, string? internalInstructions,
        string? customerInstructions, string? notes)
    {
        if (plannedQuantity <= 0) throw new DomainException("A formation basin quantity must be greater than zero.");
        if (widthCm is < 0) throw new DomainException("Width cannot be negative.");
        if (metersPerKg is < 0) throw new DomainException("Meter per KG cannot be negative.");
        if (gsm is < 0) throw new DomainException("Weight per square meter cannot be negative.");

        var basin = FindBasin(basinId);
        basin.Update(name, plannedQuantity, unit, tubCount, color, notes);
        basin.SetSpecification(widthCm, metersPerKg, gsm, tubFormat, windingTapeFormat);
        basin.SetInstructions(qualityInstructions, labInstructions, internalInstructions, customerInstructions);
        SyncQuantitiesFromBasins();
        return basin;
    }

    internal void RemoveBasin(Guid basinId)
    {
        var basin = FindBasin(basinId);
        if (basin.ProducedQuantity > 0)
            throw new DomainException("A basin that has already produced quantity cannot be removed.");

        _basins.Remove(basin);
        RenumberBasins();
        SyncQuantitiesFromBasins();
    }

    internal void AddBasinProducedQuantity(Guid basinId, decimal quantity)
    {
        if (quantity <= 0) throw new DomainException("A production quantity must be greater than zero.");

        FindBasin(basinId).AddProducedQuantity(quantity);
        SyncQuantitiesFromBasins();
    }

    /// <summary>Copies one specification template onto every basin of this group (each keeps its own snapshot stamp).</summary>
    internal void ApplySpecificationSnapshotToBasins(FormationSpecTemplate template)
    {
        foreach (var basin in _basins)
            basin.ApplySpecificationSnapshot(template);
    }

    internal void FreezeBasinSnapshots(DateTime snapshotAtUtc)
    {
        foreach (var basin in _basins)
            basin.FreezeSpecificationSnapshot(snapshotAtUtc);
    }

    internal FormationBasin FindBasin(Guid basinId) => _basins.FirstOrDefault(b => b.Id == basinId)
        ?? throw new DomainException(
            $"Formation basin ({basinId}) does not belong to group {GroupNumber}.");

    internal void RenumberBasins()
    {
        var number = 1;
        foreach (var basin in _basins.OrderBy(b => b.BasinNumber).ToList())
            basin.SetBasinNumber(number++);
    }

    /// <summary>
    /// The single rule that keeps the three levels agreeing: while a group has basins, the group's planned and
    /// produced quantities ARE the sums of its basins. The request total, which already mirrors the groups,
    /// therefore stays correct with no further change - nothing is collapsed and nothing is counted twice.
    /// </summary>
    private void SyncQuantitiesFromBasins()
    {
        if (_basins.Count == 0) return;

        PlannedQuantity = _basins.Sum(b => b.PlannedQuantity);
        ProducedQuantity = _basins.Sum(b => b.ProducedQuantity);
    }

    public decimal RemainingQuantity => PlannedQuantity - ProducedQuantity;
}

/// <summary>
/// One BASIN / detail inside a Formation Group (spec sections 10-11).
///
/// The concrete case this exists for: a single customer order arrives as several basins -
/// 500 KG, 750 KG, 620 KG, 400 KG - and the factory needs each of them planned, specified,
/// produced, costed and traced on its own. Today one group row had to stand for all of them,
/// which forced the basins to be summed before they were ever recorded and lost which
/// quantity belonged to which basin.
///
/// A basin therefore owns its own quantity, its own tub count / colour, its own copy of the
/// specification snapshot and its own produced quantity. Its parent group stays exactly what it
/// was - the cell - and carries the sum of its basins, so the existing group concept, the request
/// total and every historical group row keep working untouched.
///
/// Like the group, this is a snapshot: the specification is copied once (from the template or from
/// the group) and is frozen when the request is approved, so later master-data edits can never
/// rewrite an approved plan. Units are never converted between KG and Meter anywhere in here.
/// </summary>
public class FormationBasin : BaseEntity
{
    public Guid FormationGroupId { get; private set; }
    public int BasinNumber { get; private set; }
    public string? Name { get; private set; }
    public decimal PlannedQuantity { get; private set; }
    public UnitOfMeasure Unit { get; private set; }
    public int? TubCount { get; private set; }
    public string? Color { get; private set; }

    public decimal? WidthCm { get; private set; }
    public decimal? MetersPerKg { get; private set; }
    public decimal? Gsm { get; private set; }
    public string? TubFormat { get; private set; }
    public string? WindingTapeFormat { get; private set; }

    public string? QualityInstructions { get; private set; }
    public string? LabInstructions { get; private set; }
    public string? InternalInstructions { get; private set; }
    public string? CustomerInstructions { get; private set; }
    public string? Notes { get; private set; }

    /// <summary>Quantity actually produced for THIS basin (accumulated, never overwritten).</summary>
    public decimal ProducedQuantity { get; private set; }

    public Guid? SpecificationTemplateId { get; private set; }
    public string? SpecificationTemplateName { get; private set; }
    public DateTime? SpecificationSnapshotAtUtc { get; private set; }

    public decimal RemainingQuantity => PlannedQuantity - ProducedQuantity;

    private FormationBasin() { } // EF Core

    internal FormationBasin(
        Guid formationGroupId, int basinNumber, string? name, decimal plannedQuantity,
        UnitOfMeasure unit, int? tubCount, string? color, string? notes)
    {
        FormationGroupId = formationGroupId;
        BasinNumber = basinNumber;
        Name = name;
        PlannedQuantity = plannedQuantity;
        Unit = unit;
        TubCount = tubCount;
        Color = color;
        Notes = notes;
    }

    internal void SetBasinNumber(int basinNumber) => BasinNumber = basinNumber;

    internal void Update(
        string? name, decimal plannedQuantity, UnitOfMeasure unit, int? tubCount, string? color, string? notes)
    {
        Name = name;
        PlannedQuantity = plannedQuantity;
        Unit = unit;
        TubCount = tubCount;
        Color = color;
        Notes = notes;
    }

    internal void SetSpecification(
        decimal? widthCm, decimal? metersPerKg, decimal? gsm, string? tubFormat, string? windingTapeFormat)
    {
        WidthCm = widthCm;
        MetersPerKg = metersPerKg;
        Gsm = gsm;
        TubFormat = tubFormat;
        WindingTapeFormat = windingTapeFormat;
    }

    internal void SetInstructions(string? quality, string? lab, string? internalInstructions, string? customer)
    {
        QualityInstructions = quality;
        LabInstructions = lab;
        InternalInstructions = internalInstructions;
        CustomerInstructions = customer;
    }

    /// <summary>
    /// Starts the basin from its parent group's cell, so a group that has one specification and four basins
    /// does not force the planner to retype that specification four times.
    /// </summary>
    internal void InheritFrom(FormationGroup group)
    {
        if (Color is null) Color = group.Color;
        if (TubCount is null) TubCount = group.TubCount;

        SetSpecification(
            group.WidthCm ?? WidthCm, group.MetersPerKg ?? MetersPerKg, group.Gsm ?? Gsm,
            group.TubFormat ?? TubFormat, group.WindingTapeFormat ?? WindingTapeFormat);
        SetInstructions(
            group.QualityInstructions ?? QualityInstructions, group.LabInstructions ?? LabInstructions,
            group.InternalInstructions ?? InternalInstructions, group.CustomerInstructions ?? CustomerInstructions);

        SpecificationTemplateId ??= group.SpecificationTemplateId;
        SpecificationTemplateName ??= group.SpecificationTemplateName;
    }

    /// <summary>The basin's own copy of the specification snapshot (spec section 29).</summary>
    internal void ApplySpecificationSnapshot(FormationSpecTemplate template)
    {
        SpecificationTemplateId = template.Id;
        SpecificationTemplateName = string.IsNullOrWhiteSpace(template.NameEn) ? template.NameAr : template.NameEn;

        WidthCm = template.WidthCm ?? WidthCm;
        MetersPerKg = template.MetersPerKg ?? MetersPerKg;
        Gsm = template.Gsm ?? Gsm;
        TubFormat = template.TubFormat ?? TubFormat;
        WindingTapeFormat = template.WindingTapeFormat ?? WindingTapeFormat;

        QualityInstructions = template.QualityInstructions ?? QualityInstructions;
        LabInstructions = template.LabInstructions ?? LabInstructions;
        InternalInstructions = template.InternalInstructions ?? InternalInstructions;
        CustomerInstructions = template.CustomerInstructions ?? CustomerInstructions;
        Notes = template.Notes ?? Notes;
    }

    internal void FreezeSpecificationSnapshot(DateTime snapshotAtUtc) => SpecificationSnapshotAtUtc = snapshotAtUtc;

    internal void AddProducedQuantity(decimal quantity) => ProducedQuantity += quantity;
}
