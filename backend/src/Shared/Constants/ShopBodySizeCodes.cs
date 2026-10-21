namespace Shared.Constants;

/// <summary>
/// Whitelist of shop catalog body/size group codes (Select presets).
/// </summary>
public static class ShopBodySizeCodes
{
    /// <summary>Regular / Straight Size.</summary>
    public const string Regular = "regular";

    /// <summary>Plus Size.</summary>
    public const string Plus = "plus";

    /// <summary>Midsize (between straight and plus).</summary>
    public const string Midsize = "midsize";

    /// <summary>Curvy.</summary>
    public const string Curvy = "curvy";

    /// <summary>Petite.</summary>
    public const string Petite = "petite";

    /// <summary>Tall.</summary>
    public const string Tall = "tall";

    /// <summary>All allowed codes (order = display order).</summary>
    public static readonly IReadOnlyList<string> All =
    [
        Regular,
        Plus,
        Midsize,
        Curvy,
        Petite,
        Tall
    ];

    /// <summary>Returns true when <paramref name="code"/> is a known body-size code.</summary>
    public static bool IsValid(string? code) =>
        !string.IsNullOrWhiteSpace(code)
        && All.Contains(code.Trim(), StringComparer.OrdinalIgnoreCase);
}
