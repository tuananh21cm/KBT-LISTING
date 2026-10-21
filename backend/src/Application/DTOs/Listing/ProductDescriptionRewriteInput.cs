namespace Application.DTOs.Listing;

/// <summary>
/// Prompt inputs for a single OpenAI description rewrite call.
/// </summary>
public class ProductDescriptionRewriteInput
{
    /// <summary>Product title.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Original description, already stripped to plain text.</summary>
    public string OriginalDescription { get; set; } = string.Empty;

    /// <summary>
    /// Pre-rendered "Verified product facts" block (category, brand, colors, sizes, attributes).
    /// Empty when the listing has no structured data yet.
    /// </summary>
    public string ProductFacts { get; set; } = string.Empty;

    /// <summary>Optional extra instruction appended to the user message (e.g. shorten retry).</summary>
    public string? ExtraInstruction { get; set; }

    /// <summary>
    /// Optional OpenAI chat <c>seed</c> (Dev harness). Null = omit from the request body.
    /// </summary>
    public int? Seed { get; set; }
}

/// <summary>
/// Prompt inputs for a single OpenAI title rewrite call. Runs after the description
/// so the title can be built from the copy the shopper will actually read.
/// </summary>
public class ProductTitleRewriteInput
{
    /// <summary>Current product title, typically supplier wording.</summary>
    public string OriginalTitle { get; set; } = string.Empty;

    /// <summary>Freshly rewritten description (markdown-lite), the keyword source.</summary>
    public string RewrittenDescription { get; set; } = string.Empty;

    /// <summary>
    /// Pre-rendered "Verified product facts" block (category, brand, colors, sizes, attributes).
    /// </summary>
    public string ProductFacts { get; set; } = string.Empty;

    /// <summary>Optional extra instruction appended to the user message (e.g. length retry).</summary>
    public string? ExtraInstruction { get; set; }

    /// <summary>
    /// Optional OpenAI chat <c>seed</c> (Dev harness). Null = omit from the request body.
    /// </summary>
    public int? Seed { get; set; }
}
