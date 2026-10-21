namespace Application.DTOs.FourSeller;

/// <summary>
/// TikTok warehouse option from 4Seller get-warehouse-list-by-shop-id.
/// </summary>
public class FourSellerWarehouseDto
{
    /// <summary>TikTok warehouse ID.</summary>
    public string WarehouseId { get; set; } = string.Empty;

    /// <summary>Display name.</summary>
    public string WarehouseName { get; set; } = string.Empty;

    /// <summary>Warehouse type code.</summary>
    public int? WarehouseType { get; set; }

    /// <summary>Warehouse sub-type code.</summary>
    public int? WarehouseSubType { get; set; }

    /// <summary>Effect status (1 = active).</summary>
    public int? WarehouseEffectStatus { get; set; }

    /// <summary>Whether this is the shop default warehouse.</summary>
    public bool IsDefault { get; set; }

    /// <summary>Fulfillment-by-TikTok flag.</summary>
    public bool Fbt { get; set; }

    /// <summary>Allocation mode string (often empty).</summary>
    public string? AllocationMode { get; set; }
}
