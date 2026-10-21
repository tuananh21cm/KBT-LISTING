using Domain.Common;

namespace Domain.Entities;

/// <summary>
/// Reusable issue-tag library entry (system presets + user-created).
/// </summary>
public class IssueTag : BaseEntity
{
    /// <summary>Row identifier.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Display label (unique case-insensitive).</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>True for seeded presets (Hết size, …).</summary>
    public bool IsSystem { get; set; }

    /// <summary>User who created a custom tag (null for system).</summary>
    public Guid? CreatedByUserId { get; set; }

    /// <summary>Creator navigation.</summary>
    public ApplicationUser? CreatedBy { get; set; }

    /// <summary>Orders using this tag.</summary>
    public ICollection<OrderIssueTag> OrderAssignments { get; set; } = new List<OrderIssueTag>();
}

/// <summary>Known <see cref="Order.IssueStatus"/> values.</summary>
public static class OrderIssueStatuses
{
    /// <summary>No open issue (no tags, or cleared).</summary>
    public const string None = "None";

    /// <summary>Has tags and still being worked.</summary>
    public const string Processing = "Processing";

    /// <summary>Resolved — kept for history; out of out_stock queue.</summary>
    public const string Done = "Done";
}
