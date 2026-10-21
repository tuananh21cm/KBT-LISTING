using Application.Interfaces;
using Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Shared.Constants;

namespace Application.Services;

/// <summary>
/// Picks the first active Admin as acting user for USPS auto push.
/// </summary>
public sealed class UspsScanAutoActingUserResolver : IUspsScanAutoActingUserResolver
{
    private readonly UserManager<ApplicationUser> _userManager;

    /// <summary>Creates the resolver.</summary>
    public UspsScanAutoActingUserResolver(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    /// <inheritdoc />
    public async Task<Guid?> ResolveActingAdminUserIdAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var admins = await _userManager.GetUsersInRoleAsync(RoleNames.Admin);
        var id = admins
            .Where(u => u.IsActive)
            .OrderBy(u => u.CreatedAt)
            .Select(u => (Guid?)u.Id)
            .FirstOrDefault();
        return id;
    }
}
