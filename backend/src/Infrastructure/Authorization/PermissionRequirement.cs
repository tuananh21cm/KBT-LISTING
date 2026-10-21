using Microsoft.AspNetCore.Authorization;

namespace Infrastructure.Authorization;

/// <summary>
/// Permission-based authorization requirement.
/// </summary>
public class PermissionRequirement : IAuthorizationRequirement
{
    /// <summary>
    /// Required permission code(s). Any match succeeds when <see cref="MatchAny"/> is true.
    /// </summary>
    public IReadOnlyList<string> Permissions { get; }

    /// <summary>
    /// When true, user needs any of the listed permissions; otherwise all (single).
    /// </summary>
    public bool MatchAny { get; }

    /// <summary>Single required permission (first).</summary>
    public string Permission => Permissions[0];

    /// <summary>
    /// Initializes the requirement with one permission.
    /// </summary>
    public PermissionRequirement(string permission)
    {
        Permissions = [permission];
        MatchAny = false;
    }

    /// <summary>
    /// Initializes the requirement with multiple permissions (OR when matchAny).
    /// </summary>
    public PermissionRequirement(IEnumerable<string> permissions, bool matchAny)
    {
        Permissions = permissions
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p.Trim())
            .ToArray();
        MatchAny = matchAny;
        if (Permissions.Count == 0)
        {
            throw new ArgumentException("At least one permission is required.", nameof(permissions));
        }
    }
}
