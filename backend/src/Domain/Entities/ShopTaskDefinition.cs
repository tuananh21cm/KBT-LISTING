using Domain.Common;

namespace Domain.Entities;

/// <summary>
/// Nhiệm vụ hàng ngày kiểu "truy cập URL" do admin cấu hình — áp dụng cho mọi shop đang Active.
/// Người dùng tự vào trang (bấm nhiệm vụ trong extension hoặc đi bằng menu TikTok); extension
/// quan sát URL đang mở, khớp thì báo về (<see cref="ShopTaskCompletion"/>).
/// </summary>
public class ShopTaskDefinition : BaseEntity
{
    /// <summary>Khoá chính.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Tên hiển thị trên bảng nhiệm vụ.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>URL đích người dùng phải tới (https, host thuộc allowlist).</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// Prefix so khớp URL đang mở (sau redirect); null thì dùng origin + path của <see cref="Url"/>.
    /// </summary>
    public string? UrlMatchPattern { get; set; }

    /// <summary>Mô tả ngắn cho seller hiểu vì sao phải làm.</summary>
    public string? Description { get; set; }

    /// <summary>
    /// Số giây người dùng phải ở lại trang sau khi load xong; <c>0</c> = tính ngay khi trang mở.
    /// </summary>
    public int ViewDurationSeconds { get; set; }

    /// <summary>Thứ tự hiển thị.</summary>
    public int SortOrder { get; set; }

    /// <summary>Tắt để ngừng giao nhiệm vụ mà không mất lịch sử.</summary>
    public bool IsActive { get; set; } = true;
}
