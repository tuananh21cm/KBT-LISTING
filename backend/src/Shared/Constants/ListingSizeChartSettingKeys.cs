namespace Shared.Constants;

/// <summary>
/// Setting keys for automatic Size Chart image generation (admin Settings).
/// </summary>
public static class ListingSizeChartSettingKeys
{
    /// <summary>
    /// When true, ListingEditPage shows the Grok auto-generate Size Chart button.
    /// </summary>
    public const string AutoGenerateEnabled = "listing.size_chart.auto_generate_enabled";

    /// <summary>Default: feature off until admin enables.</summary>
    public const string DefaultAutoGenerateEnabled = "false";
}
