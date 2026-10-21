using Application.DTOs.Shop;

namespace Application.Interfaces;

/// <summary>
/// Aggregates shop bestsellers + niche + cost, then asks OpenAI what to source today.
/// </summary>
public interface IWhatToSellTodayService
{
    /// <summary>
    /// Builds «Hôm nay bán gì?» for one shop.
    /// When <paramref name="ownerId"/> is set, the shop must belong to that seller.
    /// </summary>
    Task<WhatToSellTodayDto> AnalyzeAsync(
        Guid shopId,
        Guid? ownerId,
        CancellationToken cancellationToken = default);
}
