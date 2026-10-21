namespace Domain.Entities;

/// <summary>
/// One product attribute value TikTok refuses to accept, learned from a real publish failure
/// (<c>attribute ID `100401` does not support custom value names</c>).
/// <para>
/// 4Seller keeps offering such a value with a numeric id long after TikTok stops honouring it, so the
/// schema cannot be trusted for enum membership and the failure itself is the only reliable signal.
/// </para>
/// </summary>
public class TikTokAttributeValueRejection
{
    /// <summary>Sentinel <see cref="CategoryId"/> meaning the value is blocked in every category.</summary>
    public const string AllCategories = "*";

    /// <summary>Row identifier.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// TikTok category the rejection was observed in, or <see cref="AllCategories"/> for every category.
    /// Scoped per category on purpose: one attribute id carries a different enum per category, so a
    /// value withdrawn from one category may still be valid elsewhere.
    /// </summary>
    public string CategoryId { get; set; } = AllCategories;

    /// <summary>TikTok attribute id (Int64 as text), e.g. <c>100401</c>.</summary>
    public string AttributeId { get; set; } = string.Empty;

    /// <summary>Attribute label as TikTok reported it, for the admin list.</summary>
    public string? AttributeName { get; set; }

    /// <summary>Value id 4Seller offered, when known. TikTok only names the value in the error.</summary>
    public string? ValueId { get; set; }

    /// <summary>Rejected value name exactly as TikTok reported it.</summary>
    public string ValueName { get; set; } = string.Empty;

    /// <summary>Normalized <see cref="ValueName"/> used for matching and for the unique index.</summary>
    public string ValueNameNorm { get; set; } = string.Empty;

    /// <summary>How many publishes this value has broken.</summary>
    public int RejectionCount { get; set; }

    /// <summary>UTC first observation.</summary>
    public DateTime FirstSeenAt { get; set; }

    /// <summary>UTC most recent observation.</summary>
    public DateTime LastSeenAt { get; set; }

    /// <summary>Where the row came from — see <c>TikTokAttributeRejectionSources</c>.</summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>A listing that hit the rejection, for tracing.</summary>
    public Guid? SampleListingId { get; set; }

    /// <summary>Original error text, truncated.</summary>
    public string? SampleError { get; set; }
}
