using Application.DTOs.Category;

namespace Application.Interfaces;

/// <summary>
/// TikTok category catalog sync and query service.
/// </summary>
public interface ICategoryService
{
    /// <summary>
    /// Syncs the system-wide category tree from 4Seller for a marketplace site.
    /// Uses any user cookie with status <c>logged_in</c> (prefers <paramref name="preferredUserId"/> when eligible).
    /// </summary>
    Task<CategorySyncResultDto> SyncFromFourSellerAsync(
        Guid preferredUserId,
        SyncCategoriesRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the category tree for Cascader / Tree UI.
    /// </summary>
    Task<List<CategoryTreeNodeDto>> GetTreeAsync(
        string site,
        bool activeOnly = true,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns only the direct children of <paramref name="parentExternalId"/>
    /// (root nodes when null/empty) for lazy Cascader loading.
    /// </summary>
    Task<List<CategoryTreeNodeDto>> GetChildrenAsync(
        string site,
        string? parentExternalId,
        bool activeOnly = true,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Builds the lazy-cascader seed (option tree pre-expanded to <paramref name="externalId"/>).
    /// </summary>
    Task<CategoryPathDto> GetPathAsync(
        string site,
        string externalId,
        bool activeOnly = true,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a flat list, optionally filtered by search text.
    /// <paramref name="take"/> caps the result (search picker); omit for the full admin table.
    /// </summary>
    Task<List<CategoryDto>> GetListAsync(
        string site,
        string? search = null,
        bool activeOnly = true,
        int? take = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds a category by external ID within a site.
    /// </summary>
    Task<CategoryDto?> GetByExternalIdAsync(
        string site,
        string externalId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Recommends a TikTok category via 4Seller AI from product title.
    /// </summary>
    /// <param name="actingUserId">JWT user (admin or seller).</param>
    /// <param name="allowOwnerOverride">
    /// When true (admin), <see cref="RecommendCategoryRequest.OwnerUserId"/> may select another seller's cookie;
    /// when absent, falls back to any <c>logged_in</c> cookie.
    /// </param>
    Task<RecommendCategoryResultDto> RecommendCategoryAsync(
        Guid actingUserId,
        RecommendCategoryRequest request,
        CancellationToken cancellationToken = default,
        bool allowOwnerOverride = false);
}
