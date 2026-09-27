using DyeHouseERP.Domain.Common;

namespace DyeHouseERP.Domain.Entities;

/// <summary>
/// Supplier / vendor master data (needed by the Checks register now - spec
/// section 37 "Outgoing Supplier Checks" - and by the Purchases module, which
/// is the next phase). Deliberately mirrors the Customer master: a manually
/// entered unique Code and a matching account number, because the supplier's
/// account is what purchases, payments and supplier checks all post against.
/// Phone/address are optional (spec section 5 rule, applied consistently).
/// </summary>
public class Supplier : AuditableEntity
{
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string NameAr { get; private set; } = string.Empty;
    public string NameEn { get; private set; } = string.Empty;
    public string AccountNumber { get; private set; } = string.Empty;
    public string? Phone { get; private set; }
    public string? Address { get; private set; }
    public string? ContactPerson { get; private set; }
    public string? TaxNumber { get; private set; }
    public bool IsActive { get; private set; } = true;

    private Supplier() { } // EF Core

    public Supplier(string code, string name, string createdBy,
        string? nameAr = null, string? nameEn = null, string? phone = null,
        string? address = null, string? contactPerson = null, string? taxNumber = null)
    {
        SetCode(code);
        SetAccountNumber(code);
        SetNames(name, nameAr, nameEn);
        SetContact(phone, address, contactPerson, taxNumber);
        CreatedBy = createdBy;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public void SetCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentException("Supplier code is required.", nameof(code));
        Code = code.Trim();
    }

    /// <summary>The supplier's account number is linked to the supplier code (same rule as customers, spec section 5).</summary>
    public void SetAccountNumber(string accountNumber)
    {
        if (string.IsNullOrWhiteSpace(accountNumber))
            throw new ArgumentException("Supplier account number is required.", nameof(accountNumber));
        AccountNumber = accountNumber.Trim();
    }

    public void SetNames(string? nameAr, string? nameEn) => SetNames(null, nameAr, nameEn);

    public void SetNames(string? name, string? nameAr, string? nameEn)
    {
        if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(nameAr) && string.IsNullOrWhiteSpace(nameEn))
            throw new ArgumentException("Supplier name is required.", nameof(nameEn));

        NameEn = (nameEn ?? name ?? nameAr)!.Trim();
        NameAr = (nameAr ?? name ?? nameEn)!.Trim();
        Name = NameEn.Length > 0 ? NameEn : NameAr;
    }

    /// <summary>Phone and address stay optional by design (spec section 5) - they are never required to save a supplier.</summary>
    public void SetContact(string? phone, string? address, string? contactPerson, string? taxNumber)
    {
        Phone = Blank(phone);
        Address = Blank(address);
        ContactPerson = Blank(contactPerson);
        TaxNumber = Blank(taxNumber);
    }

    public void Activate(string modifiedBy) { IsActive = true; Touch(modifiedBy); }
    public void Deactivate(string modifiedBy) { IsActive = false; Touch(modifiedBy); }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private void Touch(string modifiedBy)
    {
        ModifiedBy = modifiedBy;
        ModifiedAtUtc = DateTime.UtcNow;
    }
}
