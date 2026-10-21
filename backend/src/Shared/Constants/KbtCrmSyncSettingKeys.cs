namespace Shared.Constants;

/// <summary>
/// Setting keys for pulling teams, users and shops from KBT CRM (<c>GET /agent/org</c>),
/// replacing the Excel import. Managed via admin Settings.
/// </summary>
public static class KbtCrmSyncSettingKeys
{
    public const string Group = "Đồng bộ CRM (kbtcrm)";

    /// <summary>When true, the background job pulls /agent/org on <see cref="IntervalMinutes"/>.</summary>
    public const string Enabled = "kbtcrm.sync.enabled";

    /// <summary>Minutes between automatic pulls. Clamped to <see cref="MinIntervalMinutes"/>–<see cref="MaxIntervalMinutes"/>.</summary>
    public const string IntervalMinutes = "kbtcrm.sync.interval_minutes";

    /// <summary>CRM API root, without the trailing <c>/agent/org</c>. Prod needs the <c>/api-proxy</c> prefix.</summary>
    public const string BaseUrl = "kbtcrm.base_url";

    /// <summary>Value sent as <c>x-agent-secret</c> (CRM env <c>AGENT_BRIDGE_SECRET</c>).</summary>
    public const string AgentSecret = "kbtcrm.agent_secret";

    public const string DefaultBaseUrl = "https://apps.kbt.global/api-proxy";
    public const int DefaultIntervalMinutes = 15;
    public const int MinIntervalMinutes = 1;
    public const int MaxIntervalMinutes = 1440;

    /// <summary>Password given to accounts the sync creates (CRM never shares its hashes). Satisfies Identity policy.</summary>
    public static string DefaultPasswordFor(string username) => $"{username}@Kbt2026";
}
