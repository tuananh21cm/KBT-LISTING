using Application.DTOs.Supplier;
using Application.Interfaces;
using Domain.Entities;
using Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Shared.Constants;
using Shared.Helpers;
using System.Text.Json;

namespace Application.Services;

/// <inheritdoc />
public class SupplierProductInfoService : ISupplierProductInfoService
{
    private const int MaxSizeGuideJsonBytes = 32_768;
    private readonly IApplicationDbContext _db;
    private readonly ISettingProvider _settingProvider;

    /// <summary>Creates the service.</summary>
    public SupplierProductInfoService(IApplicationDbContext db, ISettingProvider settingProvider)
    {
        _db = db;
        _settingProvider = settingProvider;
    }

    /// <inheritdoc />
    public async Task UpsertFromUrlAsync(
        SupplierProductInfoUpsertRequest request,
        CancellationToken cancellationToken = default)
    {
        var reviewCount = SupplierPdpMetrics.NormalizeReviewCount(request.ReviewCount);
        var averageRating = SupplierPdpMetrics.NormalizeRating(request.AverageRating);
        var stock = SupplierPdpMetrics.NormalizeStock(request.Stock);
        var shippingDays = SupplierPdpMetrics.NormalizeShippingDays(request.ShippingDays)
            ?? SupplierPdpMetrics.ParseShippingDaysFromDesc(request.ShippingDayPercentDesc);
        var shippingPercent = request.ShippingDayPercent is >= 0 and <= 100
            ? Math.Round(request.ShippingDayPercent.Value, 2, MidpointRounding.AwayFromZero)
            : SupplierPdpMetrics.ParseShippingDayPercent(request.ShippingDayPercentDesc);
        var storeCode = SupplierPdpMetrics.Truncate(request.StoreCode, 64);
        var sizeGuideJson = NormalizeSizeGuideJson(request.SizeGuideJson);
        var hasSellerIdentity = !string.IsNullOrWhiteSpace(storeCode);
        var hasProductFacts = reviewCount is not null
            || averageRating is not null
            || stock is not null
            || request.IsLocalProduct is not null
            || request.IsSupportQuickShip is not null
            || sizeGuideJson is not null;

        if (!hasSellerIdentity && !hasProductFacts)
        {
            return;
        }

        var key = SourceUrlKeyHelper.Normalize(request.SourceUrl);
        if (string.IsNullOrWhiteSpace(key) && !hasSellerIdentity)
        {
            return;
        }

        var now = DateTime.UtcNow;
        var platform = SupplierPdpMetrics.NormalizePlatform(request.Platform);
        if (platform is null && !string.IsNullOrWhiteSpace(request.SourceUrl))
        {
            var sources = await _db.ProductSources.AsNoTracking()
                .Select(p => new { p.Code, p.Domains })
                .ToListAsync(cancellationToken);
            platform = SupplierPdpMetrics.MatchPlatformFromDomains(
                request.SourceUrl,
                sources.Select(p => (p.Code, p.Domains)));
            platform ??= SupplierPdpMetrics.InferPlatformFromUrl(request.SourceUrl);
        }

        Guid? sellerId = null;
        if (hasSellerIdentity && !string.IsNullOrWhiteSpace(platform))
        {
            var seller = await _db.SupplierSellerInfos
                .FirstOrDefaultAsync(
                    s => s.Platform == platform && s.StoreCode == storeCode,
                    cancellationToken);

            var storeUrl = SupplierPdpMetrics.BuildStoreUrl(platform, storeCode, request.StoreUrl);
            var logo = SupplierPdpMetrics.NormalizeAbsoluteUrl(request.StoreLogo);
            var soldBy = SupplierPdpMetrics.Truncate(request.SoldByTips, 500);
            var title = SupplierPdpMetrics.ResolveStoreTitle(request.StoreTitle, soldBy);
            var shipsFrom = SupplierPdpMetrics.Truncate(request.ShipsFromTips, 500);
            var shopRank = SupplierPdpMetrics.NormalizeShopRank(request.ShopRank);
            var storeAttention = SupplierPdpMetrics.NormalizeStoreAttention(request.StoreAttention);
            var percentDesc = SupplierPdpMetrics.Truncate(request.ShippingDayPercentDesc, 4000);

            if (seller is null)
            {
                seller = new SupplierSellerInfo
                {
                    Id = Guid.NewGuid(),
                    Platform = platform,
                    StoreCode = storeCode!,
                    StoreUrl = storeUrl,
                    StoreLogo = logo,
                    StoreTitle = title,
                    SoldByTips = soldBy,
                    ShipsFromTips = shipsFrom,
                    ShopRank = shopRank,
                    StoreAttention = storeAttention,
                    ShippingDays = shippingDays,
                    ShippingDayPercent = shippingPercent,
                    ShippingDayPercentDesc = percentDesc,
                    CreatedAt = now,
                    UpdatedAt = now
                };
                _db.SupplierSellerInfos.Add(seller);
            }
            else
            {
                if (storeUrl is not null) seller.StoreUrl = storeUrl;
                if (logo is not null) seller.StoreLogo = logo;
                if (title is not null) seller.StoreTitle = title;
                else if (string.IsNullOrWhiteSpace(seller.StoreTitle) && soldBy is not null)
                {
                    seller.StoreTitle = SupplierPdpMetrics.ParseStoreTitleFromSoldByTips(soldBy);
                }
                if (soldBy is not null) seller.SoldByTips = soldBy;
                if (shipsFrom is not null) seller.ShipsFromTips = shipsFrom;
                if (shopRank is not null) seller.ShopRank = shopRank;
                if (storeAttention is not null) seller.StoreAttention = storeAttention;
                if (shippingDays is not null) seller.ShippingDays = shippingDays;
                if (shippingPercent is not null) seller.ShippingDayPercent = shippingPercent;
                if (percentDesc is not null) seller.ShippingDayPercentDesc = percentDesc;
                seller.UpdatedAt = now;
            }

            sellerId = seller.Id;
        }

        if (string.IsNullOrWhiteSpace(key))
        {
            await _db.SaveChangesAsync(cancellationToken);
            return;
        }

        var info = await _db.SupplierProductInfos
            .FirstOrDefaultAsync(p => p.SourceUrlKey == key, cancellationToken);

        if (info is null)
        {
            info = new SupplierProductInfo
            {
                Id = Guid.NewGuid(),
                SourceUrlKey = key,
                SourceUrl = SupplierPdpMetrics.Truncate(request.SourceUrl, 2000),
                SupplierSellerInfoId = sellerId,
                Stock = stock,
                ReviewCount = reviewCount,
                AverageRating = averageRating,
                IsLocalProduct = request.IsLocalProduct,
                IsSupportQuickShip = request.IsSupportQuickShip,
                SizeGuideJson = sizeGuideJson,
                CreatedAt = now,
                UpdatedAt = now
            };
            _db.SupplierProductInfos.Add(info);
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(request.SourceUrl))
            {
                info.SourceUrl = SupplierPdpMetrics.Truncate(request.SourceUrl, 2000);
            }

            if (sellerId is not null)
            {
                info.SupplierSellerInfoId = sellerId;
            }

            if (stock is not null) info.Stock = stock;
            if (reviewCount is not null) info.ReviewCount = reviewCount;
            if (averageRating is not null) info.AverageRating = averageRating;
            if (request.IsLocalProduct is not null) info.IsLocalProduct = request.IsLocalProduct;
            if (request.IsSupportQuickShip is not null) info.IsSupportQuickShip = request.IsSupportQuickShip;
            if (sizeGuideJson is not null) info.SizeGuideJson = sizeGuideJson;
            info.UpdatedAt = now;
        }

        var productId = SourceUrlKeyHelper.ExtractProductId(request.SourceUrl);
        List<Listing> candidates;
        if (!string.IsNullOrWhiteSpace(productId))
        {
            var needle = $"-p-{productId}";
            candidates = await _db.Listings
                .Where(l => l.SourceUrl != null && l.SourceUrl != "" && l.SourceUrl.Contains(needle))
                .ToListAsync(cancellationToken);
        }
        else if (!string.IsNullOrWhiteSpace(request.SourceUrl))
        {
            var exact = request.SourceUrl.Trim();
            candidates = await _db.Listings
                .Where(l => l.SourceUrl == exact)
                .ToListAsync(cancellationToken);
        }
        else
        {
            candidates = [];
        }

        foreach (var listing in candidates)
        {
            if (!string.Equals(SourceUrlKeyHelper.Normalize(listing.SourceUrl), key, StringComparison.Ordinal))
            {
                continue;
            }

            ApplyInfo(listing, info);
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task SetSizeChartImageUrlAsync(
        SupplierSizeChartImageRequest request,
        CancellationToken cancellationToken = default)
    {
        var publicUrl = SupplierPdpMetrics.NormalizeAbsoluteUrl(request.PublicUrl);
        if (string.IsNullOrWhiteSpace(publicUrl))
        {
            throw new BusinessException("PublicUrl không hợp lệ.");
        }

        await EnsureBunnyPublicUrlAsync(publicUrl, cancellationToken);

        var key = SourceUrlKeyHelper.Normalize(request.SourceUrl);
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new BusinessException("Không xác định được sản phẩm nguồn từ URL.");
        }

        var info = await _db.SupplierProductInfos
            .FirstOrDefaultAsync(p => p.SourceUrlKey == key, cancellationToken);

        if (info is null)
        {
            throw new BusinessException(
                "Chưa có snapshot sản phẩm. Hãy thêm sản phẩm qua extension trước khi upload Size Chart.");
        }

        info.SizeChartImageUrl = publicUrl;
        info.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<SupplierSizeChartImageStatusDto> GetSizeChartImageStatusAsync(
        string sourceUrl,
        CancellationToken cancellationToken = default)
    {
        var key = SourceUrlKeyHelper.Normalize(sourceUrl);
        if (string.IsNullOrWhiteSpace(key))
        {
            return new SupplierSizeChartImageStatusDto();
        }

        var url = await _db.SupplierProductInfos
            .AsNoTracking()
            .Where(s => s.SourceUrlKey == key && s.SizeChartImageUrl != null)
            .Select(s => s.SizeChartImageUrl)
            .FirstOrDefaultAsync(cancellationToken);

        var trimmed = url?.Trim();
        return new SupplierSizeChartImageStatusDto
        {
            HasImage = !string.IsNullOrWhiteSpace(trimmed),
            PublicUrl = trimmed,
        };
    }

    /// <inheritdoc />
    public async Task ApplyToListingAsync(Listing listing, CancellationToken cancellationToken = default)
    {
        await ApplyToListingsAsync([listing], cancellationToken);
    }

    /// <inheritdoc />
    public async Task ApplyToListingsAsync(
        IReadOnlyCollection<Listing> listings,
        CancellationToken cancellationToken = default)
    {
        if (listings.Count == 0)
        {
            return;
        }

        var keyed = new List<(Listing Listing, string Key)>();
        foreach (var listing in listings)
        {
            var key = SourceUrlKeyHelper.Normalize(listing.SourceUrl);
            if (!string.IsNullOrWhiteSpace(key))
            {
                keyed.Add((listing, key));
            }
        }

        if (keyed.Count == 0)
        {
            return;
        }

        var keys = keyed.Select(k => k.Key).Distinct().ToList();
        var infos = await _db.SupplierProductInfos.AsNoTracking()
            .Where(s => keys.Contains(s.SourceUrlKey))
            .ToDictionaryAsync(s => s.SourceUrlKey, cancellationToken);

        foreach (var (listing, key) in keyed)
        {
            if (infos.TryGetValue(key, out var info))
            {
                ApplyInfo(listing, info);
            }
        }
    }

    internal static void ApplyInfo(Listing listing, SupplierProductInfo info)
    {
        listing.SupplierProductInfoId = info.Id;

        if (info.ReviewCount is not null)
        {
            listing.ReviewCount = info.ReviewCount;
        }

        if (info.AverageRating is not null)
        {
            listing.AverageRating = info.AverageRating;
        }
    }

    private static string? NormalizeSizeGuideJson(JsonElement? element)
    {
        if (element is null)
        {
            return null;
        }

        var value = element.Value;
        if (value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var raw = value.GetRawText();
        if (string.IsNullOrWhiteSpace(raw) || raw.Length > MaxSizeGuideJsonBytes)
        {
            return null;
        }

        return raw;
    }

    private async Task EnsureBunnyPublicUrlAsync(string publicUrl, CancellationToken cancellationToken)
    {
        var settings = await _settingProvider.GetValuesAsync(
            [BunnySettingKeys.CdnHostname],
            cancellationToken);

        settings.TryGetValue(BunnySettingKeys.CdnHostname, out var cdnHostname);
        if (string.IsNullOrWhiteSpace(cdnHostname))
        {
            throw new BusinessException(
                "Chưa cấu hình bunny.cdn_hostname. Vui lòng thiết lập trong Cấu hình hệ thống.");
        }

        if (!Uri.TryCreate(publicUrl, UriKind.Absolute, out var url))
        {
            throw new BusinessException("PublicUrl không hợp lệ.");
        }

        var host = url.Host;
        var allowedHosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in cdnHostname.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var trimmed = part.Trim().TrimEnd('/');
            if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                if (Uri.TryCreate(trimmed, UriKind.Absolute, out var cdnUri))
                {
                    allowedHosts.Add(cdnUri.Host);
                }
            }
            else
            {
                allowedHosts.Add(trimmed.TrimStart('/'));
            }
        }

        if (allowedHosts.Count == 0 || !allowedHosts.Contains(host))
        {
            throw new BusinessException("PublicUrl phải thuộc Bunny CDN đã cấu hình (bunny.cdn_hostname).");
        }
    }
}
