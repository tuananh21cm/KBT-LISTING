using Application.DTOs.FourSeller;
using Application.DTOs.Listing;
using Application.Interfaces;

namespace Application.Tests;

/// <summary>
/// Scriptable <see cref="IListingSyncService"/> double for auto publish pipeline tests.
/// Holds a single mutable detail snapshot so chained UpdateAsync calls behave like the real
/// mirror: whatever the pipeline writes is what the next step reads back.
/// </summary>
internal sealed class StubListingSyncService : IListingSyncService
{
    private readonly ListingDetailDto _detail;

    public StubListingSyncService(ListingDetailDto detail)
    {
        _detail = detail;
    }

    public List<string> Calls { get; } = [];
    public int UpdateCount { get; private set; }

    /// <summary>Live mirror the orchestrator has been mutating, for post-run assertions.</summary>
    public ListingDetailDto Detail => _detail;

    /// <summary>Attribute ids sent on each UpdateAsync, so tests can see a Specifics reset.</summary>
    public List<List<string>> AttributeSnapshots { get; } = [];

    public int PublishCount { get; private set; }
    public UpdateListingMirrorDto? LastPublishPayload { get; private set; }
    public string? LastPublishSource { get; private set; }
    public ListingShopMetadataDto? ShopMetadata { get; set; }

    /// <summary>Set to drop SizeChartCaptureUrl on every UpdateAsync round-trip (mirror regression).</summary>
    public bool DropSizeChartCaptureOnUpdate { get; set; }

    /// <summary>Runs after a successful PublishAsync — used to simulate a mid-finalise cancel.</summary>
    public Func<Task>? OnPublished { get; set; }

    /// <summary>Throws from PublishAsync when set.</summary>
    public Exception? PublishError { get; set; }

    public Task<ListingDetailDto> GetByIdAsync(
        Guid id,
        bool refreshFromFourSeller = false,
        CancellationToken cancellationToken = default)
    {
        Calls.Add(refreshFromFourSeller ? "GetByIdAsync:refresh" : "GetByIdAsync");
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_detail);
    }

    public Task<ListingDetailDto> UpdateAsync(
        Guid id,
        UpdateListingMirrorDto dto,
        CancellationToken cancellationToken = default)
    {
        Calls.Add(nameof(UpdateAsync));
        cancellationToken.ThrowIfCancellationRequested();
        UpdateCount++;

        // Mirror the real service: the update payload becomes the new snapshot.
        _detail.CategoryId = dto.CategoryId;
        _detail.CategoryName = dto.CategoryName;
        _detail.WarehouseId = dto.WarehouseId;
        _detail.PackageWeight = dto.PackageWeight;
        _detail.PackageLength = dto.PackageLength;
        _detail.PackageWidth = dto.PackageWidth;
        _detail.PackageHeight = dto.PackageHeight;
        _detail.ProductName = dto.ProductName;
        _detail.DescriptionHtml = dto.DescriptionHtml;

        if (dto.Attributes is not null)
        {
            AttributeSnapshots.Add(dto.Attributes.Select(a => a.AttrId ?? string.Empty).ToList());
            _detail.Attributes = dto.Attributes
                .Select(a => new ListingAttributeDto
                {
                    AttrId = a.AttrId,
                    AttrName = a.AttrName,
                    AttrValue = a.AttrValue,
                    ValueId = a.ValueId,
                    ValueType = a.ValueType,
                    IsRequired = a.IsRequired
                })
                .ToList();
        }

        if (DropSizeChartCaptureOnUpdate)
        {
            _detail.SizeChartCaptureUrl = null;
        }

        return Task.FromResult(_detail);
    }

    public async Task<ListingPushResultDto> PublishAsync(
        Guid id,
        UpdateListingMirrorDto? dto = null,
        string? publishSource = null,
        CancellationToken cancellationToken = default)
    {
        Calls.Add(nameof(PublishAsync));
        cancellationToken.ThrowIfCancellationRequested();

        if (PublishError is not null)
        {
            throw PublishError;
        }

        PublishCount++;
        LastPublishPayload = dto;
        LastPublishSource = publishSource;

        if (OnPublished is not null)
        {
            await OnPublished();
        }

        return new ListingPushResultDto { Success = true, ListingId = id };
    }

    public Task<ListingDetailDto> SyncOneAsync(Guid id, CancellationToken cancellationToken = default)
    {
        Calls.Add(nameof(SyncOneAsync));
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_detail);
    }

    public Task<IReadOnlyList<FourSellerRecentUseCategoryDto>> GetRecentCategoriesAsync(
        Guid id,
        long? shopId = null,
        string? site = null,
        CancellationToken cancellationToken = default)
    {
        Calls.Add(nameof(GetRecentCategoriesAsync));
        return Task.FromResult<IReadOnlyList<FourSellerRecentUseCategoryDto>>([]);
    }

    public Task BindCategoryAsync(
        Guid id,
        BindListingCategoryRequest request,
        CancellationToken cancellationToken = default)
    {
        Calls.Add(nameof(BindCategoryAsync));
        _detail.CategoryId = request.CategoryId;
        _detail.CategoryName = request.CategoryName;
        return Task.CompletedTask;
    }

    public Task<ListingShopMetadataDto> GetShopMetadataAsync(
        Guid listingId,
        Guid shopId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(ShopMetadata ?? new ListingShopMetadataDto { ShopId = shopId });

    public Task<FourSellerCosUploadResultDto> UploadCosMediaAsync(
        Guid listingId,
        Stream content,
        string fileName,
        string contentType,
        string? referenceType = null,
        CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    public Task<ListingListPageDto> ListAsync(ListingListQuery query, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    public Task<ListingRecentPageDto> ListRecentListingsAsync(
        Guid ownerId,
        string? pageType = null,
        IReadOnlyList<long>? fourSellerShopIds = null,
        int pageCurrent = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    public Task<ListingTabCountsDto> GetTabCountsAsync(
        Guid ownerId,
        IReadOnlyList<long>? fourSellerShopIds = null,
        CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    public Task<IReadOnlyList<AddedSourceShopDto>> ListOwnedAddedSourceShopsAsync(
        Guid ownerId,
        int take = 5000,
        CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    public Task<ListingStatusCountsDto> GetStatusCountsAsync(
        ListingListQuery query,
        CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    public Task<IReadOnlyList<ListingAutoPublishQueueStatusItemDto>> GetAutoPublishQueueStatusAsync(
        IReadOnlyList<Guid> listingIds,
        Guid? ownerUserId = null,
        CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    public Task<ListingSyncResultDto> SyncAsync(
        ListingSyncRequest request,
        IProgress<ListingSyncSellerProgressDto>? progress = null,
        CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    public Task<ListingOwnerSyncResultDto> SyncOwnerAsync(
        Guid ownerUserId,
        string platform,
        IReadOnlyList<string> statuses,
        string? shopId,
        DateTime? dateFrom,
        DateTime? dateTo,
        int? maxRecordsPerSeller,
        string? syncScope = null,
        IReadOnlyList<string>? listingIds = null,
        int? reconcileMaxPerSeller = null,
        bool purgeMissingOnReconcile = false,
        int reconcileMaxRounds = 1,
        CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    public Task<ListingDescriptionPrepResultDto> SaveDescriptionPrepAsync(
        Guid id,
        UpdateListingDescriptionPrepDto dto,
        CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    public Task<ListingSourceUrlResultDto> UpdateSourceUrlAsync(
        Guid id,
        UpdateListingSourceUrlDto dto,
        CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    public Task<ListingPushResultDto> SaveDraftAsync(
        Guid id,
        UpdateListingMirrorDto? dto = null,
        CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    public Task<ListingPushPreviewDto> BuildPushPreviewAsync(
        Guid id,
        bool includePayload = false,
        CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    public Task<ListingPushResultDto> PushUpdateAsync(
        Guid id,
        UpdateListingMirrorDto? dto = null,
        CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    public Task<ListingPushResultDto> DeactivateAsync(Guid id, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    public Task<ListingDeactivateResultDto> DeactivateByIdsAsync(
        IReadOnlyList<Guid> ids,
        CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    public Task<ListingPushResultDto> ActivateAsync(Guid id, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    public Task<ListingDetailDto> DuplicateAsync(Guid id, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    public Task<FourSellerMoveSourceListingDto> GetMoveSourceAsync(
        Guid id,
        CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    public Task<IReadOnlyList<FourSellerBrandDto>> SuggestBrandsAsync(
        Guid listingId,
        Guid shopId,
        string brandName,
        CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    public Task DeleteByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    public Task DeleteOwnedDraftAsync(Guid ownerId, Guid id, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    public Task<ListingDeleteResultDto> DeleteByFilterAsync(
        ListingListQuery query,
        CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    public Task<ListingDeleteResultDto> DeleteByIdsAsync(
        IReadOnlyList<Guid> ids,
        CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();
}
