using Application.DTOs.Listing;
using Application.Services;

namespace Application.Interfaces;

/// <summary>
/// Learns which product attribute values TikTok refuses, from the publish failures themselves, and
/// hands the result to every step that picks an attribute value.
/// </summary>
public interface ITikTokAttributeRejectionService
{
    /// <summary>
    /// Values blocked for a category (plus the ones blocked everywhere). Cached, so callers on the
    /// publish path can ask per listing without extra database round trips.
    /// </summary>
    Task<TikTokAttributeValueDenylist> GetDenylistAsync(
        string? categoryId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records the attribute value named in a failure message. Returns false when there is nothing new
    /// to learn: the message is not a custom-value rejection (the usual case), or the value is already
    /// blocked. Safe to call on every sync pass — a failed listing keeps its error message.
    /// </summary>
    Task<bool> RecordFromErrorAsync(
        Guid? listingId,
        string? categoryId,
        string? error,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Counter-evidence from a listing TikTok accepted: every blocked value the payload carries with a
    /// numeric id stops blocking, because TikTok would have refused the publish otherwise. Returns how
    /// many values were unblocked. Safe to call on every sync pass — a value already demoted is skipped.
    /// </summary>
    Task<int> RecordAcceptedAsync(
        Guid? listingId,
        string? categoryId,
        string? rawPayload,
        CancellationToken cancellationToken = default);

    /// <summary>Everything learned so far, newest failure first.</summary>
    Task<List<TikTokAttributeValueRejectionDto>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Unblocks a value, for when TikTok accepts it again. Returns false when the row is already gone.
    /// </summary>
    Task<bool> RemoveAsync(Guid id, CancellationToken cancellationToken = default);
}
