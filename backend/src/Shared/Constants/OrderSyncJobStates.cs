namespace Shared.Constants;

/// <summary>Lifecycle of a manual order sync job (<c>OrderSyncJobStatusDto.State</c>).</summary>
public static class OrderSyncJobStates
{
    /// <summary>Still pulling; the client should keep polling.</summary>
    public const string Running = "running";

    /// <summary>Finished — individual sellers may still have failed (or the gate was busy).</summary>
    public const string Completed = "completed";

    /// <summary>The run itself threw before it could report a result.</summary>
    public const string Failed = "failed";

    /// <summary>True once polling can stop.</summary>
    public static bool IsTerminal(string? state) =>
        string.Equals(state, Completed, StringComparison.OrdinalIgnoreCase)
        || string.Equals(state, Failed, StringComparison.OrdinalIgnoreCase);
}
