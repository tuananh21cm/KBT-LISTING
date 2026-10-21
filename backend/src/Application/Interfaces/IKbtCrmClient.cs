using Application.DTOs.KbtCrm;

namespace Application.Interfaces;

/// <summary>Read-only client for KBT CRM's agent bridge.</summary>
public interface IKbtCrmClient
{
    /// <summary>Fetches <c>GET {baseUrl}/agent/org</c> with <c>x-agent-secret</c>.</summary>
    Task<KbtCrmOrgDto> GetOrgAsync(string baseUrl, string agentSecret, CancellationToken cancellationToken = default);
}
