namespace Domain.Entities;

/// <summary>
/// Many-to-many: order ↔ issue tag assignment.
/// </summary>
public class OrderIssueTag
{
    /// <summary>Parent order.</summary>
    public Guid OrderId { get; set; }

    /// <summary>Assigned library tag.</summary>
    public Guid IssueTagId { get; set; }

    /// <summary>Who attached the tag.</summary>
    public Guid? AssignedByUserId { get; set; }

    /// <summary>When the tag was attached (UTC).</summary>
    public DateTime AssignedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Order navigation.</summary>
    public Order Order { get; set; } = null!;

    /// <summary>Tag navigation.</summary>
    public IssueTag IssueTag { get; set; } = null!;

    /// <summary>Assigner navigation.</summary>
    public ApplicationUser? AssignedBy { get; set; }
}
