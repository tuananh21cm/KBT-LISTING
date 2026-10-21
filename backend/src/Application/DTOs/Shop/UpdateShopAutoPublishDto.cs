namespace Application.DTOs.Shop;

/// <summary>
/// Per-shop auto publish settings only — lightweight PUT …/auto-publish body.
/// </summary>
public class UpdateShopAutoPublishDto
{
    /// <summary>Enable auto publish for this shop (requires system setting).</summary>
    public bool AutoPublishEnabled { get; set; }

    /// <summary>Seconds between auto publishes (null = system default).</summary>
    public int? AutoPublishIntervalSeconds { get; set; }

    /// <summary>Minimum draft age in minutes before pick (null = system default).</summary>
    public int? AutoPublishMinDraftAgeMinutes { get; set; }
}
