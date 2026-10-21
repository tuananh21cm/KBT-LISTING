namespace Application.Interfaces;

/// <summary>Purges auto publish run logs older than configured retention.</summary>
public interface IListingAutoPublishLogRetentionService
{
  Task<int> PurgeOldRunsAsync(CancellationToken cancellationToken = default);
}
