using Domain.Common;

namespace Domain.Entities;

/// <summary>
/// SKU / variation row for a synced 4Seller <see cref="Listing"/>.
/// </summary>
public class ListingVariation : BaseEntity
{
    /// <summary>Row identifier.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Parent listing id.</summary>
    public Guid ListingId { get; set; }

    /// <summary>4Seller variation / sku id when present.</summary>
    public long? FourSellerSkuId { get; set; }

    /// <summary>Seller SKU (MSKU).</summary>
    public string? SellerSku { get; set; }

    /// <summary>Variant title (e.g. Color/Size).</summary>
    public string? Title { get; set; }

    /// <summary>Thumbnail URL.</summary>
    public string? MainImage { get; set; }

    /// <summary>Sale / original price (current; may change after seller edits).</summary>
    public decimal? SalePrice { get; set; }

    /// <summary>
    /// Cost / first-seen unit price when listing was first synced. Set once when null from 4Seller
    /// original/global price; never overwritten by later SalePrice changes. On draft sync this is
    /// the purchase cost; SalePrice starts at 0 until the seller calculates it on edit.
    /// Used to autofill <c>Order.Cost</c> via SellerSku match.
    /// </summary>
    public decimal? InitialPrice { get; set; }

    /// <summary>Global price when present.</summary>
    public decimal? GlobalPrice { get; set; }

    /// <summary>Available stock.</summary>
    public int? Quantity { get; set; }

    /// <summary>Variant attributes JSON.</summary>
    public string? VariantAttributes { get; set; }

    /// <summary>Parent listing.</summary>
    public Listing Listing { get; set; } = null!;
}
