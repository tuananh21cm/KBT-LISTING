namespace Shared.Constants;

/// <summary>
/// Whitelist of shop catalog fashion-style codes — khớp facet Style Shein (attr group 101)
/// đã lấy từ URL pdsearch thật. Code không có trên Shein (<c>casual-woman</c>/<c>royal</c>) không còn trong list.
/// </summary>
public static class ShopStyleCodes
{
    /// <summary>Elegant — thanh lịch, tinh tế, trưởng thành. Shein 101_257.</summary>
    public const string Elegant = "elegant";

    /// <summary>Casual — đời thường, dễ mặc, thoải mái. Shein 101_167.</summary>
    public const string Casual = "casual";

    /// <summary>Vintage — cảm hứng thập niên trước, floral/retro. Shein 101_730.</summary>
    public const string Vintage = "vintage";

    /// <summary>Sexy — nhấn quyến rũ, ôm dáng / cut-out. Shein 101_580.</summary>
    public const string Sexy = "sexy";

    /// <summary>Boho — Bohemian, phóng khoáng, resort/festival. Shein 101_725.</summary>
    public const string Boho = "boho";

    /// <summary>Cute — trẻ trung, đáng yêu, nữ tính. Shein 101_231.</summary>
    public const string Cute = "cute";

    /// <summary>Glamorous — lộng lẫy, sequin/metallic. Shein 101_493.</summary>
    public const string Glamorous = "glamorous";

    /// <summary>Vacation — resort / biển / du lịch. Shein 101_2489.</summary>
    public const string Vacation = "vacation";

    /// <summary>Modest — kín đáo nhưng vẫn thời trang. Shein 101_1547.</summary>
    public const string Modest = "modest";

    /// <summary>Party — tiệc, club, sự kiện. Shein 101_2491.</summary>
    public const string Party = "party";

    /// <summary>All allowed codes (order = shop UI). Max selection = count.</summary>
    public static readonly IReadOnlyList<string> All =
    [
        Elegant,
        Casual,
        Vintage,
        Sexy,
        Boho,
        Cute,
        Glamorous,
        Vacation,
        Modest,
        Party
    ];

    /// <summary>Maximum styles per shop (full whitelist).</summary>
    public static int MaxPerShop => All.Count;

    /// <summary>Comma list for validation / error copy.</summary>
    public static string AllowedList => string.Join(", ", All);

    /// <summary>Returns true when <paramref name="code"/> is a known style code.</summary>
    public static bool IsValid(string? code) =>
        !string.IsNullOrWhiteSpace(code)
        && All.Contains(code.Trim(), StringComparer.OrdinalIgnoreCase);

    /// <summary>Canonical code from whitelist, or null if unknown.</summary>
    public static string? Canonical(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return null;
        }

        var trimmed = code.Trim();
        return All.FirstOrDefault(c => string.Equals(c, trimmed, StringComparison.OrdinalIgnoreCase));
    }
}
