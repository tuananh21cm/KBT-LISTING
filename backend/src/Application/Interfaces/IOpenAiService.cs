using Application.DTOs.Listing;

namespace Application.Interfaces;

/// <summary>
/// OpenAI chat client for listing content generation.
/// </summary>
public interface IOpenAiService
{
    /// <summary>
    /// Rewrites a product description using the configured SEO prompt template.
    /// Returns markdown-lite text (## headings, - bullets; plain text, no bold) — never HTML.
    /// </summary>
    Task<string> RewriteProductDescriptionAsync(
        ProductDescriptionRewriteInput input,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Rewrites a product title into a TikTok Shop US search-optimized one using the
    /// configured title prompt. Returns a single plain-text line — never markdown or HTML.
    /// </summary>
    Task<string> RewriteProductTitleAsync(
        ProductTitleRewriteInput input,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists marketplace / private-label brand tokens in a supplier title as JSON
    /// <c>{"brands":["SHEIN BAE","SUMWON"]}</c> — full phrases, not a truncated
    /// marketplace root. Does not rewrite the title.
    /// </summary>
    Task<string> StripTitleBrandsAsync(
        string originalTitle,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Completes a constrained JSON response for attribute mapping.
    /// Returns the model content as-is (caller validates/parses).
    /// </summary>
    Task<string> CompleteJsonAsync(
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// «Hôm nay bán gì?» — returns JSON with analysis + 2 Shein keyword searches.
    /// Uses <c>OpenAI:WhatToSellToday*</c> prompts; <paramref name="shopContext"/> replaces
    /// <c>{{SHOP_CONTEXT}}</c>.
    /// </summary>
    Task<string> CompleteWhatToSellTodayAsync(
        string shopContext,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// «Shop này bán gì» — returns JSON with analysis + snapshot keyword ids.
    /// Uses <c>OpenAI:ShopKeywordResearch*</c> prompts; <paramref name="shopContext"/> replaces
    /// <c>{{SHOP_CONTEXT}}</c>.
    /// </summary>
    Task<string> CompleteShopKeywordResearchAsync(
        string shopContext,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// «Shop này bán gì» when the shop has no TikTok trending snapshot — returns JSON
    /// with analysis + invented search keywords. Uses <c>OpenAI:ShopKeywordResearchSuggest*</c>.
    /// </summary>
    Task<string> CompleteShopKeywordResearchSuggestAsync(
        string shopContext,
        CancellationToken cancellationToken = default);
}
