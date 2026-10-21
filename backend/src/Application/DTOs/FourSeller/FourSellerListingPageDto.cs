namespace Application.DTOs.FourSeller;

/// <summary>
/// Paged listing list from 4Seller.
/// </summary>
public class FourSellerListingPageDto
{
    /// <summary>Listing records.</summary>
    public List<FourSellerListingRecordDto> Records { get; set; } = [];

    /// <summary>Total matching records.</summary>
    public int Total { get; set; }

    /// <summary>Page size.</summary>
    public int PageSize { get; set; }

    /// <summary>Current page.</summary>
    public int PageCurrent { get; set; }
}
