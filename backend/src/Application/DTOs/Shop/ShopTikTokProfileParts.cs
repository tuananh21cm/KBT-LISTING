namespace Application.DTOs.Shop;

/// <summary>
/// Address nested in TikTok shop profile (camelCase JSON).
/// </summary>
public class ShopAddressDto
{
    /// <summary>Street line 1.</summary>
    public string? LineOne { get; set; }

    /// <summary>Street line 2.</summary>
    public string? LineTwo { get; set; }

    /// <summary>City.</summary>
    public string? City { get; set; }

    /// <summary>State / province.</summary>
    public string? State { get; set; }

    /// <summary>Country name.</summary>
    public string? Country { get; set; }

    /// <summary>ISO country code.</summary>
    public string? CountryCode { get; set; }

    /// <summary>Postal code.</summary>
    public string? PostCode { get; set; }

    /// <summary>TikTok address_type.</summary>
    public int? AddressType { get; set; }
}

/// <summary>
/// Owner / relate person from TikTok pay onboard.
/// </summary>
public class ShopOwnerPersonDto
{
    /// <summary>Full name.</summary>
    public string? FullName { get; set; }

    /// <summary>First name.</summary>
    public string? FirstName { get; set; }

    /// <summary>Last name.</summary>
    public string? LastName { get; set; }

    /// <summary>Birthday (YYYY-MM-DD).</summary>
    public string? Birthday { get; set; }

    /// <summary>Nationality code.</summary>
    public string? Nationality { get; set; }

    /// <summary>Owner address.</summary>
    public ShopAddressDto? Address { get; set; }

    /// <summary>Owner certificate number.</summary>
    public string? CertNum { get; set; }

    /// <summary>Relate person type.</summary>
    public int? RelatePersonType { get; set; }
}

/// <summary>
/// TikTok product category chip.
/// </summary>
public class ShopProductCategoryDto
{
    /// <summary>Display label.</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>External category id / value.</summary>
    public string Value { get; set; } = string.Empty;
}

/// <summary>
/// TikTok logistics / shipping service.
/// </summary>
public class ShopLogisticsServiceDto
{
    /// <summary>Service name.</summary>
    public string? ServiceName { get; set; }

    /// <summary>Delivery option name.</summary>
    public string? DeliveryOptionName { get; set; }

    /// <summary>Whether this is the default shipping method.</summary>
    public bool? IsDefault { get; set; }

    /// <summary>Warehouse id if present.</summary>
    public string? WarehouseId { get; set; }
}
