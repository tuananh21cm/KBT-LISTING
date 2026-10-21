using Domain.Common;

namespace Domain.Entities;

/// <summary>
/// Shop NCC trên một nền tảng nguồn (Shein / Temu / …). Một shop có nhiều sản phẩm.
/// </summary>
public class SupplierSellerInfo : BaseEntity
{
    /// <summary>Row identifier.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Mã nguồn hàng (<see cref="ProductSource.Code"/>), vd. SHEIN.</summary>
    public string Platform { get; set; } = string.Empty;

    /// <summary>Mã shop trên nền tảng nguồn (Shein <c>storeCode</c>).</summary>
    public string StoreCode { get; set; } = string.Empty;

    /// <summary>URL mở shop nhanh (canonical, không tracking).</summary>
    public string? StoreUrl { get; set; }

    /// <summary>Logo shop.</summary>
    public string? StoreLogo { get; set; }

    /// <summary>Tên shop.</summary>
    public string? StoreTitle { get; set; }

    /// <summary>Shein <c>soldByTips</c>.</summary>
    public string? SoldByTips { get; set; }

    /// <summary>Shein <c>shipsFromTips</c>.</summary>
    public string? ShipsFromTips { get; set; }

    /// <summary>Shein <c>shopRank</c>.</summary>
    public decimal? ShopRank { get; set; }

    /// <summary>Shein <c>storeAttention</c> (số follow).</summary>
    public int? StoreAttention { get; set; }

    /// <summary>SLA ngày giao (từ <c>dayPercentDesc</c>, vd. ≤ 7).</summary>
    public int? ShippingDays { get; set; }

    /// <summary>% đơn giao trong SLA (parse <c>dayPercentDesc</c> / cộng <c>dayPercents</c>).</summary>
    public decimal? ShippingDayPercent { get; set; }

    /// <summary>Raw <c>dayPercentDesc</c> HTML (debug).</summary>
    public string? ShippingDayPercentDesc { get; set; }

    /// <summary>Sản phẩm nguồn thuộc shop này.</summary>
    public ICollection<SupplierProductInfo> Products { get; set; } = new List<SupplierProductInfo>();
}
