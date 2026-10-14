namespace DyeHouseERP.Application.Customers.DTOs;

public class CustomerDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; }

    public string? Phone { get; set; }
    public string? Address { get; set; }
    public string? ContactPerson { get; set; }
    public string? TaxNumber { get; set; }

    /// <summary>The customer this one is a sub-customer of, or null for a top-level customer (spec section 7).</summary>
    public Guid? ParentCustomerId { get; set; }

    /// <summary>Convenience display fields for the parent, resolved at query time.</summary>
    public string? ParentCustomerCode { get; set; }
    public string? ParentCustomerName { get; set; }

    /// <summary>True when this customer has a parent - a sub-customer.</summary>
    public bool IsSubCustomer => ParentCustomerId.HasValue;
}
