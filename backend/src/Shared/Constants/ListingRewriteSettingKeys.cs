namespace Shared.Constants;

/// <summary>
/// Setting keys for listing Rewrite title handling (managed via admin Settings).
/// </summary>
public static class ListingRewriteSettingKeys
{
    /// <summary>
    /// How Rewrite treats the product title: strip supplier brands or rewrite SEO.
    /// </summary>
    public const string TitleMode = "listing.rewrite.title_mode";

    /// <summary>AI flags brand tokens; C# removes them and keeps the rest of the source title. Titles still under 40 characters take the SEO fallback (Women … + keywords synced to the description).</summary>
    public const string TitleModeStripBrands = "strip_brands";

    /// <summary>AI rewrites a machine-first SEO title (150–250 characters; never invent attributes to fill the band).</summary>
    public const string TitleModeRewrite = "rewrite";

    /// <summary>Default when the setting is missing or unrecognized.</summary>
    public const string DefaultTitleMode = TitleModeStripBrands;

    /// <summary>
    /// When true, Rewrite stays clickable after a title/description has already been rewritten.
    /// When false (default), the button locks and the operator uses Undo instead — avoids a
    /// second paid OpenAI call by accident.
    /// </summary>
    public const string AllowRepeat = "listing.rewrite.allow_repeat";

    /// <summary>Default: lock Rewrite after the first successful run.</summary>
    public const string DefaultAllowRepeat = "false";
}
