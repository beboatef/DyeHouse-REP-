using DyeHouseERP.Domain.Common;
using DyeHouseERP.Domain.Enums;

namespace DyeHouseERP.Domain.Entities;

/// <summary>
/// Configuration for one document type's numbering rules (spec section 6).
/// Example: DocumentType=ProductionOrder, Prefix="PRD", Padding=6,
/// YearlyReset=true  =>  produces "PRD-2026-000217".
/// </summary>
public class DocumentSequenceDefinition : BaseEntity
{
    public DocumentType DocumentType { get; private set; }
    public string Prefix { get; private set; } = string.Empty;
    public int Padding { get; private set; } = 6;
    public bool YearlyReset { get; private set; }
    public bool WarehouseScoped { get; private set; }
    public long StartingNumber { get; private set; } = 1;

    private DocumentSequenceDefinition() { } // EF Core

    public DocumentSequenceDefinition(DocumentType documentType, string prefix, int padding,
        bool yearlyReset, bool warehouseScoped, long startingNumber = 1)
    {
        DocumentType = documentType;
        Prefix = prefix;
        Padding = padding;
        YearlyReset = yearlyReset;
        WarehouseScoped = warehouseScoped;
        StartingNumber = startingNumber;
    }

    /// <summary>Formats a raw counter value into the visible document number.</summary>
    public string Format(long value, int? year)
    {
        var number = value.ToString().PadLeft(Padding, '0');
        return YearlyReset && year.HasValue
            ? $"{Prefix}-{year}-{number}"
            : $"{Prefix}-{number}";
    }
}

/// <summary>
/// The actual counter row that gets incremented. One row per
/// (DocumentType, Year, WarehouseId) combination. Incrementing this row is
/// done under UPDLOCK/HOLDLOCK inside a transaction by
/// SqlDocumentNumberGenerator - never via application-level MAX()+1, which
/// is unsafe under concurrent writers.
/// </summary>
public class DocumentSequenceCounter : BaseEntity
{
    public DocumentType DocumentType { get; private set; }
    public int? Year { get; private set; }
    public Guid? WarehouseId { get; private set; }
    public long CurrentValue { get; private set; }

    private DocumentSequenceCounter() { } // EF Core

    public DocumentSequenceCounter(DocumentType documentType, int? year, Guid? warehouseId, long startingValue)
    {
        DocumentType = documentType;
        Year = year;
        WarehouseId = warehouseId;
        CurrentValue = startingValue - 1; // first call to Next() will bring it to startingValue
    }

    /// <summary>Only ever called from within the locked transaction in the generator.</summary>
    public long Next()
    {
        CurrentValue += 1;
        return CurrentValue;
    }
}
