namespace Application.DTOs.Shop;

/// <summary>
/// AI recommendation: shop analysis + Shein search links for organic sourcing.
/// </summary>
public class WhatToSellTodayDto
{
    /// <summary>Joined analysis text (points separated by newlines).</summary>
    public string Analysis { get; set; } = string.Empty;

    /// <summary>Short Vietnamese bullet points (niche / winners / price / next).</summary>
    public List<string> AnalysisPoints { get; set; } = [];

    /// <summary>TTS product-category labels (may be empty if profile not synced).</summary>
    public List<string> ProductCategories { get; set; } = [];

    /// <summary>Avg Giá nhập from top bestsellers; null if unknown.</summary>
    public decimal? AvgCost { get; set; }

    /// <summary>Avg TikTok sale price customers actually paid for top sellers.</summary>
    public decimal? AvgSalePrice { get; set; }

    /// <summary>Suggested Shein cost band low (UI/context only; not in search URL).</summary>
    public int MinPrice { get; set; }

    /// <summary>Suggested Shein cost band high (UI/context only; not in search URL).</summary>
    public int MaxPrice { get; set; }

    /// <summary>Order lookback window actually used (7 or 30).</summary>
    public int PeriodDays { get; set; }

    /// <summary>Number of distinct bestsellers / listing rows fed to the model.</summary>
    public int ProductCountUsed { get; set; }

    /// <summary>UTC date string passed to the model (yyyy-MM-dd).</summary>
    public string AsOfDate { get; set; } = string.Empty;

    /// <summary>US shopping season label (e.g. back-to-school).</summary>
    public string Season { get; set; } = string.Empty;

    /// <summary>US retail events inside the sourcing window.</summary>
    public List<string> UpcomingEvents { get; set; } = [];

    /// <summary>2 Shein searches following what is currently winning.</summary>
    public List<WhatToSellTodaySearchDto> SearchesTrend { get; set; } = [];

    /// <summary>3 Shein searches for upcoming events / season shift.</summary>
    public List<WhatToSellTodaySearchDto> SearchesUpcoming { get; set; } = [];
}

/// <summary>One Shein pdsearch suggestion.</summary>
public class WhatToSellTodaySearchDto
{
    /// <summary>English keyword query (e.g. tube tops women).</summary>
    public string Keywords { get; set; } = string.Empty;

    /// <summary>Fully built us.shein.com pdsearch URL.</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>Short rationale from the model (optional).</summary>
    public string? Rationale { get; set; }
}
