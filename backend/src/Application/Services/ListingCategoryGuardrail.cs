namespace Application.Services;

/// <summary>What auto publish should do with the category it found on the listing.</summary>
public enum ListingCategoryDecisionKind
{
    /// <summary>Listing had no category — take the AI recommendation.</summary>
    Assign,

    /// <summary>AI agrees with the category already on the listing.</summary>
    Confirmed,

    /// <summary>AI disagrees but the existing category stands.</summary>
    Keep,

    /// <summary>Existing category is unsafe for the product — replace it with the AI one.</summary>
    Override
}

/// <summary>Category identity used by the guardrail (catalog path is optional).</summary>
public sealed record ListingCategoryInfo(string? CategoryId, string? CategoryName, string? Path);

/// <summary>Guardrail outcome plus the reason recorded in the run log.</summary>
public sealed record ListingCategoryDecision(ListingCategoryDecisionKind Kind, string Reason);

/// <summary>
/// Decides whether the category 4Seller attached while crawling should stand or be replaced by the
/// AI recommendation.
/// </summary>
/// <remarks>
/// 4Seller fills <c>categoryId</c> at import, so auto publish used to accept it untouched. That let
/// ordinary fashion items land in costume leaves (e.g. 903824 Costume Tops &amp; Corsets) whose
/// attribute enums TikTok validates differently, which surfaced as Specifics publish failures.
/// Pure logic here so the policy is unit-testable without 4Seller or the database.
/// </remarks>
public static class ListingCategoryGuardrail
{
    /// <summary>Configured risky branches and the title keywords that justify staying in them.</summary>
    public sealed record Options(
        IReadOnlyList<string> RiskyAncestorIds,
        IReadOnlyList<string> RiskyNameKeywords,
        IReadOnlyList<string> TitleKeywords,
        IReadOnlyList<string> BlockedCategoryIds);

    /// <summary>Reads the comma-separated Settings values into a comparable option set.</summary>
    public static Options ParseOptions(
        string? riskyAncestorIds,
        string? riskyNameKeywords,
        string? riskyTitleKeywords,
        string? blockedRemap = null) =>
        new(
            SplitList(riskyAncestorIds),
            SplitList(riskyNameKeywords),
            SplitList(riskyTitleKeywords),
            ListingCategoryBlockedRemap.BlockedIds(ListingCategoryBlockedRemap.Parse(blockedRemap)));

    /// <summary>True when the category sits under a risky ancestor or its name matches a keyword.</summary>
    public static bool IsRiskyCategory(ListingCategoryInfo category, Options options)
    {
        var segments = (category.Path ?? string.Empty)
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var segment in segments)
        {
            if (options.RiskyAncestorIds.Any(id => string.Equals(id, segment, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        if (!string.IsNullOrWhiteSpace(category.CategoryId)
            && options.RiskyAncestorIds.Any(id =>
                string.Equals(id, category.CategoryId!.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        var name = category.CategoryName;
        return !string.IsNullOrWhiteSpace(name)
               && options.RiskyNameKeywords.Any(keyword =>
                   name!.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>True when the leaf itself is known to be unpublishable (Settings blocked remap).</summary>
    public static bool IsBlockedCategory(ListingCategoryInfo category, Options options) =>
        !string.IsNullOrWhiteSpace(category.CategoryId)
        && options.BlockedCategoryIds.Any(id =>
            string.Equals(id, category.CategoryId!.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>True when the product title itself says the item belongs in the risky branch.</summary>
    public static bool TitleMatchesRiskyBranch(string? title, Options options) =>
        !string.IsNullOrWhiteSpace(title)
        && options.TitleKeywords.Any(keyword =>
            title!.Contains(keyword, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Compares the listing category with the AI recommendation. Only called when AI returned a
    /// category; a failed AI call leaves the existing category untouched.
    /// </summary>
    public static ListingCategoryDecision Decide(
        ListingCategoryInfo current,
        ListingCategoryInfo ai,
        bool aiValidated,
        string? title,
        Options options)
    {
        if (string.IsNullOrWhiteSpace(ai.CategoryId))
        {
            return new ListingCategoryDecision(
                ListingCategoryDecisionKind.Keep,
                "AI không trả về category — giữ category hiện tại.");
        }

        if (string.IsNullOrWhiteSpace(current.CategoryId))
        {
            return new ListingCategoryDecision(
                ListingCategoryDecisionKind.Assign,
                aiValidated
                    ? "Listing chưa có category — dùng category AI."
                    : "Listing chưa có category — dùng category AI dù chưa kiểm chứng được theo catalog.");
        }

        if (string.Equals(current.CategoryId!.Trim(), ai.CategoryId!.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return new ListingCategoryDecision(
                ListingCategoryDecisionKind.Confirmed,
                "Category hiện tại khớp với AI.");
        }

        if (!aiValidated)
        {
            return new ListingCategoryDecision(
                ListingCategoryDecisionKind.Keep,
                "AI đề xuất category khác nhưng không hợp lệ theo catalog — giữ category hiện tại.");
        }

        // Blocked leaves (e.g. 903824) cannot be published into for some shops even with a numeric
        // valueId. Override regardless of costume title keywords — staying put has no upside.
        if (IsBlockedCategory(current, options) && !IsBlockedCategory(ai, options))
        {
            return new ListingCategoryDecision(
                ListingCategoryDecisionKind.Override,
                $"Category hiện tại {current.CategoryId} nằm trong blocked remap — ghi đè bằng category AI.");
        }

        if (!IsRiskyCategory(current, options))
        {
            return new ListingCategoryDecision(
                ListingCategoryDecisionKind.Keep,
                "Category hiện tại không thuộc nhánh rủi ro — giữ nguyên dù AI đề xuất khác.");
        }

        // AI stays inside the risky branch but names a different leaf — take it, whatever the title
        // says. Staying put has no upside: 4Seller lists six values for the mandatory `100401` of
        // `903824` and TikTok refuses every one of them as a custom value name, so that leaf simply
        // cannot be published into, while a sibling costume leaf publishes normally.
        if (IsRiskyCategory(ai, options))
        {
            return new ListingCategoryDecision(
                ListingCategoryDecisionKind.Override,
                "AI đề xuất leaf khác trong cùng nhánh costume — ghi đè bằng category AI.");
        }

        if (TitleMatchesRiskyBranch(title, options))
        {
            return new ListingCategoryDecision(
                ListingCategoryDecisionKind.Keep,
                "Title có từ khoá costume/cosplay — giữ category nhánh costume.");
        }

        return new ListingCategoryDecision(
            ListingCategoryDecisionKind.Override,
            "Category hiện tại thuộc nhánh rủi ro và title không phải costume — ghi đè bằng category AI.");
    }

    private static List<string> SplitList(string? raw) =>
        (raw ?? string.Empty)
            .Split([',', ';', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(item => item.ToLowerInvariant())
            .Distinct()
            .ToList();
}
