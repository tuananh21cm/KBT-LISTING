namespace Application.DTOs.Order;

/// <summary>One library / assigned issue tag.</summary>
public class IssueTagDto
{
    /// <summary>Tag id.</summary>
    public Guid Id { get; set; }

    /// <summary>Display label.</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>Seeded system preset.</summary>
    public bool IsSystem { get; set; }
}

/// <summary>Create a custom tag in the reusable library.</summary>
public class CreateIssueTagRequest
{
    /// <summary>Label (trim, max 50).</summary>
    public string Label { get; set; } = string.Empty;
}

/// <summary>Rename a library tag.</summary>
public class UpdateIssueTagRequest
{
    /// <summary>New label (trim, max 50).</summary>
    public string Label { get; set; } = string.Empty;
}

/// <summary>Replace the set of tags on an order.</summary>
public class SetOrderIssueTagsRequest
{
    /// <summary>Full set of tag ids to attach (empty = clear).</summary>
    public List<Guid> TagIds { get; set; } = [];
}

/// <summary>Update order issue workflow status.</summary>
public class SetOrderIssueStatusRequest
{
    /// <summary><c>Processing</c> or <c>Done</c>.</summary>
    public string Status { get; set; } = string.Empty;
}

/// <summary>Order issue tags + status snapshot (also embedded on list/detail).</summary>
public class OrderIssueStateDto
{
    /// <summary>Attached tags.</summary>
    public List<IssueTagDto> IssueTags { get; set; } = [];

    /// <summary><c>None</c> / <c>Processing</c> / <c>Done</c>.</summary>
    public string IssueStatus { get; set; } = "None";

    /// <summary>Denormalized labels join (Orders.Message).</summary>
    public string? Message { get; set; }
}
