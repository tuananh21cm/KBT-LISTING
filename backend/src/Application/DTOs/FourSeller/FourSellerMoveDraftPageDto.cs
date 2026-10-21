namespace Application.DTOs.FourSeller;

/// <summary>
/// Paged move-draft list from 4Seller crawl.
/// </summary>
public class FourSellerMoveDraftPageDto
{
    /// <summary>Draft records.</summary>
    public List<FourSellerMoveDraftRecordDto> Records { get; set; } = [];

    /// <summary>Total matching records.</summary>
    public int Total { get; set; }

    /// <summary>Page size.</summary>
    public int PageSize { get; set; }

    /// <summary>Current page.</summary>
    public int PageCurrent { get; set; }
}
