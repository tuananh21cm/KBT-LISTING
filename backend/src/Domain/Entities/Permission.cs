using Domain.Common;

namespace Domain.Entities;

/// <summary>
/// Fine-grained permission for application and module scoping.
/// </summary>
public class Permission : BaseEntity
{
    /// <summary>
    /// Permission identifier.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Unique permission code (e.g. seller.dashboard.view).
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// Display name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Functional module name.
    /// </summary>
    public string Module { get; set; } = string.Empty;

    /// <summary>
    /// Target application (e.g. seller-web).
    /// </summary>
    public string Application { get; set; } = string.Empty;

    /// <summary>
    /// Optional description.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Role mappings.
    /// </summary>
    public ICollection<RolePermission> RolePermissions { get; set; } = [];
}
