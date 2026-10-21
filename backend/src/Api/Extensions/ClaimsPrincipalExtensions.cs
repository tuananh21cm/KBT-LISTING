using Shared.Constants;
using System.Security.Claims;

namespace Api.Extensions;

/// <summary>
/// Claims principal extension methods.
/// </summary>
public static class ClaimsPrincipalExtensions
{
    /// <summary>
    /// Gets the current user identifier from JWT claims (the account that logged in).
    /// Use for identity actions: password, theme, audit "who did it".
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

    /// <summary>
    /// Gets the data-owner identifier: the team account for a team member
    /// (<c>ApplicationUser.TeamOwnerId</c>), otherwise the user itself. Seller-scoped data
    /// (shops, orders, listings, 4Seller cookie…) is filtered by this id, so every member of a
    /// team works on the same data. Falls back to <see cref="GetUserId"/> for tokens issued
    /// before the <c>owner</c> claim existed.
    /// </summary>
    public static Guid GetOwnerId(this ClaimsPrincipal user)
    {
        var ownerClaim = user.FindFirst(ClaimConstants.OwnerId)?.Value;
        return ownerClaim is not null && Guid.TryParse(ownerClaim, out var ownerId)
            ? ownerId
            : user.GetUserId();
    }
}
