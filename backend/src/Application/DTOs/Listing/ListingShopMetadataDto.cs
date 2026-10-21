using Application.DTOs.FourSeller;

namespace Application.DTOs.Listing;

/// <summary>
/// Shop-scoped TikTok metadata (brands + warehouses) for listing edit / publish.
/// </summary>
public class ListingShopMetadataDto
{
    /// <summary>Local shop Guid.</summary>
    public Guid ShopId { get; set; }

    /// <summary>4Seller numeric shop id (string).</summary>
    public string FourSellerShopId { get; set; } = string.Empty;

    /// <summary>Authorized brands for the shop (may be empty).</summary>
    public List<FourSellerBrandDto> Brands { get; set; } = [];

    /// <summary>Warehouses for the shop.</summary>
    public List<FourSellerWarehouseDto> Warehouses { get; set; } = [];

    /// <summary>Default warehouse id when present.</summary>
    public string? DefaultWarehouseId { get; set; }
}
