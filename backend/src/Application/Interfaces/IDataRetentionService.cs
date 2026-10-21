namespace Application.Interfaces;

/// <summary>
/// Dọn các bảng chỉ-ghi-thêm mà không có luồng nào xoá: nhật ký sync, snapshot phân tích, refresh
/// token đã hết vòng đời.
/// </summary>
public interface IDataRetentionService
{
    /// <summary>Chạy một lượt dọn, trả về số dòng đã xoá theo từng bảng.</summary>
    Task<IReadOnlyDictionary<string, int>> PurgeAsync(CancellationToken cancellationToken = default);
}
