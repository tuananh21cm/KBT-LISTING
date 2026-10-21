using Application.DTOs.FourSeller;
using Application.DTOs.Listing;
using Application.DTOs.ShippingTemplate;
using Application.Interfaces;
using Application.Services;
using Domain.Exceptions;
using Xunit;

namespace Application.Tests;

public class ListingAutoPublishPrepServiceShippingTests
{
    private static readonly Guid OwnerId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ListingId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid ShopId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    [Fact]
    public async Task ApplyDefaultShipping_FillsMissingDimsFromDefaultTemplate()
    {
        var templateId = Guid.NewGuid();
        var service = CreateService(
            templates:
            [
                new ShippingTemplateDto
                {
                    Id = templateId,
                    Name = "Pajamas",
                    PackageWeight = 1m,
                    PackageLength = 12m,
                    PackageWidth = 10m,
                    PackageHeight = 3m,
                    WeightUnit = "lb",
                    DimensionUnit = "inch",
                    IsDefault = true
                }
            ]);

        var detail = new ListingDetailDto { OwnerId = OwnerId };

        var applied = await service.ApplyDefaultShippingAsync(OwnerId, detail);

        Assert.True(applied);
        Assert.Equal(1m, detail.PackageWeight);
        Assert.Equal(12m, detail.PackageLength);
        Assert.Equal(10m, detail.PackageWidth);
        Assert.Equal(3m, detail.PackageHeight);
        Assert.Equal("lb", detail.WeightUnit);
        Assert.Equal("inch", detail.DimensionUnit);
        Assert.True(service.IsShippingComplete(detail));
    }

    [Fact]
    public async Task ApplyDefaultShipping_DoesNotOverwriteCompleteDims()
    {
        var service = CreateService(
            templates:
            [
                new ShippingTemplateDto
                {
                    Id = Guid.NewGuid(),
                    Name = "Pajamas",
                    PackageWeight = 1m,
                    PackageLength = 12m,
                    PackageWidth = 10m,
                    PackageHeight = 3m,
                    IsDefault = true
                }
            ]);

        var detail = new ListingDetailDto
        {
            OwnerId = OwnerId,
            PackageWeight = 2m,
            PackageLength = 8m,
            PackageWidth = 6m,
            PackageHeight = 4m
        };

        var applied = await service.ApplyDefaultShippingAsync(OwnerId, detail);

        Assert.False(applied);
        Assert.Equal(2m, detail.PackageWeight);
        Assert.Equal(8m, detail.PackageLength);
    }

    [Fact]
    public async Task ApplyDefaultShipping_ThrowsWhenNoTemplate()
    {
        var service = CreateService(templates: []);
        var detail = new ListingDetailDto { OwnerId = OwnerId };

        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            service.ApplyDefaultShippingAsync(OwnerId, detail));

        Assert.Contains("shipping template", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ApplyDefaultWarehouse_AssignsDefaultFromShopMetadata()
    {
        const string warehouseId = "wh-123";
        var service = CreateService(
            templates: [],
            shopMetadata: new ListingShopMetadataDto
            {
                ShopId = ShopId,
                DefaultWarehouseId = warehouseId
            });

        var detail = new ListingDetailDto
        {
            OwnerId = OwnerId,
            ShopId = ShopId
        };

        var applied = await service.ApplyDefaultWarehouseAsync(ListingId, detail);

        Assert.True(applied);
        Assert.Equal(warehouseId, detail.WarehouseId);
    }

    [Fact]
    public async Task ApplyDefaultWarehouse_SkipsWhenWarehouseAlreadySet()
    {
        var service = CreateService(
            templates: [],
            shopMetadata: new ListingShopMetadataDto
            {
                ShopId = ShopId,
                DefaultWarehouseId = "wh-new"
            });

        var detail = new ListingDetailDto
        {
            OwnerId = OwnerId,
            ShopId = ShopId,
            WarehouseId = "wh-existing"
        };

        var applied = await service.ApplyDefaultWarehouseAsync(ListingId, detail);

        Assert.False(applied);
        Assert.Equal("wh-existing", detail.WarehouseId);
    }

    [Fact]
    public async Task ApplyDefaults_ShippingAndWarehouseReportChangesIndependently()
    {
        // The validate step derives each sub-step's "skipped" flag from its own return value —
        // a shipping autofill must not make an untouched warehouse look changed.
        var service = CreateService(
            templates:
            [
                new ShippingTemplateDto
                {
                    Id = Guid.NewGuid(),
                    Name = "Pajamas",
                    PackageWeight = 1m,
                    PackageLength = 12m,
                    PackageWidth = 10m,
                    PackageHeight = 3m,
                    IsDefault = true
                }
            ],
            shopMetadata: new ListingShopMetadataDto
            {
                ShopId = ShopId,
                DefaultWarehouseId = "wh-new"
            });

        var detail = new ListingDetailDto
        {
            OwnerId = OwnerId,
            ShopId = ShopId,
            WarehouseId = "wh-existing"
        };

        var shippingChanged = await service.ApplyDefaultShippingAsync(OwnerId, detail);
        var warehouseChanged = await service.ApplyDefaultWarehouseAsync(ListingId, detail);

        Assert.True(shippingChanged);
        Assert.False(warehouseChanged);
        Assert.Equal("wh-existing", detail.WarehouseId);
    }

    [Fact]
    public void NeedsShippingAutofill_TrueWhenAnyDimMissing()
    {
        var service = CreateService(templates: []);

        Assert.True(service.NeedsShippingAutofill(new ListingDetailDto()));
        Assert.True(service.NeedsShippingAutofill(new ListingDetailDto
        {
            PackageWeight = 1m,
            PackageLength = 12m,
            PackageWidth = 10m
        }));
        Assert.False(service.NeedsShippingAutofill(new ListingDetailDto
        {
            PackageWeight = 1m,
            PackageLength = 12m,
            PackageWidth = 10m,
            PackageHeight = 3m
        }));
    }

    private static ListingAutoPublishPrepService CreateService(
        IReadOnlyList<ShippingTemplateDto> templates,
        ListingShopMetadataDto? shopMetadata = null)
    {
        return new ListingAutoPublishPrepService(
            new FakePricingTemplateService(),
            new FakeShippingTemplateService(templates),
            new FakeListingSyncService(shopMetadata),
            new FakeHttpClientFactory());
    }

    private sealed class FakePricingTemplateService : IPricingTemplateService
    {
        public Task<List<Application.DTOs.PricingTemplate.PricingTemplateDto>> GetAllAsync(
            Guid userId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new List<Application.DTOs.PricingTemplate.PricingTemplateDto>());

        public Task<Application.DTOs.PricingTemplate.PricingTemplateDto> GetByIdAsync(
            Guid userId,
            Guid id,
            CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<Application.DTOs.PricingTemplate.PricingTemplateDto> CreateAsync(
            Guid userId,
            Application.DTOs.PricingTemplate.CreatePricingTemplateDto dto,
            CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<Application.DTOs.PricingTemplate.PricingTemplateDto> UpdateAsync(
            Guid userId,
            Guid id,
            Application.DTOs.PricingTemplate.UpdatePricingTemplateDto dto,
            CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task DeleteAsync(Guid userId, Guid id, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
    }

    private sealed class FakeShippingTemplateService(IReadOnlyList<ShippingTemplateDto> templates)
        : IShippingTemplateService
    {
        public Task<List<ShippingTemplateDto>> GetAllAsync(
            Guid userId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(templates.ToList());

        public Task<ShippingTemplateDto> GetByIdAsync(
            Guid userId,
            Guid id,
            CancellationToken cancellationToken = default) =>
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

    private sealed class FakeListingSyncService(ListingShopMetadataDto? shopMetadata) : IListingSyncService
    {
        public Task<ListingShopMetadataDto> GetShopMetadataAsync(
            Guid listingId,
            Guid shopId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(shopMetadata ?? new ListingShopMetadataDto { ShopId = shopId });

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

        public Task<ListingDetailDto> GetByIdAsync(
            Guid id,
            bool refreshFromFourSeller = false,
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

        public Task<ListingDetailDto> UpdateAsync(
            Guid id,
            UpdateListingMirrorDto dto,
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

        public Task<ListingPushResultDto> PublishAsync(
            Guid id,
            UpdateListingMirrorDto? dto = null,
            string? publishSource = null,
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

        public Task<ListingPushResultDto> DeactivateAsync(
            Guid id,
            CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<ListingDeactivateResultDto> DeactivateByIdsAsync(
            IReadOnlyList<Guid> ids,
            CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<ListingPushResultDto> ActivateAsync(
            Guid id,
            CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<ListingDetailDto> DuplicateAsync(Guid id, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<ListingDetailDto> SyncOneAsync(Guid id, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<FourSellerMoveSourceListingDto> GetMoveSourceAsync(
            Guid id,
            CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<IReadOnlyList<FourSellerRecentUseCategoryDto>> GetRecentCategoriesAsync(
            Guid id,
            long? shopId = null,
            string? site = null,
            CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task BindCategoryAsync(
            Guid id,
            BindListingCategoryRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<IReadOnlyList<FourSellerBrandDto>> SuggestBrandsAsync(
            Guid listingId,
            Guid shopId,
            string brandName,
            CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<FourSellerCosUploadResultDto> UploadCosMediaAsync(
            Guid listingId,
            Stream content,
            string fileName,
            string contentType,
            string? referenceType = null,
            CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task DeleteByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task DeleteOwnedDraftAsync(
            Guid ownerId,
            Guid id,
            CancellationToken cancellationToken = default) =>
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

    private sealed class FakeHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }
}
