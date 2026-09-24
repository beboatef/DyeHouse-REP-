using DyeHouseERP.Domain.Common;

namespace DyeHouseERP.Domain.Entities;

/// <summary>
/// A saved custom report definition (spec section 36 "Custom Report
/// Builder"). Deliberately NOT arbitrary SQL - EntityKey must match one of
/// the whitelisted entries in ReportableEntities, and Columns must be a
/// subset of that entity's allowed column list. See RunReportCommand for
/// where that whitelist is enforced.
/// </summary>
public class SavedReportTemplate : AuditableEntity
{
    public string NameAr { get; private set; } = string.Empty;
    public string NameEn { get; private set; } = string.Empty;
    public string EntityKey { get; private set; } = string.Empty;
    public string ColumnsCsv { get; private set; } = string.Empty;
    public string? FiltersJson { get; private set; }

    private SavedReportTemplate() { } // EF Core

    public SavedReportTemplate(string nameAr, string nameEn, string entityKey, string columnsCsv, string createdBy, string? filtersJson = null)
    {
        NameAr = nameAr;
        NameEn = nameEn;
        EntityKey = entityKey;
        ColumnsCsv = columnsCsv;
        FiltersJson = filtersJson;
        CreatedBy = createdBy;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public IEnumerable<string> Columns => ColumnsCsv.Split(',', StringSplitOptions.RemoveEmptyEntries);
}
