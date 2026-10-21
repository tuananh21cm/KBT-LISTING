namespace Application.DTOs.Listing;

/// <summary>
/// Hybrid Specifics mapping result for admin review before applying to the form.
/// </summary>
public class MapListingSpecificsResultDto
{
    /// <summary>Pairs successfully mapped onto schema attribute/value ids.</summary>
    public List<MappedListingSpecificDto> Matched { get; set; } = [];

    /// <summary>Parsed source pairs that could not be mapped confidently.</summary>
    public List<UnmappedListingSpecificDto> Unmatched { get; set; } = [];

    /// <summary>Required category attributes still missing after mapping.</summary>
    public List<MissingRequiredSpecificDto> MissingRequired { get; set; } = [];

    /// <summary>Number of schema attributes returned by 4Seller for the category.</summary>
    public int SchemaAttributeCount { get; set; }

    /// <summary>Number of <c>Key: Value</c> pairs parsed from the description.</summary>
    public int ParsedPairCount { get; set; }
}

/// <summary>One confidently mapped Specifics suggestion.</summary>
public class MappedListingSpecificDto
{
    /// <summary>Original key from description.</summary>
    public string SourceName { get; set; } = string.Empty;

    /// <summary>Original value from description.</summary>
    public string SourceValue { get; set; } = string.Empty;

    /// <summary>4Seller / TikTok attribute id.</summary>
    public string AttrId { get; set; } = string.Empty;

    /// <summary>Schema attribute name.</summary>
    public string AttrName { get; set; } = string.Empty;

    /// <summary>
    /// Resolved display value (single or comma-joined when <see cref="Values"/> has multiple).
    /// </summary>
    public string AttrValue { get; set; } = string.Empty;

    /// <summary>
    /// TikTok value id when known (enum); first id when multi; null/empty for custom text.
    /// </summary>
    public string? ValueId { get; set; }

    /// <summary>
    /// Canonical enum values for this attribute. Single-select → 0–1 item;
    /// <c>isMultipleSelected=1</c> may have several. Prefer this over AttrValue/ValueId when pushing.
    /// </summary>
    public List<MappedListingSpecificValueDto> Values { get; set; } = [];

    /// <summary>Whether the category marks this attribute as required.</summary>
    public bool IsRequired { get; set; }

    /// <summary>0–1 confidence score.</summary>
    public double Confidence { get; set; }

    /// <summary>How the match was produced: Exact / Fuzzy / Alias / Ai.</summary>
    public string MatchMethod { get; set; } = string.Empty;

    /// <summary>
    /// True when an existing form row already has a non-empty value for this attribute —
    /// FE should keep the user's value unless they explicitly overwrite.
    /// </summary>
    public bool SkippedExisting { get; set; }
}

/// <summary>One canonical enum value under a mapped Specifics suggestion.</summary>
public class MappedListingSpecificValueDto
{
    /// <summary>TikTok value id (empty for custom free-text).</summary>
    public string? ValueId { get; set; }

    /// <summary>Display value name from schema (or custom text).</summary>
    public string ValueName { get; set; } = string.Empty;
}

/// <summary>Parsed description pair that was not mapped.</summary>
public class UnmappedListingSpecificDto
{
    /// <summary>Original key.</summary>
    public string SourceName { get; set; } = string.Empty;

    /// <summary>Original value.</summary>
    public string SourceValue { get; set; } = string.Empty;

    /// <summary>Human-readable reason.</summary>
    public string Reason { get; set; } = string.Empty;
}

/// <summary>Required schema attribute still empty after mapping.</summary>
public class MissingRequiredSpecificDto
{
    /// <summary>Attribute id.</summary>
    public string AttrId { get; set; } = string.Empty;

    /// <summary>Attribute name.</summary>
    public string AttrName { get; set; } = string.Empty;
}
