namespace Application.DTOs.Shop;

/// <summary>Payload extension POST lên <c>/shops/tiktok-shop-scores/sync</c>.</summary>
public class SyncTikTokShopScoresDto
{
    /// <summary>TikTok seller id.</summary>
    public string? ExternalShopId { get; set; }

    /// <summary>Alias của <see cref="ExternalShopId"/>.</summary>
    public string? SellerId { get; set; }

    /// <summary>Mã lượt unified Sync.</summary>
    public string? RunId { get; set; }

    /// <summary>Health Center AHR score (<c>account/ahr/score</c> → <c>data.score</c>).</summary>
    public int? AhrScore { get; set; }

    /// <summary>Shop Performance Score tổng (<c>overview.score</c>).</summary>
    public decimal? PerformanceScore { get; set; }

    /// <summary>Nhóm <c>020100</c> — Product satisfaction.</summary>
    public decimal? ProductSatisfactionScore { get; set; }

    /// <summary>Nhóm <c>020200</c> — Fulfillment and logistics.</summary>
    public decimal? FulfillmentScore { get; set; }

    /// <summary>Nhóm <c>020300</c> — Customer service.</summary>
    public decimal? CustomerServiceScore { get; set; }
}

/// <summary>Kết quả sync điểm shop.</summary>
public class SyncTikTokShopScoresResultDto
{
    /// <summary>Shop local id.</summary>
    public Guid ShopId { get; set; }

    /// <summary>Tên các cột thực sự được ghi trong lượt này.</summary>
    public List<string> UpdatedFields { get; set; } = [];
}
