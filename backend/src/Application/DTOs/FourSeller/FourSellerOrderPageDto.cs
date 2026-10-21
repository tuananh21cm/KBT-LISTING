namespace Application.DTOs.FourSeller;

/// <summary>
/// Paged order list from 4Seller.
/// </summary>
public class FourSellerOrderPageDto
{
    /// <summary>Order records.</summary>
    public List<FourSellerOrderRecordDto> Records { get; set; } = [];

    /// <summary>Total matching records.</summary>
    public int Total { get; set; }

    /// <summary>Page size.</summary>
    public int PageSize { get; set; }

    /// <summary>Current page.</summary>
    public int PageCurrent { get; set; }
}
