using Domain.Common;

namespace Domain.Entities;

/// <summary>
/// Local ownership mirror of a 4Seller crawl move-draft for one user account.
/// Unique per (CreatedById, FourSellerMoveDraftId).
/// </summary>
public class ScrapeDraft : BaseEntity
{
    /// <summary>Row identifier.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Owner user (who scraped / claimed).</summary>
    public Guid CreatedById { get; set; }

    /// <summary>4Seller move-draft id (unique per user).</summary>
    public long FourSellerMoveDraftId { get; set; }

    /// <summary>Optional originating batch.</summary>
    public Guid? BatchId { get; set; }

    /// <summary>Source product URL.</summary>
    public string? SourceUrl { get; set; }

    /// <summary>Normalized source key.</summary>
    public string SourceUrlKey { get; set; } = string.Empty;

    /// <summary>Product title snapshot.</summary>
    public string? ProductName { get; set; }

    /// <summary>Thumbnail URL snapshot.</summary>
    public string? ImageUrl { get; set; }

    /// <summary>4Seller draft status code.</summary>
    public int Status { get; set; }

    /// <summary>Variant count snapshot.</summary>
    public int VariantCount { get; set; }

    /// <summary>Low price snapshot.</summary>
    public decimal? LowPrice { get; set; }

    /// <summary>High price snapshot.</summary>
    public decimal? HighPrice { get; set; }

    /// <summary>Currency snapshot.</summary>
    public string? Currency { get; set; }

    /// <summary>4Seller creator display name.</summary>
    public string? Creator { get; set; }

    /// <summary>4Seller createTime string.</summary>
    public string? FourSellerCreateTime { get; set; }

    /// <summary>Last time we synced snapshot from 4Seller.</summary>
    public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;

    /// <summary>Owner navigation.</summary>
    public ApplicationUser CreatedBy { get; set; } = null!;

    /// <summary>Batch navigation.</summary>
    public ScrapeBatch? Batch { get; set; }
}
