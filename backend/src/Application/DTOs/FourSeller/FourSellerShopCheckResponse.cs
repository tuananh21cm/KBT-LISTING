namespace Application.DTOs.FourSeller;

/// <summary>
/// Result of checking a shop's 4Seller connection status.
/// </summary>
public class FourSellerShopCheckResponse
{
    /// <summary>Whether a matching connected shop was found on 4Seller.</summary>
    public bool IsConnected { get; set; }

    /// <summary>How the shop was matched: platformShopName, sellingPartnerId, fourSellerId.</summary>
    public string? MatchedBy { get; set; }

    /// <summary>Matched 4Seller shop record, if any.</summary>
    public FourSellerShopRecordDto? MatchedShop { get; set; }

    /// <summary>Whether local shop record was updated from the check result.</summary>
    public bool ShopSynced { get; set; }

    /// <summary>Synced connection timestamp on local shop (UTC).</summary>
    public DateTime? FourSellerConnectedAt { get; set; }
}
