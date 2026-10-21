using Domain.Common;

namespace Domain.Entities;

/// <summary>
/// One source URL inside a scrape batch.
/// </summary>
public class ScrapeBatchUrl : BaseEntity
{
    /// <summary>Row identifier.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Parent batch.</summary>
    public Guid BatchId { get; set; }

    /// <summary>Original product URL.</summary>
    public string SourceUrl { get; set; } = string.Empty;

    /// <summary>Normalized key for matching (e.g. Shein p:id).</summary>
    public string SourceUrlKey { get; set; } = string.Empty;

    /// <summary>Matched 4Seller move-draft id when claimed.</summary>
    public long? MatchedMoveDraftId { get; set; }

    /// <summary>Parent batch navigation.</summary>
    public ScrapeBatch Batch { get; set; } = null!;
}
