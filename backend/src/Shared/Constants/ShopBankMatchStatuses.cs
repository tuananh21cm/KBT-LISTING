namespace Shared.Constants;

/// <summary>
/// Last-4 digit match between TikTok settlement bank and the shop's receiving-bank card.
/// </summary>
public static class ShopBankMatchStatuses
{
    /// <summary>Both sides have ≥4 digits and tails match.</summary>
    public const string Matched = "Matched";

    /// <summary>Both sides have ≥4 digits but tails differ.</summary>
    public const string Mismatched = "Mismatched";

    /// <summary>Shop has a receiving-bank card but TikTok settlement bank is missing/short.</summary>
    public const string MissingSettlement = "MissingSettlement";

    /// <summary>Shop has no receiving-bank card (or account number has fewer than 4 digits).</summary>
    public const string MissingReceivingBank = "MissingReceivingBank";

    /// <summary>All accepted bank-match status strings.</summary>
    public static readonly HashSet<string> All = new(StringComparer.OrdinalIgnoreCase)
    {
        Matched,
        Mismatched,
        MissingSettlement,
        MissingReceivingBank,
    };
}
