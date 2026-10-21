using Domain.Entities;
using Domain.Interfaces;
using Infrastructure.Persistence;

namespace Infrastructure.Repositories;

/// <summary>
/// Unit of work implementation.
/// </summary>
public class UnitOfWork : IUnitOfWork
{
    private readonly ApplicationDbContext _context;
    private IRepository<Permission>? _permissions;
    private IRepository<RefreshToken>? _refreshTokens;
    private IRepository<RolePermission>? _rolePermissions;

    /// <summary>
    /// Initializes the unit of work.
    /// </summary>
    public UnitOfWork(ApplicationDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc />
    public IRepository<Permission> Permissions =>
        _permissions ??= new Repository<Permission>(_context);

    /// <inheritdoc />
    public IRepository<RefreshToken> RefreshTokens =>
        _refreshTokens ??= new Repository<RefreshToken>(_context);

    /// <inheritdoc />
    public IRepository<RolePermission> RolePermissions =>
        _rolePermissions ??= new Repository<RolePermission>(_context);

    /// <inheritdoc />
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        _context.SaveChangesAsync(cancellationToken);
}
