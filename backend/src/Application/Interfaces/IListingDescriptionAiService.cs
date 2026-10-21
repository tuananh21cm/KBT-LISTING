using Application.DTOs.Listing;

namespace Application.Interfaces;

/// <summary>
/// AI rewrite of listing product descriptions (form values only; no persist).
/// </summary>
public interface IListingDescriptionAiService
{
    /// <summary>
    /// Rewrites description from title + original description; returns HTML for the editor.
    /// </summary>
    Task<RewriteListingDescriptionResultDto> RewriteAsync(
        RewriteListingDescriptionRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Title-mode + whether Rewrite may run again after a successful rewrite.
    /// </summary>
    Task<ListingRewriteConfigDto> GetRewriteConfigAsync(
        CancellationToken cancellationToken = default);
}
