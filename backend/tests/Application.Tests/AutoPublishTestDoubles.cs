using Application.DTOs.Category;
using Application.DTOs.FourSeller;
using Application.DTOs.Listing;
using Application.DTOs.PricingTemplate;
using Application.DTOs.ShippingTemplate;
using Application.Interfaces;

namespace Application.Tests;

/// <summary>
/// Hand-written doubles shared by the auto publish tests. Every member throws by default so a
/// test only has to configure the calls its scenario actually exercises.
/// </summary>
internal sealed class StubPricingTemplateService : IPricingTemplateService
{
    public List<PricingTemplateDto> Templates { get; } = [];

    public Task<List<PricingTemplateDto>> GetAllAsync(Guid userId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Templates.ToList());

    public Task<PricingTemplateDto> GetByIdAsync(Guid userId, Guid id, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    public Task<PricingTemplateDto> CreateAsync(
        Guid userId,
        CreatePricingTemplateDto dto,
        CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    public Task<PricingTemplateDto> UpdateAsync(
        Guid userId,
        Guid id,
        UpdatePricingTemplateDto dto,
        CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    public Task DeleteAsync(Guid userId, Guid id, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();
}

internal sealed class StubShippingTemplateService : IShippingTemplateService
{
    public List<ShippingTemplateDto> Templates { get; } = [];

    public Task<List<ShippingTemplateDto>> GetAllAsync(Guid userId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Templates.ToList());

    public Task<ShippingTemplateDto> GetByIdAsync(Guid userId, Guid id, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    public Task<ShippingTemplateDto> CreateAsync(
        Guid userId,
        CreateShippingTemplateDto dto,
        CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    public Task<ShippingTemplateDto> UpdateAsync(
        Guid userId,
        Guid id,
        UpdateShippingTemplateDto dto,
        CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    public Task DeleteAsync(Guid userId, Guid id, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();
}

internal sealed class StubHttpClientFactory : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new();
}

internal sealed class StubSettingProvider : ISettingProvider
{
    public Dictionary<string, string> Values { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Task<string?> GetValueAsync(string key, CancellationToken cancellationToken = default) =>
        Task.FromResult(Values.TryGetValue(key, out var v) ? v : null);

    public Task<Dictionary<string, string>> GetValuesAsync(
        IEnumerable<string> keys,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(keys
            .Where(Values.ContainsKey)
            .ToDictionary(k => k, k => Values[k], StringComparer.OrdinalIgnoreCase));

    public Task SetValueAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        Values[key] = value;
        return Task.CompletedTask;
    }
}

internal sealed class StubCategoryService : ICategoryService
{
    public RecommendCategoryResultDto Recommendation { get; set; } = new();

    /// <summary>Thrown instead of returning a recommendation, to test the 4Seller AI outage path.</summary>
    public Exception? RecommendError { get; set; }

    public int RecommendCallCount { get; private set; }

    public List<string> RecommendedTitles { get; } = [];

    /// <summary>Synced catalog rows keyed by external id, for path/leaf lookups.</summary>
    public Dictionary<string, CategoryDto> Catalog { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Task<RecommendCategoryResultDto> RecommendCategoryAsync(
        Guid actingUserId,
        RecommendCategoryRequest request,
        CancellationToken cancellationToken = default,
        bool allowOwnerOverride = false)
    {
        RecommendCallCount++;
        RecommendedTitles.Add(request.Title);
        return RecommendError is not null
            ? Task.FromException<RecommendCategoryResultDto>(RecommendError)
            : Task.FromResult(Recommendation);
    }

    public Task<CategorySyncResultDto> SyncFromFourSellerAsync(
        Guid preferredUserId,
        SyncCategoriesRequest request,
        CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    public Task<List<CategoryTreeNodeDto>> GetTreeAsync(
        string site,
        bool activeOnly = true,
        CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    public Task<List<CategoryTreeNodeDto>> GetChildrenAsync(
        string site,
        string? parentExternalId,
        bool activeOnly = true,
        CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    public Task<CategoryPathDto> GetPathAsync(
        string site,
        string externalId,
        bool activeOnly = true,
        CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    public Task<List<CategoryDto>> GetListAsync(
        string site,
        string? search = null,
        bool activeOnly = true,
        int? take = null,
        CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    public Task<CategoryDto?> GetByExternalIdAsync(
        string site,
        string externalId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Catalog.TryGetValue(externalId, out var category) ? category : null);
}

internal sealed class StubDescriptionAiService : IListingDescriptionAiService
{
    public RewriteListingDescriptionResultDto Result { get; set; } = new();
    public Func<Task>? OnRewrite { get; set; }
    public int CallCount { get; private set; }

    public async Task<RewriteListingDescriptionResultDto> RewriteAsync(
        RewriteListingDescriptionRequest request,
        CancellationToken cancellationToken = default)
    {
        CallCount++;
        if (OnRewrite is not null)
        {
            await OnRewrite();
        }

        cancellationToken.ThrowIfCancellationRequested();
        return Result;
    }

    public Task<ListingRewriteConfigDto> GetRewriteConfigAsync(CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();
}

internal sealed class StubSpecificsMapService : IListingSpecificsMapService
{
    public MapListingSpecificsResultDto Result { get; set; } = new();
    public int CallCount { get; private set; }
    public List<string?> MappedCategoryIds { get; } = [];

    public Task<MapListingSpecificsResultDto> MapAsync(
        Guid listingId,
        MapListingSpecificsRequest request,
        CancellationToken cancellationToken = default)
    {
        CallCount++;
        MappedCategoryIds.Add(request.CategoryId);
        return Task.FromResult(Result);
    }
}

/// <summary>
/// Records prep calls and applies just enough mutation for the validate gate to pass.
/// </summary>
internal sealed class StubAutoPublishPrepService : IListingAutoPublishPrepService
{
    public List<string> Calls { get; } = [];
    public string? LastSizeChartCaptureUrl { get; private set; }
    public string? LastPrepareImagesSizeChartUrl { get; private set; }
    public bool ShippingAlreadyComplete { get; set; }
    public bool WarehouseAlreadySet { get; set; }
    public bool SizeChartInGallery { get; set; } = true;
    public string? DefaultWarehouseId { get; set; } = "wh-1";

    public Task ApplyDefaultPricingAsync(Guid ownerId, ListingDetailDto detail, CancellationToken ct = default)
    {
        Calls.Add(nameof(ApplyDefaultPricingAsync));
        return Task.CompletedTask;
    }

    public void ApplyDefaultQuantity(ListingDetailDto detail, int quantity = 45) =>
        Calls.Add(nameof(ApplyDefaultQuantity));

    public void GenerateBatchSkus(ListingDetailDto detail)
    {
        Calls.Add(nameof(GenerateBatchSkus));
        EnsureUniqueSellerSkus(detail);
    }

    public void EnsureUniqueSellerSkus(ListingDetailDto detail) => Calls.Add(nameof(EnsureUniqueSellerSkus));

    public bool HasUniqueSellerSkus(ListingDetailDto detail) => true;

    public Task UploadSizeChartFromCaptureAsync(
        Guid listingId,
        ListingDetailDto detail,
        string captureUrl,
        CancellationToken ct = default)
    {
        Calls.Add(nameof(UploadSizeChartFromCaptureAsync));
        LastSizeChartCaptureUrl = captureUrl;
        SizeChartInGallery = true;
        return Task.CompletedTask;
    }

    public void PrepareImages(ListingDetailDto detail, string? sizeChartUrl)
    {
        Calls.Add(nameof(PrepareImages));
        LastPrepareImagesSizeChartUrl = sizeChartUrl;
    }

    public bool HasSizeChartInGallery(ListingDetailDto detail) => SizeChartInGallery;

    public bool NeedsShippingAutofill(ListingDetailDto detail) => !ShippingAlreadyComplete;

    public bool IsShippingComplete(ListingDetailDto detail) => true;

    public Task<bool> ApplyDefaultShippingAsync(
        Guid ownerId,
        ListingDetailDto detail,
        CancellationToken ct = default)
    {
        Calls.Add(nameof(ApplyDefaultShippingAsync));
        if (ShippingAlreadyComplete)
        {
            return Task.FromResult(false);
        }

        detail.PackageWeight = 1m;
        detail.PackageLength = 10m;
        detail.PackageWidth = 8m;
        detail.PackageHeight = 2m;
        return Task.FromResult(true);
    }

    public Task<bool> ApplyDefaultWarehouseAsync(
        Guid listingId,
        ListingDetailDto detail,
        CancellationToken ct = default)
    {
        Calls.Add(nameof(ApplyDefaultWarehouseAsync));
        if (WarehouseAlreadySet || !string.IsNullOrWhiteSpace(detail.WarehouseId))
        {
            return Task.FromResult(false);
        }

        detail.WarehouseId = DefaultWarehouseId;
        return Task.FromResult(true);
    }
}
