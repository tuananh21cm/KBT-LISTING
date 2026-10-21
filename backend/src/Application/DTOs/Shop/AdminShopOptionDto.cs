namespace Application.DTOs.Shop;

/// <summary>Minimal shop row for admin filter dropdowns.</summary>
public class AdminShopOptionDto
{
    /// <summary>Shop identifier.</summary>
    public Guid Id { get; set; }

    /// <summary>Display name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Browser profile name (optional) — shown in filter dropdowns.</summary>
    public string? ProfileName { get; set; }

    /// <summary>Owner user id (seller filter).</summary>
    public Guid OwnerId { get; set; }

    /// <summary>Catalog body/size code (plus / petite / …) — used to build sourcing queries.</summary>
    public string? BodySizeCode { get; set; }

    /// <summary>Platform code (e.g. TIKTOK).</summary>
    public string PlatformCode { get; set; } = string.Empty;

    /// <summary>Shop status (Active / Inactive / …).</summary>
    public string Status { get; set; } = string.Empty;
}
