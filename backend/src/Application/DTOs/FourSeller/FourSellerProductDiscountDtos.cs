namespace Application.DTOs.FourSeller;

/// <summary>Request for 4Seller product-discount activity page.</summary>
public class FourSellerProductDiscountPageRequest
{
    /// <summary>1-based page.</summary>
    public int PageCurrent { get; set; } = 1;

    /// <summary>Page size (capped server-side).</summary>
    public int PageSize { get; set; } = 100;

    /// <summary>Filter discount type, or empty for all.</summary>
    public string DiscountType { get; set; } = string.Empty;

    /// <summary>4Seller shop id filter, or empty for all shops.</summary>
    public string ShopId { get; set; } = string.Empty;

    /// <summary>Filter promotion status, or empty for all.</summary>
    public string PromotionStatus { get; set; } = string.Empty;

    /// <summary>Search field (default activityName).</summary>
    public string SearchType { get; set; } = "activityName";

    /// <summary>Search value.</summary>
    public string SearchValue { get; set; } = string.Empty;
}

/// <summary>Paged response from 4Seller product-discount get-page.</summary>
public class FourSellerProductDiscountPageDto
{
    /// <summary>Activity rows.</summary>
    public List<FourSellerProductDiscountRecordDto> Records { get; set; } = [];

    /// <summary>Total matching rows.</summary>
    public int Total { get; set; }

    /// <summary>Page size used.</summary>
    public int PageSize { get; set; }

    /// <summary>Current page.</summary>
    public int PageCurrent { get; set; }
}

/// <summary>One product-discount activity from 4Seller.</summary>
public class FourSellerProductDiscountRecordDto
{
    /// <summary>4Seller activity id.</summary>
    public long Id { get; set; }

    /// <summary>4Seller shop id.</summary>
    public long ShopId { get; set; }

    /// <summary>Shop name.</summary>
    public string ShopName { get; set; } = string.Empty;

    /// <summary>Platform activity id.</summary>
    public string PlatformActivityId { get; set; } = string.Empty;

    /// <summary>Activity name.</summary>
    public string ActivityName { get; set; } = string.Empty;

    /// <summary>Discount type.</summary>
    public string DiscountType { get; set; } = string.Empty;

    /// <summary>Begin time epoch ms.</summary>
    public long BeginTime { get; set; }

    /// <summary>End time epoch ms.</summary>
    public long EndTime { get; set; }

    /// <summary>Promotion status.</summary>
    public string PromotionStatus { get; set; } = string.Empty;

    /// <summary>Product level.</summary>
    public string ProductLevel { get; set; } = string.Empty;

    /// <summary>Product count.</summary>
    public int ProductCount { get; set; }

    /// <summary>Failed product count.</summary>
    public int ProductFailCount { get; set; }

    /// <summary>Error message.</summary>
    public string? ErrorMessage { get; set; }
}
