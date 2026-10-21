using Shared.Constants;

namespace Application.DTOs.Listing;

/// <summary>
/// System listing-edit AI settings (Rewrite title mode + repeat locks).
/// One small GET so admin/seller do not download the full Settings list.
/// </summary>
public class ListingRewriteConfigDto
{
    /// <summary><c>strip_brands</c> or <c>rewrite</c>.</summary>
    public string TitleMode { get; set; } = ListingRewriteSettingKeys.DefaultTitleMode;

    /// <summary>
    /// When true, the Rewrite button stays enabled after a successful rewrite.
    /// </summary>
    public bool AllowRepeat { get; set; }

    /// <summary>
    /// When true, Auto Specifics stays enabled after Specifics are already filled.
    /// </summary>
    public bool AllowSpecificsRepeat { get; set; }

    /// <summary>
    /// When true, ListingEditPage shows the Grok auto-generate Size Chart button.
    /// </summary>
    public bool AutoGenerateSizeChart { get; set; }
}
