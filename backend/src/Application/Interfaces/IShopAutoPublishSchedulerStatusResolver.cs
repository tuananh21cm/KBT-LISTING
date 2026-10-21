namespace Application.Interfaces;

/// <summary>Resolves aggregate auto publish scheduler status for shop list rows.</summary>
public interface IShopAutoPublishSchedulerStatusResolver
{
    /// <summary>
    /// Returns scheduler status per shop id. Keys omitted when no scheduler badge should show.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, ShopAutoPublishSchedulerStatusResult>> ResolveAsync(
        IReadOnlyList<Guid> shopIds,
        CancellationToken cancellationToken = default);
}

/// <summary>Resolved scheduler badge for one shop.</summary>
public sealed record ShopAutoPublishSchedulerStatusResult(
    string SchedulerStatus,
    DateTime? NextPickAt,
    int EligibleDraftCount);
