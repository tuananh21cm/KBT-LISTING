using Microsoft.AspNetCore.Authorization;
using Shared.Constants;

namespace Infrastructure.Authorization;

/// <summary>
/// Validates permission claims against authorization requirements.
/// </summary>
public class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
{
    /// <inheritdoc />
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        if (context.User.IsInRole(RoleNames.Admin))
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        var permissions = context.User.Claims
            .Where(c => c.Type == ClaimConstants.Permission)
            .Select(c => c.Value)
            .ToHashSet(StringComparer.Ordinal);

        var matched = requirement.MatchAny
            ? requirement.Permissions.Any(permissions.Contains)
            : requirement.Permissions.All(permissions.Contains);

        if (matched)
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
