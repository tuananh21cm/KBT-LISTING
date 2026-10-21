namespace Application.DTOs.FourSeller;

/// <summary>
/// Request to list 4Seller move-draft (scraped) products with local ownership filter.
/// </summary>
public class FourSellerMoveDraftPageRequest
{
    /// <summary>1-based page index.</summary>
    public int PageCurrent { get; set; } = 1;

    /// <summary>Page size.</summary>
    public int PageSize { get; set; } = 100;

    /// <summary>Optional search text.</summary>
    public string? SearchValue { get; set; }

    /// <summary>When set, claim/match pending URLs for this batch then list.</summary>
    public Guid? BatchId { get; set; }

    /// <summary>
    /// When true with <see cref="BatchId"/>, abandon unmatched pending URLs for that batch
    /// (exit crawl poll pool) and mark batch Failed if nothing was claimed.
    /// </summary>
    public bool AbandonPending { get; set; }

    /// <summary>Default true — only drafts owned by current user (or createdById filter).</summary>
    public bool MineOnly { get; set; } = true;

    /// <summary>Admin-only: filter by another user's drafts. Ignored when MineOnly and not Admin.</summary>
    public Guid? CreatedById { get; set; }
}
