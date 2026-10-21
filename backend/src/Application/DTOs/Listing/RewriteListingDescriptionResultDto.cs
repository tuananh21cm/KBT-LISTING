namespace Application.DTOs.Listing;

/// <summary>
/// Result of AI description rewrite — HTML ready for HtmlEditor.
/// </summary>
public class RewriteListingDescriptionResultDto
{
    /// <summary>Rewritten description as simple HTML.</summary>
    public string DescriptionHtml { get; set; } = string.Empty;

    /// <summary>
    /// SEO product title built from the rewritten description. Falls back to the cleaned
    /// original title when the model call fails, so it is never empty.
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Backend TikTok search terms (2–3 primary + 2–3 secondary). Empty when the model
    /// omitted the sentinel — the caller keeps the form values it already has.
    /// </summary>
    public List<string> SearchTerms { get; set; } = [];

    /// <summary>
    /// PDP product highlights (up to 5). Empty when the model omitted the sentinel.
    /// </summary>
    public List<string> ProductHighlights { get; set; } = [];
}
