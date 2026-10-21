namespace Application.Interfaces;

using Application.DTOs.Listing;

/// <summary>Prep steps ported from listing edit UI for auto publish.</summary>
public interface IListingAutoPublishPrepService
{
    /// <summary>Apply default pricing template sale prices to variations.</summary>
    Task ApplyDefaultPricingAsync(Guid ownerId, ListingDetailDto detail, CancellationToken ct = default);

    /// <summary>Set quantity on all variations.</summary>
    void ApplyDefaultQuantity(ListingDetailDto detail, int quantity = 45);

    /// <summary>Generate unique seller SKUs when missing (alias for dedupe + fill).</summary>
    void GenerateBatchSkus(ListingDetailDto detail);

    /// <summary>Dedupe sale-prop combos and ensure every variant has a unique sellerSku.</summary>
    void EnsureUniqueSellerSkus(ListingDetailDto detail);

    /// <summary>True when every variant has a non-empty, case-insensitive unique sellerSku.</summary>
    bool HasUniqueSellerSkus(ListingDetailDto detail);

    /// <summary>Upload size chart from capture URL to 4Seller COS and add to gallery.</summary>
    Task UploadSizeChartFromCaptureAsync(
        Guid listingId,
        ListingDetailDto detail,
        string captureUrl,
        CancellationToken ct = default);

    /// <summary>https upgrade, dedupe variant colors, strip .gif, remove Product video, pad gallery.</summary>
    void PrepareImages(ListingDetailDto detail, string? sizeChartUrl);

    /// <summary>Whether gallery has size chart media type with URL.</summary>
    bool HasSizeChartInGallery(ListingDetailDto detail);

    /// <summary>True when any package weight/dimension field is missing or zero.</summary>
    bool NeedsShippingAutofill(ListingDetailDto detail);

    /// <summary>All package weight and dimensions are present and &gt; 0.</summary>
    bool IsShippingComplete(ListingDetailDto detail);

    /// <summary>Apply owner default shipping template when package fields are incomplete.</summary>
    Task<bool> ApplyDefaultShippingAsync(Guid ownerId, ListingDetailDto detail, CancellationToken ct = default);

    /// <summary>Apply shop default warehouse from 4Seller metadata when warehouse is empty.</summary>
    Task<bool> ApplyDefaultWarehouseAsync(Guid listingId, ListingDetailDto detail, CancellationToken ct = default);
}
