namespace Application.DTOs.Shop;

/// <summary>
/// Slim auto-publish payload returned by PUT …/auto-publish.
/// </summary>
public class ShopAutoPublishDto
{
    /// <summary>Shop id.</summary>
    public Guid Id { get; set; }

    /// <summary>Per-shop auto publish enabled.</summary>
    public bool AutoPublishEnabled { get; set; }

    /// <summary>Seconds between auto publishes (null = system default).</summary>
    public int? AutoPublishIntervalSeconds { get; set; }

    /// <summary>Minimum draft age in minutes before pick (null = system default).</summary>
    public int? AutoPublishMinDraftAgeMinutes { get; set; }
}
