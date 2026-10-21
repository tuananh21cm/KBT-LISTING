namespace Application.DTOs.Order;

/// <summary>Request to sync one fulfil account via stored SHEIN session (local mode).</summary>
public class SheinSyncRunRequest
{
    /// <summary>Fulfil account id that just received a session push.</summary>
    public Guid AccountId { get; set; }
}

/// <summary>Result of a manual local SHEIN sync run.</summary>
public class SheinSyncRunResultDto
{
    /// <summary>Human-readable summary.</summary>
    public string Summary { get; set; } = string.Empty;
}
