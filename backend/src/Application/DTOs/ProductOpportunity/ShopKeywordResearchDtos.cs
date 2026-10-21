namespace Application.DTOs.ProductOpportunity;

/// <summary>Request body for shop keyword research.</summary>
public class ShopKeywordResearchRequest
{
    /// <summary>Local shop id.</summary>
    public Guid ShopId { get; set; }
}

/// <summary>
/// AI research: audience-fit TikTok keywords picked from the shop snapshot,
/// or invented from catalog audience when the snapshot is empty or unusable.
/// </summary>
public class ShopKeywordResearchDto
{
    /// <summary>
    /// <c>snapshot</c> when grounded on TikTok trending keywords;
    /// <c>audienceSuggest</c> when invented (empty snapshot, or snapshot all generic / off-niche).
    /// </summary>
    public string Source { get; set; } = "snapshot";

    /// <summary>Vietnamese analysis bullets.</summary>
    public List<string> AnalysisPoints { get; set; } = [];

    /// <summary>Non-fatal issues (incomplete catalog audience, zero listing slots, …).</summary>
    public List<string> Warnings { get; set; } = [];

    /// <summary>Operational catalog category names.</summary>
    public List<string> Categories { get; set; } = [];

    /// <summary>Shop audience age lower bound.</summary>
    public int? AgeMin { get; set; }

    /// <summary>Shop audience age upper bound.</summary>
    public int? AgeMax { get; set; }

    /// <summary>Body/size code.</summary>
    public string? BodySizeCode { get; set; }

    /// <summary>Style codes.</summary>
    public List<string> StyleCodes { get; set; } = [];

    /// <summary>TikTok published listing count.</summary>
    public int? PublishLimitPublished { get; set; }

    /// <summary>TikTok listing cap.</summary>
    public int? PublishLimitTotal { get; set; }

    /// <summary>Remaining listing slots (0 when unknown or full).</summary>
    public int RemainingSlots { get; set; }

    /// <summary>Snapshot keyword count before filtering.</summary>
    public int SnapshotCount { get; set; }

    /// <summary>Keywords dropped as generic / too short.</summary>
    public int DroppedGeneric { get; set; }

    /// <summary>Non-generic keywords scored.</summary>
    public int CandidatesScored { get; set; }

    /// <summary>US shopping season label.</summary>
    public string Season { get; set; } = string.Empty;

    /// <summary>Vietnamese climate note for this month.</summary>
    public string WeatherNote { get; set; } = string.Empty;

    /// <summary>Retail events in the 21–56 day window.</summary>
    public List<string> UpcomingEvents { get; set; } = [];

    /// <summary>UTC date fed to the model (yyyy-MM-dd).</summary>
    public string AsOfDate { get; set; } = string.Empty;

    /// <summary>Last successful keyword snapshot sync.</summary>
    public DateTime? LastSyncedAt { get; set; }

    /// <summary>When this analysis was last written (UTC). Null on a fresh POST before persist.</summary>
    public DateTime? AnalyzedAt { get; set; }

    /// <summary>
    /// True when the shop's keyword snapshot is newer than <see cref="LastSyncedAt"/> stored with this analysis.
    /// Informational only — GET still returns the saved copy.
    /// </summary>
    public bool SnapshotChanged { get; set; }

    /// <summary>Keywords to list now (grounded snapshot rows).</summary>
    public List<ShopKeywordResearchItemDto> ListNow { get; set; } = [];

    /// <summary>Keywords for the 21–56 day horizon.</summary>
    public List<ShopKeywordResearchItemDto> Upcoming { get; set; } = [];
}

/// <summary>
/// One required / preferred match phrase plus synonym alternatives (denim = jean).
/// Hitting any <see cref="AnyOf"/> value on the Shein title/slug/cate counts.
/// </summary>
public class KeywordMatchPhraseDto
{
    /// <summary>Canonical phrase the analyzer requires (e.g. denim, dress).</summary>
    public string Phrase { get; set; } = string.Empty;

    /// <summary>Canonical plus synonyms; any one hit satisfies this phrase.</summary>
    public List<string> AnyOf { get; set; } = [];
}

/// <summary>Positive score contributions for the drawer tooltip (0–100 parts).</summary>
public class ShopKeywordScoreBreakdownDto
{
    /// <summary>Niche fit contribution (0–45).</summary>
    public int Audience { get; set; }

    /// <summary>Season / weather contribution (0–5).</summary>
    public int Season { get; set; }

    /// <summary>Demand (search volume vs the shop snapshot) contribution (0–30).</summary>
    public int Demand { get; set; }

    /// <summary>Recent search growth (volume ring) contribution (0–15).</summary>
    public int Growth { get; set; }

    /// <summary>Competition (online products sweet-spot) contribution (0–5).</summary>
    public int Competition { get; set; }
}

/// <summary>One recommended snapshot keyword.</summary>
public class ShopKeywordResearchItemDto
{
    /// <summary>Product opportunity row id.</summary>
    public Guid Id { get; set; }

    /// <summary>TikTok search keyword (English, from snapshot).</summary>
    public string Keyword { get; set; } = string.Empty;

    /// <summary>
    /// Sourcing query with the shop body/size modifier (SHEIN / paste).
    /// Computed on GET/POST from the live catalog profile — not stored as source of truth.
    /// </summary>
    public string SearchQuery { get; set; } = string.Empty;

    /// <summary>Organic SHEIN US pdsearch URL for <see cref="SearchQuery"/>.</summary>
    public string? SheinUrl { get; set; }

    /// <summary>Search volume.</summary>
    public int SearchVolume { get; set; }

    /// <summary>Competing listings when numeric.</summary>
    public int? OnlineProducts { get; set; }

    /// <summary>Raw competing-listing text.</summary>
    public string? OnlineProductsRaw { get; set; }

    /// <summary>Volume ring ratio.</summary>
    public string? SearchVolumeRingRatio { get; set; }

    /// <summary>TikTok lead tags.</summary>
    public List<string> LeadTagTexts { get; set; } = [];

    /// <summary>First image.</summary>
    public string? PicUrl { get; set; }

    /// <summary>All images.</summary>
    public List<string> PicUrls { get; set; } = [];

    /// <summary>Vietnamese rationale (≤ 20 words).</summary>
    public string? Rationale { get; set; }

    /// <summary>Short fit label from the model (audience/season).</summary>
    public string? Fit { get; set; }

    /// <summary>Saved for the shop owner.</summary>
    public bool IsSaved { get; set; }

    /// <summary>
    /// 1-based rank within the lane (<c>listNow</c> or <c>upcoming</c>).
    /// Restamped on GET after removals.
    /// </summary>
    public int? Rank { get; set; }

    /// <summary>
    /// Absolute 0–100 display score (snapshot path). Null for <c>audienceSuggest</c>
    /// or legacy payloads until re-analyze.
    /// </summary>
    public int? Score { get; set; }

    /// <summary>Positive contributions for the score tooltip.</summary>
    public ShopKeywordScoreBreakdownDto? ScoreBreakdown { get; set; }

    /// <summary>
    /// AI-chosen copy only. Gate phrases are stamped by coverage (distinctive + garment),
    /// not by this list. Persisted in research payload; GET re-stamps
    /// <see cref="MustPhrases"/> / <see cref="MinDistinctiveHits"/>.
    /// </summary>
    public List<string> MustTokens { get; set; } = [];

    /// <summary>
    /// Important phrases (at most 2 demand modifiers + primary garment) stamped on GET/POST.
    /// Coverage gate: garment + <see cref="MinDistinctiveHits"/> distinctive hits.
    /// <c>anyOf</c> = synonyms (denim = jean).
    /// </summary>
    public List<KeywordMatchPhraseDto> MustPhrases { get; set; } = [];

    /// <summary>Hardware / style / extra garments; title score uses weighted coverage.</summary>
    public List<KeywordMatchPhraseDto> ShouldPhrases { get; set; } = [];

    /// <summary>
    /// Minimum distinctive hits for the Shein title gate (<c>ceil(N/2)</c>).
    /// Stamped on GET/POST with <see cref="MustPhrases"/>.
    /// </summary>
    public int MinDistinctiveHits { get; set; }
}
