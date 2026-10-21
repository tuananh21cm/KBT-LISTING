namespace Domain.Entities;

/// <summary>Many-to-many: purchase account assigned to users (cấp cho nhiều người).</summary>
public class FulfilAccountAssignee
{
    /// <summary>Account id.</summary>
    public Guid FulfilAccountId { get; set; }

    /// <summary>User id.</summary>
    public Guid UserId { get; set; }

    /// <summary>Account navigation.</summary>
    public FulfilAccount FulfilAccount { get; set; } = null!;

    /// <summary>User navigation.</summary>
    public ApplicationUser User { get; set; } = null!;
}
