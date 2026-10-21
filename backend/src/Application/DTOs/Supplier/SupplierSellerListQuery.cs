namespace Application.DTOs.Supplier;

/// <summary>Query for supplier shop (NCC) list / detail KPI window.</summary>
public class SupplierSellerListQuery
{
    /// <summary>Page (1-based).</summary>
    public int PageCurrent { get; set; } = 1;

    /// <summary>Page size.</summary>
    public int PageSize { get; set; } = 20;

    /// <summary>Search store title / store code / soldByTips.</summary>
    public string? Search { get; set; }

    /// <summary>ProductSource code filter (SHEIN, TEMU, …).</summary>
    public string? Platform { get; set; }

    /// <summary>Admin-only seller owner filter (listings + orders).</summary>
    public Guid? OwnerUserId { get; set; }

    /// <summary>
    /// Period: <c>day</c> | <c>week</c> | <c>month</c> | <c>30d</c> (default <c>30d</c>).
    /// Ignored when <see cref="FromDate"/> and <see cref="ToDate"/> are both set.
    /// </summary>
    public string? Period { get; set; }

    /// <summary>Inclusive yyyy-MM-dd Asia/Ho_Chi_Minh. With ToDate, overrides Period.</summary>
    public string? FromDate { get; set; }

    /// <summary>Inclusive yyyy-MM-dd Asia/Ho_Chi_Minh.</summary>
    public string? ToDate { get; set; }

    /// <summary>Detail only — product page (1-based).</summary>
    public int ProductPageCurrent { get; set; } = 1;

    /// <summary>Detail only — product page size.</summary>
    public int ProductPageSize { get; set; } = 20;
}
