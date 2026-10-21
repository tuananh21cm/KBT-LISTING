namespace Application.DTOs.Listing;

/// <summary>
/// One learned TikTok attribute value rejection, as shown in admin.
/// </summary>
public class TikTokAttributeValueRejectionDto
{
    /// <summary>Row identifier, used to unblock the value.</summary>
    public Guid Id { get; set; }

    /// <summary>TikTok category the value is blocked in (<c>*</c> = every category).</summary>
    public string CategoryId { get; set; } = string.Empty;

    /// <summary>TikTok attribute id, e.g. <c>100401</c>.</summary>
    public string AttributeId { get; set; } = string.Empty;

    /// <summary>Attribute label as TikTok reported it.</summary>
    public string? AttributeName { get; set; }

    /// <summary>Value id 4Seller offered, when known.</summary>
    public string? ValueId { get; set; }

    /// <summary>Blocked value name.</summary>
    public string ValueName { get; set; } = string.Empty;

    /// <summary>
    /// How many times the value had to be learned the hard way. Normally 1: once blocked, later
    /// publishes replace the value instead of failing on it again.
    /// </summary>
    public int RejectionCount { get; set; }

    /// <summary>UTC first observation.</summary>
    public DateTime FirstSeenAt { get; set; }

    /// <summary>UTC most recent observation.</summary>
    public DateTime LastSeenAt { get; set; }

    /// <summary>Where the row came from: <c>seed</c>, <c>publish_error</c> or <c>manual</c>.</summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>A listing that hit the rejection.</summary>
    public Guid? SampleListingId { get; set; }

    /// <summary>Original error text, truncated.</summary>
    public string? SampleError { get; set; }
}
