namespace Application.DTOs.Shop;

/// <summary>
/// One operational TikTok category assigned to a shop (parent or leaf).
/// </summary>
public class ShopCatalogCategoryDto
{
    /// <summary>TikTok category external ID.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Display path (e.g. Automotive &amp; Motorcycle or Women / Dresses / Mini).</summary>
    public string Name { get; set; } = string.Empty;
}
