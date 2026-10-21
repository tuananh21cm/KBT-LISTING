using Application.DTOs.FourSeller;

namespace Application.Interfaces;

/// <summary>
/// Cached access to the live TikTok category attribute schema
/// (4Seller <c>GET /api/meta/tiktok/get-category-attribute-list-by-id</c>).
/// <para>
/// This is the only trustworthy answer to "which Specifics does this category require": the copy
/// 4Seller embeds in a listing detail (<c>categoryProductAttributeList</c>) describes the category the
/// listing had when it was crawled, so it goes stale the moment the category changes — and the
/// mandatory set differs sharply between categories (<c>897040</c> requires nothing, <c>903824</c>
/// requires Weaving method, Washing instructions, Region of origin and Material).
/// </para>
/// <para>
/// A single publish reads the schema several times (Specifics map, sale-prop ids, mandatory seeding),
/// and a category is shared by many listings, so the result is cached per <c>site|categoryId</c>.
/// </para>
/// </summary>
public interface ITikTokCategorySchemaProvider
{
    /// <summary>
    /// Category attribute schema for <paramref name="categoryId"/>, from cache when fresh.
    /// Throws <see cref="Domain.Exceptions.BusinessException"/> when 4Seller cannot be reached.
    /// </summary>
    Task<IReadOnlyList<FourSellerCategoryAttributeDto>> GetAsync(
        Guid ownerId,
        string categoryId,
        long fourSellerShopId,
        string site,
        CancellationToken cancellationToken = default);

    /// <summary>Drops the cached schema of one category (used after an admin sync / category change).</summary>
    void Invalidate(string categoryId, string site);
}
