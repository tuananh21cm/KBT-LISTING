using System.Net.Http;
using Application.DTOs.Listing;
using Application.DTOs.PricingTemplate;
using Application.Interfaces;
using Domain.Exceptions;

namespace Application.Services;

/// <summary>Server-side prep steps for auto publish.</summary>
public sealed class ListingAutoPublishPrepService : IListingAutoPublishPrepService
{
    private const int DefaultGalleryMax = 9;

    private readonly IPricingTemplateService _pricingTemplates;
    private readonly IShippingTemplateService _shippingTemplates;
    private readonly IListingSyncService _listingSync;
    private readonly IHttpClientFactory _httpClientFactory;

    public ListingAutoPublishPrepService(
        IPricingTemplateService pricingTemplates,
        IShippingTemplateService shippingTemplates,
        IListingSyncService listingSync,
        IHttpClientFactory httpClientFactory)
    {
        _pricingTemplates = pricingTemplates;
        _shippingTemplates = shippingTemplates;
        _listingSync = listingSync;
        _httpClientFactory = httpClientFactory;
    }

    public async Task ApplyDefaultPricingAsync(
        Guid ownerId,
        ListingDetailDto detail,
        CancellationToken ct = default)
    {
        var templates = await _pricingTemplates.GetAllAsync(ownerId, ct);
        var template = templates.FirstOrDefault(t => t.IsDefault)
                       ?? templates.FirstOrDefault();
        if (template is null)
        {
            throw new BusinessException("Không có pricing template — tạo template trước khi auto publish.");
        }

        var formula = TemplateToApplied(template);
        detail.AppliedPricing = formula;

        foreach (var row in detail.Variations)
        {
            var cost = row.InitialPrice ?? row.SalePrice;
            var breakdown = CalculateSalePrice(cost, formula);
            if (breakdown.SalePrice is > 0)
            {
                row.SalePrice = breakdown.SalePrice;
            }
        }
    }

    public void ApplyDefaultQuantity(ListingDetailDto detail, int quantity = 45)
    {
        foreach (var row in detail.Variations)
        {
            row.Quantity = quantity;
        }
    }

    public void GenerateBatchSkus(ListingDetailDto detail) => EnsureUniqueSellerSkus(detail);

    public void EnsureUniqueSellerSkus(ListingDetailDto detail) =>
        ListingSellerSkuNormalizer.EnsureUniqueSellerSkus(detail);

    public bool HasUniqueSellerSkus(ListingDetailDto detail) =>
        ListingSellerSkuNormalizer.HasUniqueSellerSkus(detail.Variations);

    public async Task UploadSizeChartFromCaptureAsync(
        Guid listingId,
        ListingDetailDto detail,
        string captureUrl,
        CancellationToken ct = default)
    {
        var client = _httpClientFactory.CreateClient("ListingAutoPublish");
        client.Timeout = TimeSpan.FromSeconds(60);
        using var response = await client.GetAsync(captureUrl, ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new BusinessException($"Không tải được ảnh size chart ({response.StatusCode}).");
        }

        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
        var contentType = response.Content.Headers.ContentType?.MediaType ?? "image/png";
        var ext = contentType.Contains("png", StringComparison.OrdinalIgnoreCase) ? "png" : "jpg";

        await using var stream = new MemoryStream(bytes);
        var upload = await _listingSync.UploadCosMediaAsync(
            listingId,
            stream,
            $"size-chart-capture.{ext}",
            contentType,
            "SizeChart",
            ct);

        if (string.IsNullOrWhiteSpace(upload.Url))
        {
            throw new BusinessException("Upload size chart lên 4Seller không trả URL.");
        }

        detail.SizeChartId = upload.LocalMaterialId?.ToString();
        var images = detail.Images ?? [];
        images.RemoveAll(img =>
            string.Equals(img.MediaType, "SizeChart", StringComparison.OrdinalIgnoreCase));
        images.Add(new ListingImageDto
        {
            MediaType = "SizeChart",
            Url = upload.Url,
            SortOrder = images.Count
        });
        detail.Images = images;
    }

    public void PrepareImages(ListingDetailDto detail, string? sizeChartUrl)
    {
        var images = detail.Images ?? [];
        foreach (var img in images)
        {
            img.Url = UpgradeHttpUrl(img.Url);
        }

        images.RemoveAll(img => ListingImageUrlFilters.IsGifImageUrl(img.Url));
        detail.MainImage = ListingImageUrlFilters.StripGifFromPipeJoined(detail.MainImage);
        foreach (var variation in detail.Variations)
        {
            variation.MainImage = ListingImageUrlFilters.StripGifFromPipeJoined(variation.MainImage);
        }

        images = DedupeVariantColorImages(images);

        // Variant color images carry MediaType "Main" as well, so classify them first —
        // otherwise the same image lands in both buckets and gets pushed twice.
        var variantColors = images.Where(IsVariantColorImage).ToList();
        var products = images
            .Where(img => !IsVariantColorImage(img) && IsSharedProductImage(img))
            .ToList();
        var extras = images
            .Where(img => !IsVariantColorImage(img) && !IsSharedProductImage(img))
            .ToList();

        if (products.Count < DefaultGalleryMax && !string.IsNullOrWhiteSpace(sizeChartUrl))
        {
            var chartUrl = UpgradeHttpUrl(sizeChartUrl);
            if (!ListingImageUrlFilters.IsGifImageUrl(chartUrl)
                && !images.Any(i => string.Equals(i.Url, chartUrl, StringComparison.OrdinalIgnoreCase)))
            {
                products.Add(new ListingImageDto
                {
                    MediaType = "Detail",
                    Url = chartUrl,
                    SortOrder = products.Count
                });
            }
        }

        var ordered = products.Concat(variantColors).Concat(extras).ToList();
        for (var i = 0; i < ordered.Count; i++)
        {
            ordered[i].SortOrder = i;
        }

        detail.Images = ordered;
        detail.MainImage = products.FirstOrDefault(p => !string.IsNullOrWhiteSpace(p.Url))?.Url
                           ?? detail.MainImage;
        ListingImageUrlFilters.RemoveProductVideo(detail);
    }

    public bool HasSizeChartInGallery(ListingDetailDto detail)
    {
        return (detail.Images ?? []).Any(img =>
            string.Equals(img.MediaType, "SizeChart", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(img.Url));
    }

    public bool NeedsShippingAutofill(ListingDetailDto detail) => !IsShippingComplete(detail);

    public bool IsShippingComplete(ListingDetailDto detail) =>
        detail.PackageWeight is > 0
        && detail.PackageLength is > 0
        && detail.PackageWidth is > 0
        && detail.PackageHeight is > 0;

    public async Task<bool> ApplyDefaultShippingAsync(
        Guid ownerId,
        ListingDetailDto detail,
        CancellationToken ct = default)
    {
        if (!NeedsShippingAutofill(detail))
        {
            return false;
        }

        var templates = await _shippingTemplates.GetAllAsync(ownerId, ct);
        var template = templates.FirstOrDefault(t => t.IsDefault)
                       ?? templates.FirstOrDefault();
        if (template is null)
        {
            throw new BusinessException("Không có shipping template — tạo template trước khi auto publish.");
        }

        detail.PackageWeight = template.PackageWeight;
        detail.PackageLength = template.PackageLength;
        detail.PackageWidth = template.PackageWidth;
        detail.PackageHeight = template.PackageHeight;
        detail.WeightUnit = template.WeightUnit;
        detail.DimensionUnit = template.DimensionUnit;
        return true;
    }

    public async Task<bool> ApplyDefaultWarehouseAsync(
        Guid listingId,
        ListingDetailDto detail,
        CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(detail.WarehouseId))
        {
            return false;
        }

        if (detail.ShopId is null)
        {
            return false;
        }

        var meta = await _listingSync.GetShopMetadataAsync(listingId, detail.ShopId.Value, ct);
        if (string.IsNullOrWhiteSpace(meta.DefaultWarehouseId))
        {
            return false;
        }

        detail.WarehouseId = meta.DefaultWarehouseId;
        return true;
    }

    private static ListingAppliedPricingDto TemplateToApplied(PricingTemplateDto t) =>
        new()
        {
            TemplateId = t.Id,
            AppliedAt = DateTime.UtcNow,
            SupplierShipping = t.SupplierShipping,
            Tax = t.Tax,
            DiscountPercent = t.DiscountPercent,
            FastSalePercent = t.FastSalePercent,
            AdsFee = t.AdsFee,
            TikTokShipping = t.TikTokShipping,
            PlatformFeePercent = t.PlatformFeePercent,
            TargetMode = t.TargetMode,
            MarginPercent = t.MarginPercent,
            ProfitAmount = t.ProfitAmount
        };

    private static (decimal? SalePrice, string? Error) CalculateSalePrice(
        decimal? cost,
        ListingAppliedPricingDto input)
    {
        if (cost is null or <= 0)
        {
            return (null, "Thiếu costPrice");
        }

        var totalImport = cost.Value + input.SupplierShipping + input.Tax;
        var promoPercent = Math.Max(input.DiscountPercent, input.FastSalePercent);
        var promoRate = promoPercent / 100m;
        var platformRate = input.PlatformFeePercent / 100m;
        if (promoRate >= 1 || platformRate >= 1)
        {
            return (null, "Promo / phí sàn không hợp lệ");
        }

        var targetProfit = string.Equals(input.TargetMode, "profit", StringComparison.OrdinalIgnoreCase)
            ? input.ProfitAmount
            : (input.MarginPercent / 100m) * totalImport;
        var numerator = totalImport + input.AdsFee - input.TikTokShipping + targetProfit;
        var denominator = (1 - promoRate) * (1 - platformRate);
        if (denominator <= 0)
        {
            return (null, "Công thức không hợp lệ");
        }

        var sale = Math.Round(numerator / denominator, 2, MidpointRounding.AwayFromZero);
        return sale > 0 ? (sale, null) : (null, "Giá bán không hợp lệ");
    }

    private static string UpgradeHttpUrl(string? url)
    {
        var u = (url ?? "").Trim();
        if (u.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            return "https://" + u[7..];
        }

        return u;
    }

    private static bool IsSharedProductImage(ListingImageDto img) =>
        string.Equals(img.MediaType, "Main", StringComparison.OrdinalIgnoreCase)
        || string.Equals(img.MediaType, "Detail", StringComparison.OrdinalIgnoreCase);

    private static bool IsVariantColorImage(ListingImageDto img) =>
        img.ListingSkuId.HasValue
        || !string.IsNullOrWhiteSpace(img.ColorValue);

    private static List<ListingImageDto> DedupeVariantColorImages(List<ListingImageDto> list)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<ListingImageDto>();
        foreach (var img in list)
        {
            var key = img.Url.Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(key) || seen.Contains(key))
            {
                continue;
            }

            seen.Add(key);
            result.Add(img);
        }

        return result;
    }
}
