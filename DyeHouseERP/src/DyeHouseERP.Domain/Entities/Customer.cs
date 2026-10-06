using DyeHouseERP.Domain.Common;

namespace DyeHouseERP.Domain.Entities;

/// <summary>
/// Customer master data - deliberately minimal per spec section 7.
/// The Code is manually entered by the user (not system-generated) and is
/// the value referenced across every downstream module.
/// </summary>
public class Customer : AuditableEntity
{
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string AccountNumber { get; private set; } = string.Empty;
    public bool IsActive { get; private set; } = true;

    // Contact and tax details. A dye house quotes, invoices and delivers to its
    // customers, and a printed invoice / statement / delivery note has to carry a
    // tax number and a contact - so these belong on the customer master rather than
    // being retyped on every document. All optional: a customer is not refused for
    // not having them, which keeps existing rows valid.
    public string? Phone { get; private set; }
    public string? Address { get; private set; }
    public string? ContactPerson { get; private set; }
    public string? TaxNumber { get; private set; }

    private Customer() { } // EF Core

    public Customer(string code, string name, string createdBy)
    {
        SetCode(code);
        SetAccountNumber(code);
        SetName(name);
        CreatedBy = createdBy;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public void SetCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentException("Customer code is required.", nameof(code));
        Code = code.Trim();
    }

    public void SetAccountNumber(string accountNumber)
    {
        if (string.IsNullOrWhiteSpace(accountNumber)) throw new ArgumentException("Account number is required.");
        AccountNumber = accountNumber.Trim();
    }

    public void SetName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Customer name is required.", nameof(name));
        Name = name.Trim();
    }

    /// <summary>
    /// Sets the descriptive/contact fields in one go. Code and Account Number are NOT here:
    /// Code is the join key to every downstream document, so it is identity, not editable data.
    /// </summary>
    public void SetContact(
        string? phone, string? address, string? contactPerson, string? taxNumber, string modifiedBy)
    {
        Phone = Blank(phone);
        Address = Blank(address);
        ContactPerson = Blank(contactPerson);
        TaxNumber = Blank(taxNumber);
        Touch(modifiedBy);
    }

    /// <summary>Renames a customer. The Code stays exactly as it is.</summary>
    public void SetName(string name, string modifiedBy)
    {
        SetName(name);
        Touch(modifiedBy);
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public void Activate(string modifiedBy) { IsActive = true; Touch(modifiedBy); }
    public void Deactivate(string modifiedBy) { IsActive = false; Touch(modifiedBy); }

    private void Touch(string modifiedBy)
    {
        ModifiedBy = modifiedBy;
        ModifiedAtUtc = DateTime.UtcNow;
    }
}
