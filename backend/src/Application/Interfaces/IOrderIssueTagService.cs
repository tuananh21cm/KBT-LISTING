using Application.DTOs.Order;

namespace Application.Interfaces;

/// <summary>
/// Issue-tag library + per-order tag / status workflow.
/// </summary>
public interface IOrderIssueTagService
{
    /// <summary>Lists all library tags (system first, then label).</summary>
    Task<IReadOnlyList<IssueTagDto>> ListLibraryAsync(CancellationToken cancellationToken = default);

    /// <summary>Creates a custom library tag (or returns existing case-insensitive match).</summary>
    Task<IssueTagDto> CreateLibraryTagAsync(
        CreateIssueTagRequest request,
        Guid actingUserId,
        CancellationToken cancellationToken = default);

    /// <summary>Renames a library tag and refreshes denormalized order messages.</summary>
    Task<IssueTagDto> UpdateLibraryTagAsync(
        Guid tagId,
        UpdateIssueTagRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a library tag (not system), detaches from orders, refreshes Message/IssueStatus.
    /// </summary>
    Task DeleteLibraryTagAsync(
        Guid tagId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces tags on an order; syncs Message, IssueStatus rules, audit Issue messages, notify.
    /// </summary>
    Task<OrderIssueStateDto> SetOrderTagsAsync(
        Guid orderId,
        SetOrderIssueTagsRequest request,
        Guid actingUserId,
        CancellationToken cancellationToken = default);

    /// <summary>Sets Processing / Done (reopens Processing when attaching tags while Done).</summary>
    Task<OrderIssueStateDto> SetOrderStatusAsync(
        Guid orderId,
        SetOrderIssueStatusRequest request,
        Guid actingUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Legacy PUT …/message: empty clears tags; non-empty GetOrCreate one tag and replace set.
    /// </summary>
    Task SyncFromLegacyMessageAsync(
        Guid orderId,
        string? message,
        Guid actingUserId,
        CancellationToken cancellationToken = default);
}
