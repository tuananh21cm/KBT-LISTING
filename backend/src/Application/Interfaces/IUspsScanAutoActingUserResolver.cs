namespace Application.Interfaces;

/// <summary>
/// Resolves the acting user for automatic USPS scan pushes (admin stamp / audit).
/// </summary>
public interface IUspsScanAutoActingUserResolver
{
    /// <summary>
    /// First active Admin user id, or null when none exist.
    /// </summary>
    Task<Guid?> ResolveActingAdminUserIdAsync(CancellationToken cancellationToken = default);
}
