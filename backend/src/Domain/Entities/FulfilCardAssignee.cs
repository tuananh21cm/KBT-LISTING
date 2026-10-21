namespace Domain.Entities;

/// <summary>Many-to-many: card assigned to users (cấp cho nhiều người).</summary>
public class FulfilCardAssignee
{
    /// <summary>Card id.</summary>
    public Guid FulfilCardId { get; set; }

    /// <summary>User id.</summary>
    public Guid UserId { get; set; }

    /// <summary>Card navigation.</summary>
    public FulfilCard FulfilCard { get; set; } = null!;

    /// <summary>User navigation.</summary>
    public ApplicationUser User { get; set; } = null!;
}
