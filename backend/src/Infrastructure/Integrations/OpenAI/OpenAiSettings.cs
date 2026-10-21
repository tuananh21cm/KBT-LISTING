namespace Infrastructure.Integrations.OpenAI;

/// <summary>
/// OpenAI credentials and rewrite-description prompt from appsettings.
/// </summary>
public class OpenAiSettings
{
    /// <summary>
    /// Configuration section name.
    /// </summary>
    public const string SectionName = "OpenAI";

    /// <summary>
    /// OpenAI API key (Bearer).
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Chat model id (e.g. gpt-5.4).
    /// </summary>
    public string Model { get; set; } = "gpt-5.4";

    /// <summary>
    /// OpenAI API base URL (must end with /v1 or /v1/).
    /// </summary>
    public string BaseUrl { get; set; } = "https://api.openai.com/v1/";

    /// <summary>
    /// Optional HTTP(S)/SOCKS egress proxy so OpenAI sees an allowed region
    /// (Vietnam and some territories return <c>unsupported_country_region_territory</c>).
    /// Formats match 4Seller: <c>host:port</c>, <c>host:port:user:pass</c>,
    /// or <c>http://user:pass@host:port</c> / <c>socks5://…</c>.
    /// </summary>
    public string? Proxy { get; set; }

    /// <summary>
    /// System message holding the copywriter persona, SEO rules and output contract.
    /// </summary>
    public string RewriteDescriptionSystemPrompt { get; set; } = string.Empty;

    /// <summary>
    /// User message template with {{PRODUCT_TITLE}}, {{PRODUCT_FACTS}} and
    /// {{PRODUCT_DESCRIPTION}} placeholders. Carries data only — never instructions.
    /// </summary>
    public string RewriteDescriptionPrompt { get; set; } = string.Empty;

    /// <summary>
    /// Sampling temperature for rewrites. Higher values keep bulk-published
    /// listings from reading like duplicates of each other.
    /// </summary>
    public double RewriteTemperature { get; set; } = 0.75;

    /// <summary>
    /// System message holding the TikTok Shop US title formula, SEO rules and banned wording.
    /// </summary>
    public string RewriteTitleSystemPrompt { get; set; } = string.Empty;

    /// <summary>
    /// User message template with {{PRODUCT_TITLE}}, {{PRODUCT_FACTS}} and
    /// {{REWRITTEN_DESCRIPTION}} placeholders. Carries data only — never instructions.
    /// </summary>
    public string RewriteTitlePrompt { get; set; } = string.Empty;

    /// <summary>
    /// Sampling temperature for titles. Lower than the description: a title is a
    /// keyword slot, not creative copy, so drifting off the primary keyword costs traffic.
    /// </summary>
    public double RewriteTitleTemperature { get; set; } = 0.4;

    /// <summary>
    /// System message that asks the model to list marketplace / private-label brand tokens
    /// in a supplier title so they can be stripped without rewriting the rest.
    /// </summary>
    public string StripTitleBrandsSystemPrompt { get; set; } = string.Empty;

    /// <summary>
    /// User message template with <c>{{PRODUCT_TITLE}}</c>. Returns JSON
    /// <c>{"brands":["SHEIN BAE"]}</c> — the full phrase, never a truncated marketplace root.
    /// </summary>
    public string StripTitleBrandsPrompt { get; set; } = string.Empty;

    /// <summary>Low temperature: brand identification is classification, not copy.</summary>
    public double StripTitleBrandsTemperature { get; set; } = 0.1;

    /// <summary>
    /// System prompt for «Hôm nay bán gì?» (JSON analysis + Shein keywords).
    /// </summary>
    public string WhatToSellTodaySystemPrompt { get; set; } = string.Empty;

    /// <summary>
    /// User prompt template with <c>{{SHOP_CONTEXT}}</c> placeholder.
    /// </summary>
    public string WhatToSellTodayUserPrompt { get; set; } = string.Empty;

    /// <summary>
    /// Sampling temperature for what-to-sell JSON (grounded on shop data, low creativity).
    /// </summary>
    public double WhatToSellTodayTemperature { get; set; } = 0.3;

    /// <summary>
    /// System prompt for «Shop này bán gì» (JSON analysis + snapshot keyword ids).
    /// </summary>
    public string ShopKeywordResearchSystemPrompt { get; set; } = string.Empty;

    /// <summary>
    /// User prompt template with <c>{{SHOP_CONTEXT}}</c> placeholder.
    /// </summary>
    public string ShopKeywordResearchUserPrompt { get; set; } = string.Empty;

    /// <summary>
    /// Sampling temperature for shop keyword research JSON (grounded, low creativity).
    /// </summary>
    public double ShopKeywordResearchTemperature { get; set; } = 0.2;

    /// <summary>
    /// System prompt for «Shop này bán gì» when inventing keywords from catalog audience.
    /// </summary>
    public string ShopKeywordResearchSuggestSystemPrompt { get; set; } = string.Empty;

    /// <summary>
    /// User prompt template with <c>{{SHOP_CONTEXT}}</c> for the audience-suggest path.
    /// </summary>
    public string ShopKeywordResearchSuggestUserPrompt { get; set; } = string.Empty;

    /// <summary>
    /// Sampling temperature for invented keywords (slightly higher than the locked-id path).
    /// </summary>
    public double ShopKeywordResearchSuggestTemperature { get; set; } = 0.35;
}
