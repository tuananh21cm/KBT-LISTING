using Application.DTOs.KbtCrm;

namespace Application.Interfaces;

/// <summary>
/// Mirrors KBT CRM teams/users/shops into local users and shops (replaces the Excel import).
/// Match keys: user by <c>username</c>, shop by TikTok <c>shop_code</c>; a team's shared account
/// owns the team's shops and members get <c>TeamOwnerId</c> = that account.
/// </summary>
public interface IKbtCrmOrgSyncService
{
    /// <summary>Runs one full pull now. Throws <c>BusinessException</c> when base URL / secret are not configured.</summary>
    Task<KbtCrmSyncResultDto> RunAsync(string trigger, CancellationToken cancellationToken = default);
}
