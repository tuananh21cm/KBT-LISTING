using Application.DTOs.ShopTask;

namespace Application.Interfaces;

/// <summary>Nhiệm vụ hệ thống theo shop: bảng nhiệm vụ, CRUD nhiệm vụ URL, ghi nhận từ extension.</summary>
public interface IShopTaskService
{
    /// <summary>
    /// Bảng nhiệm vụ trong một ngày VN. <paramref name="ownerScope"/> khác null thì chỉ lấy shop
    /// của owner đó (portal seller).
    /// </summary>
    Task<ShopTaskBoardDto> GetBoardAsync(
        Guid? ownerScope,
        ShopTaskBoardQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>Danh sách định nghĩa nhiệm vụ truy cập URL (gồm cả đang tắt).</summary>
    Task<List<ShopTaskDefinitionDto>> ListDefinitionsAsync(CancellationToken cancellationToken = default);

    /// <summary>Tạo nhiệm vụ truy cập URL.</summary>
    Task<ShopTaskDefinitionDto> CreateDefinitionAsync(
        SaveShopTaskDefinitionDto dto,
        CancellationToken cancellationToken = default);

    /// <summary>Cập nhật nhiệm vụ truy cập URL.</summary>
    Task<ShopTaskDefinitionDto> UpdateDefinitionAsync(
        Guid id,
        SaveShopTaskDefinitionDto dto,
        CancellationToken cancellationToken = default);

    /// <summary>Xoá nhiệm vụ truy cập URL cùng toàn bộ lịch sử của nó.</summary>
    Task DeleteDefinitionAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Sắp xếp lại thứ tự hiển thị (id đầu = SortOrder 0).</summary>
    Task ReorderDefinitionsAsync(
        ReorderShopTaskDefinitionsDto dto,
        CancellationToken cancellationToken = default);

    /// <summary>Danh sách nhiệm vụ extension cần chạy hôm nay cho một seller id.</summary>
    Task<ShopTaskExtensionPlanDto> GetExtensionPlanAsync(
        Guid ownerId,
        string? externalShopId,
        CancellationToken cancellationToken = default);

    /// <summary>Ghi nhận kết quả extension truy cập URL (upsert theo shop/nhiệm vụ/ngày).</summary>
    Task RecordVisitAsync(
        Guid ownerId,
        RecordShopTaskVisitDto dto,
        CancellationToken cancellationToken = default);
}
