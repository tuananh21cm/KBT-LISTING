namespace Application.DTOs.Listing;

/// <summary>
/// Slim paged listing page for the browser extension (no statusCounts).
/// </summary>
public class ListingRecentPageDto
{
    /// <summary>Rows for the requested page only.</summary>
    public List<ListingListItemDto> Items { get; set; } = [];

    /// <summary>Total matching rows (all pages).</summary>
    public int Total { get; set; }

    /// <summary>Current 1-based page.</summary>
    public int PageCurrent { get; set; }

    /// <summary>Page size.</summary>
    public int PageSize { get; set; }
}
