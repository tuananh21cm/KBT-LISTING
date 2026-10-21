namespace Application.DTOs.Shop;

/// <summary>System-level auto publish config for shop UI gating.</summary>
public class AutoPublishSystemConfigDto
{
    public bool SystemAutoPublishEnabled { get; set; }

    /// <summary>Default seconds between auto publishes when shop has no override.</summary>
    public int DefaultIntervalSeconds { get; set; }

    /// <summary>Default minimum draft age (minutes) when shop has no override.</summary>
    public int DefaultMinDraftAgeMinutes { get; set; }
}
