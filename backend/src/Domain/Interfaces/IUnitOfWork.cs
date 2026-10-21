namespace Domain.Interfaces;

/// <summary>
/// Unit of work for transactional persistence.
/// </summary>
public interface IUnitOfWork
{
    /// <summary>
    /// Permission repository.
    /// </summary>
    IRepository<Entities.Permission> Permissions { get; }

    /// <summary>
    /// Refresh token repository.
    /// </summary>
    IRepository<Entities.RefreshToken> RefreshTokens { get; }

    /// <summary>
    /// Role permission repository.
    /// </summary>
    IRepository<Entities.RolePermission> RolePermissions { get; }

    /// <summary>
    /// Persists pending changes.
    /// </summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
