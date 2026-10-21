namespace Application.DTOs.Shop;

/// <summary>
/// Which Sync All / Sync Fast scrape steps are allowed (from Settings; missing key → on).
/// </summary>
public class TikTokUnifiedSyncConfigDto
{
    public bool Info { get; set; } = true;
    public bool Finance { get; set; } = true;
    public bool Keywords { get; set; } = true;
    public bool Analytics { get; set; } = true;
    public bool HealthCenter { get; set; } = true;
    public bool ShopScore { get; set; } = true;
}
