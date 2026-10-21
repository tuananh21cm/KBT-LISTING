using System.Text.Json;
using Application.DTOs.FourSeller;
using Application.Interfaces;
using Domain.Exceptions;
using Domain.Interfaces;
using Microsoft.EntityFrameworkCore;
using Application.Options;
using Microsoft.Extensions.Options;
using Shared.Constants;
using Shared.Helpers;

namespace Application.Services;

/// <summary>
/// Generates size-chart images via Grok and uploads to 4Seller COS.
/// </summary>
public class SizeChartImageService : ISizeChartImageService
{
    private readonly IApplicationDbContext _db;
    private readonly ISettingProvider _settings;
    private readonly IXaiImageService _xai;
    private readonly IListingSyncService _listingSync;
    private readonly XaiSizeChartOptions _xaiOptions;

    /// <summary>Initializes the size-chart orchestrator.</summary>
    public SizeChartImageService(
        IApplicationDbContext db,
        ISettingProvider settings,
        IXaiImageService xai,
        IListingSyncService listingSync,
        IOptions<XaiSizeChartOptions> xaiOptions)
    {
        _db = db;
        _settings = settings;
        _xai = xai;
        _listingSync = listingSync;
        _xaiOptions = xaiOptions.Value;
    }

    /// <inheritdoc />
    public async Task<(FourSellerCosUploadResultDto Result, string? Warning)> GenerateAndUploadAsync(
        Guid listingId,
        CancellationToken cancellationToken = default)
    {
        var enabled = await _settings.GetValueAsync(
            ListingSizeChartSettingKeys.AutoGenerateEnabled,
            cancellationToken);
        if (!ParseEnabled(enabled))
        {
            throw new BusinessException(
                "Tạo Size Chart tự động đang tắt trong Settings. Bật «Tạo ảnh Size Chart tự động» trước.");
        }

        var listing = await _db.Listings
            .AsNoTracking()
            .Where(l => l.Id == listingId)
            .Select(l => new
            {
                l.Id,
                l.ProductName,
                l.MainImage,
                l.RawPayload,
                l.SourceUrl,
                SizeGuideJson = l.SupplierProductInfo != null
                    ? l.SupplierProductInfo.SizeGuideJson
                    : null
            })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy listing.");

        var sizeGuideJson = listing.SizeGuideJson;
        if (string.IsNullOrWhiteSpace(sizeGuideJson))
        {
            var sourceKey = SourceUrlKeyHelper.Normalize(listing.SourceUrl);
            if (!string.IsNullOrWhiteSpace(sourceKey))
            {
                sizeGuideJson = await _db.SupplierProductInfos
                    .AsNoTracking()
                    .Where(s => s.SourceUrlKey == sourceKey && s.SizeGuideJson != null)
                    .Select(s => s.SizeGuideJson)
                    .FirstOrDefaultAsync(cancellationToken);
            }
        }

        if (string.IsNullOrWhiteSpace(sizeGuideJson))
        {
            throw new BusinessException(
                "Listing chưa có SizeGuideJson. Thêm sản phẩm qua extension SHEIN để lấy bảng size.");
        }

        JsonElement sizeGuideEl;
        try
        {
            sizeGuideEl = JsonSerializer.Deserialize<JsonElement>(sizeGuideJson);
        }
        catch (JsonException)
        {
            throw new BusinessException("SizeGuideJson không hợp lệ.");
        }

        if (!sizeGuideEl.TryGetProperty("chartRows", out var chartRows) ||
            chartRows.ValueKind != JsonValueKind.Array ||
            chartRows.GetArrayLength() == 0)
        {
            if (!sizeGuideEl.TryGetProperty("rows", out var rows) ||
                rows.ValueKind != JsonValueKind.Array ||
                rows.GetArrayLength() == 0)
            {
                throw new BusinessException("SizeGuideJson không có dòng size.");
            }
        }

        if (string.IsNullOrWhiteSpace(_xaiOptions.SizeChartPrompt))
        {
            throw new BusinessException("Xai:SizeChartPrompt chưa được cấu hình.");
        }

        var prompt = SizeChartPromptBuilder.Build(
            _xaiOptions.SizeChartPrompt,
            sizeGuideEl,
            listing.ProductName);

        var productImageUrl = ResolveProductImageUrl(listing.MainImage, listing.RawPayload);
        var (imageBytes, usedFallback) = await _xai.GenerateSizeChartAsync(
            prompt,
            productImageUrl,
            cancellationToken);

        string? warning = usedFallback
            ? string.IsNullOrWhiteSpace(productImageUrl)
                ? "Listing chưa có ảnh sản phẩm — đã tạo size chart text-only (không dùng ảnh reference)."
                : "Không dùng được ảnh reference — đã fallback sang tạo text-only."
            : null;

        var (contentType, extension) = DetectImageFormat(imageBytes);
        var fileName = $"size-chart-{listingId:N}.{extension}";
        await using var stream = new MemoryStream(imageBytes);
        var upload = await _listingSync.UploadCosMediaAsync(
            listingId,
            stream,
            fileName,
            contentType,
            referenceType: null,
            cancellationToken);

        return (upload, warning);
    }

    private static bool ParseEnabled(string? value) =>
        string.Equals(value?.Trim(), "true", StringComparison.OrdinalIgnoreCase) ||
        value?.Trim() == "1";

    private static string? ResolveProductImageUrl(string? mainImage, string? rawPayload)
    {
        var fromMain = PickFirstImageUrl(mainImage);
        if (!string.IsNullOrWhiteSpace(fromMain))
        {
            return fromMain;
        }

        if (string.IsNullOrWhiteSpace(rawPayload))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(rawPayload);
            var root = doc.RootElement;

            if (root.TryGetProperty("mainImage", out var mainEl) &&
                mainEl.ValueKind == JsonValueKind.String)
            {
                var fromPayloadMain = PickFirstImageUrl(mainEl.GetString());
                if (!string.IsNullOrWhiteSpace(fromPayloadMain))
                {
                    return fromPayloadMain;
                }
            }

            foreach (var key in new[] { "imageList", "images", "photoList", "photos", "detailImages" })
            {
                if (!root.TryGetProperty(key, out var node))
                {
                    continue;
                }

                var fromList = PickFirstImageFromJsonNode(node);
                if (!string.IsNullOrWhiteSpace(fromList))
                {
                    return fromList;
                }
            }

            if (root.TryGetProperty("skuImgs", out var skuImgs))
            {
                var fromSku = PickFirstImageFromSkuImgs(skuImgs);
                if (!string.IsNullOrWhiteSpace(fromSku))
                {
                    return fromSku;
                }
            }
        }
        catch (JsonException)
        {
            // ignore — fallback to null
        }

        return null;
    }

    private static string? PickFirstImageUrl(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        foreach (var part in raw.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var url = NormalizeImageUrl(part);
            if (!string.IsNullOrWhiteSpace(url))
            {
                return url;
            }
        }

        return null;
    }

    private static string? PickFirstImageFromJsonNode(JsonElement node)
    {
        if (node.ValueKind == JsonValueKind.String)
        {
            return PickFirstImageUrl(node.GetString());
        }

        if (node.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var item in node.EnumerateArray())
        {
            switch (item.ValueKind)
            {
                case JsonValueKind.String:
                {
                    var url = PickFirstImageUrl(item.GetString());
                    if (!string.IsNullOrWhiteSpace(url))
                    {
                        return url;
                    }

                    break;
                }
                case JsonValueKind.Object:
                {
                    foreach (var prop in new[] { "url", "src", "imageUrl", "img_url", "imgUrl" })
                    {
                        if (item.TryGetProperty(prop, out var urlEl) &&
                            urlEl.ValueKind == JsonValueKind.String)
                        {
                            var url = PickFirstImageUrl(urlEl.GetString());
                            if (!string.IsNullOrWhiteSpace(url))
                            {
                                return url;
                            }
                        }
                    }

                    break;
                }
            }
        }

        return null;
    }

    private static string? PickFirstImageFromSkuImgs(JsonElement skuImgs)
    {
        if (skuImgs.ValueKind == JsonValueKind.Object)
        {
            foreach (var color in skuImgs.EnumerateObject())
            {
                var url = PickFirstImageFromJsonNode(color.Value);
                if (!string.IsNullOrWhiteSpace(url))
                {
                    return url;
                }
            }

            return null;
        }

        return PickFirstImageFromJsonNode(skuImgs);
    }

    private static string? NormalizeImageUrl(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var url = raw.Trim();
        if (url.StartsWith("//", StringComparison.Ordinal))
        {
            url = "https:" + url;
        }

        url = UpgradeHttpToHttps(url);
        if (url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            url.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            return url;
        }

        return null;
    }

    private static string UpgradeHttpToHttps(string url)
    {
        if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            return "https://" + url["http://".Length..];
        }

        return url;
    }

    private static (string ContentType, string Extension) DetectImageFormat(byte[] bytes)
    {
        if (bytes.Length >= 3 &&
            bytes[0] == 0xFF &&
            bytes[1] == 0xD8 &&
            bytes[2] == 0xFF)
        {
            return ("image/jpeg", "jpg");
        }

        return ("image/png", "png");
    }
}
