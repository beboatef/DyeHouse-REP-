namespace DyeHouseERP.Domain.Common;

/// <summary>
/// Base class for every entity that has identity. Uses a GUID key so that
/// IDs can be generated client-side (offline-safe, mergeable) while the
/// human-facing, sequential "document numbers" are handled separately by
/// the numbering engine (see IDocumentNumberGenerator).
/// </summary>
public abstract class BaseEntity
{
    public Guid Id { get; protected set; } = Guid.NewGuid();

    private readonly List<IDomainEvent> _domainEvents = new();
    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    protected void AddDomainEvent(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);
    public void ClearDomainEvents() => _domainEvents.Clear();
}

/// <summary>
/// Adds the standard audit trail fields required by section 3 (auditability)
/// of the spec. Every transactional entity (messages, orders, adjustments,
/// invoices, ...) must inherit this - "who did what, when" must always be
/// reconstructable.
/// </summary>
public abstract class AuditableEntity : BaseEntity
{
    public DateTime CreatedAtUtc { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime? ModifiedAtUtc { get; set; }
    public string? ModifiedBy { get; set; }

    /// <summary>
    /// Soft "finalized/locked" flag. Many documents in this system
    /// (adjustments, invoices, delivered deliveries) must become
    /// immutable once finalized - never silently editable.
    /// </summary>
    public bool IsLocked { get; private set; }

    public void Lock() => IsLocked = true;
}

public interface IDomainEvent
{
    DateTime OccurredOnUtc { get; }
}
