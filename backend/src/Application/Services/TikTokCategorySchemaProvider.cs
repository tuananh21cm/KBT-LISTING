using Application.DTOs.FourSeller;
using Application.Interfaces;
using Microsoft.Extensions.Caching.Memory;

namespace Application.Services;

/// <inheritdoc />
public class TikTokCategorySchemaProvider : ITikTokCategorySchemaProvider
{
    /// <summary>
    /// 4Seller refreshes these rows roughly daily, so a short window is enough to collapse the several
    /// reads one publish makes while still picking up a schema change on the next run.
    /// </summary>
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(15);

    private readonly IFourSellerApiService _fourSeller;
    private readonly IMemoryCache _cache;

    /// <summary>Creates the provider.</summary>
    public TikTokCategorySchemaProvider(IFourSellerApiService fourSeller, IMemoryCache cache)
    {
        _fourSeller = fourSeller;
        _cache = cache;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<FourSellerCategoryAttributeDto>> GetAsync(
        Guid ownerId,
        string categoryId,
        long fourSellerShopId,
        string site,
        CancellationToken cancellationToken = default)
    {
        var key = BuildKey(categoryId, site);
        if (_cache.TryGetValue(key, out IReadOnlyList<FourSellerCategoryAttributeDto>? cached)
            && cached is { Count: > 0 })
        {
            return cached;
        }

        var schema = await _fourSeller.GetCategoryAttributeListByIdAsync(
            ownerId,
            categoryId.Trim(),
            fourSellerShopId,
            site,
            cancellationToken);

        // An empty answer is never cached: it means the shop context or the category was wrong, and
        // remembering it would starve every later listing of the same category.
        if (schema.Count > 0)
        {
            _cache.Set(key, schema, CacheTtl);
        }

        return schema;
    }

    /// <inheritdoc />
    public void Invalidate(string categoryId, string site) => _cache.Remove(BuildKey(categoryId, site));

    // The schema depends on category + site only; shopId is just the call's auth context, so keeping it
    // out of the key lets shops of the same seller share one fetch.
    private static string BuildKey(string categoryId, string site) =>
        $"tiktok-category-schema|{(string.IsNullOrWhiteSpace(site) ? "US" : site.Trim().ToUpperInvariant())}|{categoryId?.Trim()}";
}
