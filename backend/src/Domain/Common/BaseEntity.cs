namespace Domain.Common;

/// <summary>
/// Base entity with audit fields.
/// </summary>
public abstract class BaseEntity
{
    /// <summary>
    /// Creation timestamp (UTC).
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Last update timestamp (UTC).
    /// </summary>
    public DateTime? UpdatedAt { get; set; }
}
