namespace Application.DTOs.Role;

/// <summary>
/// Lightweight role option for user assignment dropdowns.
/// </summary>
public class RoleOptionDto
{
    /// <summary>Role identifier.</summary>
    public Guid Id { get; set; }

    /// <summary>Role name.</summary>
    public string Name { get; set; } = string.Empty;
}
