namespace Application.DTOs.Order;

/// <summary>Result of comparing the denormalized workflow columns against a fresh recompute.</summary>
public class OrderWorkflowSignalsDriftDto
{
    /// <summary>Orders inspected.</summary>
    public int Scanned { get; set; }

    /// <summary>Orders whose stored columns disagreed with the recompute.</summary>
    public int DriftCount { get; set; }

    /// <summary>Orders that had never been stamped (tab still null).</summary>
    public int UnstampedCount { get; set; }

    /// <summary>Orders rewritten, when the caller asked for a repair.</summary>
    public int RepairedCount { get; set; }

    /// <summary>How long the scan took.</summary>
    public long DurationMs { get; set; }

    /// <summary>First few mismatches, for diagnosis.</summary>
    public List<OrderWorkflowSignalsDriftRowDto> Samples { get; set; } = [];
}

/// <summary>One drifted order: what is stored versus what the helpers now return.</summary>
public class OrderWorkflowSignalsDriftRowDto
{
    /// <summary>Order row id.</summary>
    public Guid OrderId { get; set; }

    /// <summary>Platform order code, for looking the order up in the UI.</summary>
    public string? PlatformOrderId { get; set; }

    /// <summary>Column value currently in the database.</summary>
    public string? StoredWorkflowTab { get; set; }

    /// <summary>Value <c>OrderWorkflowTabs.Resolve</c> returns now.</summary>
    public string? ExpectedWorkflowTab { get; set; }

    /// <summary>Flags that disagree, formatted as <c>name: stored→expected</c>.</summary>
    public List<string> FlagDrift { get; set; } = [];
}
