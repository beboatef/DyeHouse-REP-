using DyeHouseERP.Domain.Common;

namespace DyeHouseERP.Domain.Entities;

/// <summary>
/// Reusable specification CELL / master template for formation requests
/// (spec section 29). The user must never have to retype the same
/// specifications for every request: width, meters-per-kg, weight per square
/// meter, tub/tube format, winding tape format and the various instruction
/// blocks are saved once here and selected by name later, which populates the
/// group's fields.
///
/// IMPORTANT (spec section 29): when a template is selected on a Formation
/// Request, its values are COPIED onto the request's group rows. That copy is
/// the snapshot. Editing a template afterwards never rewrites a request that
/// was already created/approved - see FormationGroup.ApplySpecificationSnapshot.
/// </summary>
public class FormationSpecTemplate : AuditableEntity
{
    public string Code { get; private set; } = string.Empty;
    public string NameAr { get; private set; } = string.Empty;
    public string NameEn { get; private set; } = string.Empty;

    /// <summary>Fabric width in centimetres, e.g. 160 / 180 / 200.</summary>
    public decimal? WidthCm { get; private set; }

    /// <summary>Length produced per KG of input, e.g. 5 m/kg or 8 m/kg.</summary>
    public decimal? MetersPerKg { get; private set; }

    /// <summary>Weight per square meter in g/m², e.g. 120 / 150. Alternative descriptor to MetersPerKg - never auto-converted between the two.</summary>
    public decimal? Gsm { get; private set; }

    /// <summary>Tub / tube format: per KG, 50 meter, 60 meter, 100 meter, ... (configurable free text, not a closed list).</summary>
    public string? TubFormat { get; private set; }

    /// <summary>Winding tape format - factory-configurable description.</summary>
    public string? WindingTapeFormat { get; private set; }

    public string? Notes { get; private set; }
    public string? QualityInstructions { get; private set; }
    public string? LabInstructions { get; private set; }
    public string? InternalInstructions { get; private set; }
    public string? CustomerInstructions { get; private set; }

    public bool IsActive { get; private set; } = true;

    private FormationSpecTemplate() { } // EF Core

    public FormationSpecTemplate(
        string code, string nameAr, string nameEn, string createdBy,
        decimal? widthCm = null, decimal? metersPerKg = null, decimal? gsm = null,
        string? tubFormat = null, string? windingTapeFormat = null, string? notes = null,
        string? qualityInstructions = null, string? labInstructions = null,
        string? internalInstructions = null, string? customerInstructions = null)
    {
        SetCode(code);
        SetNames(nameAr, nameEn);
        SetSpecification(widthCm, metersPerKg, gsm, tubFormat, windingTapeFormat);
        SetInstructions(qualityInstructions, labInstructions, internalInstructions, customerInstructions);
        Notes = notes;
        CreatedBy = createdBy;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public void SetCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentException("Specification code is required.", nameof(code));
        Code = code.Trim();
    }

    public void SetNames(string? nameAr, string? nameEn)
    {
        if (string.IsNullOrWhiteSpace(nameAr) && string.IsNullOrWhiteSpace(nameEn))
            throw new ArgumentException("Specification name is required (Arabic or English).", nameof(nameEn));
        NameAr = (nameAr ?? nameEn)!.Trim();
        NameEn = (nameEn ?? nameAr)!.Trim();
    }

    public void SetSpecification(
        decimal? widthCm, decimal? metersPerKg, decimal? gsm,
        string? tubFormat, string? windingTapeFormat)
    {
        // No KG<->Meter conversion and no derived values - every figure is
        // entered as-is (spec sections 6 and 29). Only sanity is negative.
        if (widthCm is < 0) throw new ArgumentException("Width cannot be negative.", nameof(widthCm));
        if (metersPerKg is < 0) throw new ArgumentException("Meter per KG cannot be negative.", nameof(metersPerKg));
        if (gsm is < 0) throw new ArgumentException("Weight per square meter cannot be negative.", nameof(gsm));

        WidthCm = widthCm;
        MetersPerKg = metersPerKg;
        Gsm = gsm;
        TubFormat = Blank(tubFormat);
        WindingTapeFormat = Blank(windingTapeFormat);
    }

    public void SetInstructions(string? quality, string? lab, string? internalInstructions, string? customer)
    {
        QualityInstructions = Blank(quality);
        LabInstructions = Blank(lab);
        InternalInstructions = Blank(internalInstructions);
        CustomerInstructions = Blank(customer);
    }

    public void SetNotes(string? notes) => Notes = Blank(notes);

    public void Activate(string modifiedBy) { IsActive = true; Touch(modifiedBy); }
    public void Deactivate(string modifiedBy) { IsActive = false; Touch(modifiedBy); }

    /// <summary>Human-readable one-line summary used in pickers and in the snapshot label stored on a group.</summary>
    public string Summary()
    {
        var parts = new List<string>();
        if (WidthCm.HasValue) parts.Add($"{WidthCm.Value:0.##} cm");
        if (MetersPerKg.HasValue) parts.Add($"{MetersPerKg.Value:0.##} m/kg");
        if (Gsm.HasValue) parts.Add($"{Gsm.Value:0.##} g/m²");
        if (!string.IsNullOrWhiteSpace(TubFormat)) parts.Add(TubFormat!);
        return parts.Count == 0 ? Code : $"{Code} · {string.Join(" · ", parts)}";
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private void Touch(string modifiedBy)
    {
        ModifiedBy = modifiedBy;
        ModifiedAtUtc = DateTime.UtcNow;
    }
}
