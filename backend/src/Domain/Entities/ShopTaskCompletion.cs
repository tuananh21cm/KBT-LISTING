namespace Domain.Entities;

/// <summary>
/// Kết quả một shop thực hiện <see cref="ShopTaskDefinition"/> trong một ngày VN.
/// Unique theo (ShopId, DefinitionId, TaskDate) — upsert nên mỗi ngày chỉ một dòng.
/// </summary>
public class ShopTaskCompletion
{
    /// <summary>Khoá chính.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Nhiệm vụ tương ứng.</summary>
    public Guid DefinitionId { get; set; }

    /// <summary>Shop local đã thực hiện.</summary>
    public Guid ShopId { get; set; }

    /// <summary>Owner shop tại thời điểm ghi nhận.</summary>
    public Guid OwnerId { get; set; }

    /// <summary>TikTok seller id (<c>Shop.ExternalShopId</c>).</summary>
    public string ExternalShopId { get; set; } = string.Empty;

    /// <summary>Ngày lịch VN của nhiệm vụ.</summary>
    public DateOnly TaskDate { get; set; }

    /// <summary>
    /// <c>done</c> hoặc <c>failed</c> — xem <c>Shared.Constants.ShopTaskStatuses</c>.
    /// </summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>URL extension nhìn thấy khi trang đã load xong.</summary>
    public string? VisitedUrl { get; set; }

    /// <summary>Lý do khi <see cref="Status"/> là failed.</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>Thời điểm ghi nhận (UTC).</summary>
    public DateTime CompletedAt { get; set; }

    /// <summary>Nguồn ghi nhận — xem <c>Shared.Constants.ShopTaskSources</c>.</summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>Navigation tới nhiệm vụ.</summary>
    public ShopTaskDefinition Definition { get; set; } = null!;

    /// <summary>Navigation tới shop.</summary>
    public Shop Shop { get; set; } = null!;
}
