namespace DyeHouseERP.Application.Suppliers.DTOs;

/// <summary>
/// Supplier master data (spec section 35): manually entered unique code with a
/// linked account number, bilingual names, and optional phone/address/contact -
/// never mandatory (spec section 5 rule applied consistently).
/// </summary>
public class SupplierDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public string AccountNumber { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public string? ContactPerson { get; set; }
    public string? TaxNumber { get; set; }
    public bool IsActive { get; set; }
}
