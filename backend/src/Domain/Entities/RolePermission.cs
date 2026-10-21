namespace Domain.Entities;

/// <summary>
/// Many-to-many mapping between roles and permissions.
/// </summary>
public class RolePermission
{
    /// <summary>
    /// Role identifier.
    /// </summary>
    public Guid RoleId { get; set; }

    /// <summary>
    /// Permission identifier.
    /// </summary>
    public Guid PermissionId { get; set; }

    /// <summary>
    /// Navigation to role.
    /// </summary>
    public ApplicationRole Role { get; set; } = null!;

    /// <summary>
    /// Navigation to permission.
    /// </summary>
    public Permission Permission { get; set; } = null!;
}
