namespace Application.DTOs.FourSeller;

/// <summary>
/// Result of bulk syncing granted local shops with 4Seller tidy-list (update-only).
/// </summary>
public class FourSellerSyncResponse
{
    /// <summary>Total local shops in scope before sync.</summary>
    public int TotalLocalShops { get; set; }

    /// <summary>Total shops returned from 4Seller (sum across cookie accounts).</summary>
    public int FourSellerShopCount { get; set; }

    /// <summary>Updated count (compat for older clients; creates are never performed).</summary>
    public int SyncedCount { get; set; }

    /// <summary>Skipped / unmatched 4Seller records (alias of SkippedCount).</summary>
    public int UnmatchedCount { get; set; }

    /// <summary>Always 0 — sync never creates shops; grant shops via admin create.</summary>
    public int CreatedCount { get; set; }

    /// <summary>Existing local shops updated from 4Seller (same-owner full field apply).</summary>
    public int UpdatedCount { get; set; }

    /// <summary>
    /// Local shops whose <c>FourSellerShopId</c> was refreshed via TikTok <c>ExternalShopId</c>
    /// across owners (owner / name / region left untouched).
    /// </summary>
    public int RelinkedCount { get; set; }

    /// <summary>4Seller records skipped (not Connected / no matching granted shop).</summary>
    public int SkippedCount { get; set; }

    /// <summary>
    /// Local orders whose <c>ShopId</c> was (re)linked after shop sync stamped <c>FourSellerShopId</c>.
    /// </summary>
    public int OrdersRelinkedCount { get; set; }

    /// <summary>Accounts (cookies) synced successfully — admin multi-cookie sync.</summary>
    public int SyncedAccounts { get; set; }

    /// <summary>Accounts that had cookie but failed login or API — admin multi-cookie sync.</summary>
    public List<FourSellerSyncAccountFailureDto> FailedAccounts { get; set; } = [];

    /// <summary>
    /// 4Seller Connected shops that could not be matched to a unique local granted shop
    /// (or were skipped as not Connected when useful for diagnostics).
    /// </summary>
    public List<FourSellerUnmatchedShopDto> UnmatchedShops { get; set; } = [];
}

/// <summary>One cookie-account failure during admin shop sync.</summary>
public class FourSellerSyncAccountFailureDto
{
    /// <summary>User id whose cookie was used.</summary>
    public Guid OwnerUserId { get; set; }

    /// <summary>Display name / username.</summary>
    public string? OwnerName { get; set; }

    /// <summary>Error message.</summary>
    public string Message { get; set; } = string.Empty;
}

/// <summary>
/// A 4Seller shop record that sync could not apply to a local granted shop.
/// Reasons: <c>not_connected</c>, <c>no_local_shop</c>, <c>ambiguous_external_id</c>,
/// <c>fourseller_id_taken</c>.
/// </summary>
public class FourSellerUnmatchedShopDto
{
    /// <summary>4Seller internal shop id.</summary>
    public long FourSellerShopId { get; set; }

    /// <summary>4Seller shop name.</summary>
    public string? ShopName { get; set; }

    /// <summary>Platform-facing shop name.</summary>
    public string? PlatformShopName { get; set; }

    /// <summary>TikTok selling partner / seller id.</summary>
    public string? SellingPartnerId { get; set; }

    /// <summary>Marketplace (e.g. US).</summary>
    public string? MarketplaceId { get; set; }

    /// <summary>Display name of the cookie account that saw this shop.</summary>
    public string? CookieOwnerName { get; set; }

    /// <summary>Why sync skipped this record.</summary>
    public string Reason { get; set; } = string.Empty;
}

/// <summary>Values of <see cref="FourSellerUnmatchedShopDto.Reason"/>.</summary>
public static class FourSellerUnmatchedReasons
{
    /// <summary>4Seller itself reports the shop as not Connected — nothing to sync.</summary>
    public const string NotConnected = "not_connected";

    /// <summary>No granted local shop carries this TikTok seller id — admin must create/grant it.</summary>
    public const string NoLocalShop = "no_local_shop";

    /// <summary>Several local shops share the same TikTok seller id — dedupe before relinking.</summary>
    public const string AmbiguousExternalId = "ambiguous_external_id";

    /// <summary>Another local shop already holds this 4Seller id under a different TikTok seller id.</summary>
    public const string FourSellerIdTaken = "fourseller_id_taken";

    /// <summary>Reasons an admin has to act on (unlike <see cref="NotConnected"/>).</summary>
    public static bool IsActionable(string? reason) =>
        reason is NoLocalShop or AmbiguousExternalId or FourSellerIdTaken;
}
