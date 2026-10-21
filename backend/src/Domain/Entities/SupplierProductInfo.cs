using Domain.Common;

namespace Domain.Entities;

/// <summary>
/// Snapshot thông tin SP nguồn (rating, stock, shop NCC) theo <see cref="SourceUrlKey"/>.
/// Gắn vào <see cref="Listing"/> sau khi draft sync về.
/// </summary>
public class SupplierProductInfo : BaseEntity
{
    /// <summary>Row identifier.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Stable source key (Shein <c>p:{goodsId}</c>).</summary>
    public string SourceUrlKey { get; set; } = string.Empty;

    /// <summary>Original source URL when captured.</summary>
    public string? SourceUrl { get; set; }

    /// <summary>Shop NCC trên nền tảng nguồn.</summary>
    public Guid? SupplierSellerInfoId { get; set; }

    /// <summary>Tồn kho nguồn (Shein <c>productInfo.stock</c>).</summary>
    public int? Stock { get; set; }

    /// <summary>Review / comment count (Shein <c>comment_num</c>).</summary>
    public int? ReviewCount { get; set; }

    /// <summary>Average star rating (Shein <c>comment_rank_average</c>).</summary>
    public decimal? AverageRating { get; set; }

    /// <summary>Local US stock — Shein <c>shipInfo.localMadeTips == "Local"</c> (fallback <c>productInfo.isLocalProduct</c>).</summary>
    public bool? IsLocalProduct { get; set; }

    /// <summary>Any SKU <c>mall_stock.isSupportQuickShip</c> (not SKU-level <c>isSupportQuickShip</c>).</summary>
    public bool? IsSupportQuickShip { get; set; }

    /// <summary>
    /// Normalized Shein size guide (rows cm + US size, product/body measure hints) from extension.
    /// </summary>
    public string? SizeGuideJson { get; set; }

    /// <summary>Public Bunny CDN URL of user-captured size chart image (extension upload).</summary>
    public string? SizeChartImageUrl { get; set; }

    /// <summary>Seller navigation.</summary>
    public SupplierSellerInfo? Seller { get; set; }

    /// <summary>Listings linked after sync.</summary>
    public ICollection<Listing> Listings { get; set; } = new List<Listing>();
}
