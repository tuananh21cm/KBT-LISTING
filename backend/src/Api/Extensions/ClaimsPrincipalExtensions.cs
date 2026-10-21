using Shared.Constants;
using System.Security.Claims;

namespace Api.Extensions;

/// <summary>
/// Claims principal extension methods.
/// </summary>
public static class ClaimsPrincipalExtensions
{
    /// <summary>
    /// Gets the current user identifier from JWT claims.
    /// </summary>
    public static Guid GetUserId(this ClaimsPrincipal user)
    {
        var userIdClaim = user.FindFirst(ClaimConstants.UserId)?.Value
            ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (userIdClaim is null || !Guid.TryParse(userIdClaim, out var userId))
        {
            throw new UnauthorizedAccessException("Không xác định được người dùng.");
        }

        return userId;
    }
}
