namespace Application.DTOs.Order;

/// <summary>
/// Result of refreshing one order's commerce status from 4Seller by exact orderNo search.
/// </summary>
public class OrderPlatformStatusSyncResultDto
{
    /// <summary>Whether 4Seller returned a matching order in any searched bucket.</summary>
    public bool Found { get; set; }

    /// <summary>
    /// Whether any persisted commerce field changed (status, tracking, clocks, items, …).
    /// False when found but identical, or when not found.
    /// </summary>
    public bool Changed { get; set; }

    /// <summary>Local <c>PlatformOrderStatus</c> before apply.</summary>
    public string? PreviousPlatformOrderStatus { get; set; }

    /// <summary>Local <c>PlatformOrderStatus</c> after apply (unchanged when not found).</summary>
    public string? PlatformOrderStatus { get; set; }

    /// <summary>Resolved workflow tab after apply.</summary>
    public string? WorkflowTab { get; set; }

    /// <summary>Local tracking after apply.</summary>
    public string? TrackingNo { get; set; }

    /// <summary>Human-readable outcome for toast / API message.</summary>
    public string Summary { get; set; } = string.Empty;
}
