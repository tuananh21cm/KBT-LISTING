using Domain.Common;

namespace Domain.Entities;

/// <summary>
/// A scrape submission batch owned by one user.
/// </summary>
public class ScrapeBatch : BaseEntity
{
    /// <summary>Batch identifier.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>User who submitted the URLs.</summary>
    public Guid CreatedById { get; set; }

    /// <summary>Pending / Ready / Failed.</summary>
    public string Status { get; set; } = "Pending";

    /// <summary>Number of URLs submitted.</summary>
    public int SubmittedUrlCount { get; set; }

    /// <summary>Creator navigation.</summary>
    public ApplicationUser CreatedBy { get; set; } = null!;

    /// <summary>Submitted URLs.</summary>
    public ICollection<ScrapeBatchUrl> Urls { get; set; } = [];

    /// <summary>Claimed drafts from this batch.</summary>
    public ICollection<ScrapeDraft> Drafts { get; set; } = [];
}
