using Application.DTOs.ProductOpportunity;

namespace Application.Interfaces;

/// <summary>
/// Scores the shop's TikTok keyword snapshot against catalog audience + season, then asks OpenAI
/// to write copy. When the snapshot is empty, invents audience-fit search keywords instead.
/// </summary>
public interface IShopKeywordResearchService
{
    /// <summary>
    /// Return the last saved analysis for a shop, or null when none exists.
    /// Does not call OpenAI. When <paramref name="ownerId"/> is set, the shop must belong to that seller.
    /// </summary>
    Task<ShopKeywordResearchDto?> GetCachedAsync(
        Guid shopId,
        Guid? ownerId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Research keywords for one shop, persist the result, and return it.
    /// When <paramref name="ownerId"/> is set, the shop must belong to that seller.
    /// </summary>
    Task<ShopKeywordResearchDto> ResearchAsync(
        Guid shopId,
        Guid? ownerId,
        CancellationToken cancellationToken = default);
}
