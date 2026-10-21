namespace Shared.Constants;

/// <summary>Pipeline step codes for auto publish run logs.</summary>
public static class ListingAutoPublishStepCodes
{
    public const string LoadFull = "load_full";
    public const string Category = "category";
    public const string Commerce = "commerce";
    public const string Specifics = "specifics";
    public const string Rewrite = "rewrite";
    public const string Media = "media";
    public const string Validate = "validate";
    public const string Publish = "publish";

    /// <summary>Fixed pipeline order for resume / hydration.</summary>
    public static readonly string[] PipelineOrder =
    [
        LoadFull,
        Category,
        Commerce,
        Specifics,
        Rewrite,
        Media,
        Validate,
        Publish
    ];

    /// <summary>Sub-step codes recorded inside <see cref="Category"/> for the AI audit trail.</summary>
    public static class CategorySubSteps
    {
        /// <summary>What was sent to 4Seller AI (shop, site, title).</summary>
        public const string AiRequest = "category_ai_request";

        /// <summary>What 4Seller AI answered (category, path, leaf, validated).</summary>
        public const string AiResponse = "category_ai_response";

        /// <summary>AI call failed; the existing category was kept.</summary>
        public const string AiFailed = "category_ai_failed";

        /// <summary>AI answered a category missing from the catalog, inactive, or not a leaf.</summary>
        public const string AiInvalid = "category_ai_invalid";

        /// <summary>AI agrees with the category already on the listing.</summary>
        public const string Confirmed = "category_confirmed";

        /// <summary>Listing had no category and took the AI one.</summary>
        public const string Assigned = "category_assigned";

        /// <summary>An unsafe category was replaced by the AI one.</summary>
        public const string Overridden = "category_overridden";

        /// <summary>AI disagreed but the existing category stands.</summary>
        public const string Kept = "category_kept";

        /// <summary>Specifics of the replaced category were dropped so they get mapped again.</summary>
        public const string AttributesReset = "category_attributes_reset";

        /// <summary>
        /// Listing/AI landed on a blocked leaf (see <c>category_blocked_remap</c>) and was remapped
        /// to a publishable sibling without waiting for AI agreement.
        /// </summary>
        public const string BlockedRemapped = "category_blocked_remapped";
    }

    public static bool IsKnown(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return false;
        }

        foreach (var known in PipelineOrder)
        {
            if (string.Equals(known, code, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
