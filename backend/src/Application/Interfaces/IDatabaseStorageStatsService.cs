using Application.DTOs.Maintenance;

namespace Application.Interfaces;

/// <summary>
/// Đọc thống kê dung lượng của Postgres (heap / TOAST / index / bloat) từ catalog.
/// Chỉ đọc — mọi tác vụ nặng (REINDEX, VACUUM FULL) do admin chạy tay.
/// </summary>
public interface IDatabaseStorageStatsService
{
    /// <summary>
    /// Trả về phân bổ dung lượng theo bảng và theo từng index.
    /// </summary>
    /// <param name="tableName">
    /// Chỉ lấy index của một bảng (khớp không phân biệt hoa thường). <c>null</c> = lấy index lớn nhất
    /// toàn schema.
    /// </param>
    /// <param name="top">Số dòng tối đa mỗi danh sách.</param>
    Task<DatabaseStorageStatsDto> GetAsync(
        string? tableName,
        int top,
        CancellationToken cancellationToken = default);
}
