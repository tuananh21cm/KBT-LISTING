using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Application.DTOs.Auth;
using Application.Interfaces;
using Domain.Entities;
using Domain.Exceptions;
using Domain.Interfaces;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Shared.Constants;

namespace Infrastructure.Identity;

/// <summary>
/// JWT and refresh token service implementation.
/// </summary>
public class TokenService : ITokenService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ApplicationDbContext _context;
    private readonly IUnitOfWork _unitOfWork;
    private readonly JwtSettings _jwtSettings;

    /// <summary>
    /// Initializes the token service.
    /// </summary>
    public TokenService(
        UserManager<ApplicationUser> userManager,
        ApplicationDbContext context,
        IUnitOfWork unitOfWork,
        IOptions<JwtSettings> jwtSettings)
    {
        _userManager = userManager;
        _context = context;
        _unitOfWork = unitOfWork;
        _jwtSettings = jwtSettings.Value;
    }

    /// <inheritdoc />
    public async Task<AuthResponseDto> GenerateTokensAsync(
        ApplicationUser user,
        string? ipAddress,
        CancellationToken cancellationToken = default,
        bool persistent = false)
    {
        var roles = await _userManager.GetRolesAsync(user);
        var permissions = await GetUserPermissionsAsync(user.Id, cancellationToken);
        var expiresAt = DateTime.UtcNow.AddMinutes(_jwtSettings.AccessTokenExpirationMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.UniqueName, user.UserName ?? string.Empty),
            new(JwtRegisteredClaimNames.Email, user.Email ?? string.Empty),
            new(ClaimConstants.UserId, user.Id.ToString()),
            new(ClaimConstants.SecurityStamp, user.SecurityStamp ?? string.Empty),
            new(ClaimConstants.OwnerId, (user.TeamOwnerId ?? user.Id).ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        foreach (var role in roles)
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        foreach (var permission in permissions)
        {
            claims.Add(new Claim(ClaimConstants.Permission, permission));
        }

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtSettings.Secret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: _jwtSettings.Issuer,
            audience: _jwtSettings.Audience,
            claims: claims,
            expires: expiresAt,
            signingCredentials: credentials);

        var accessToken = new JwtSecurityTokenHandler().WriteToken(token);
        var refreshToken = await CreateRefreshTokenAsync(user.Id, ipAddress, persistent, cancellationToken);

        return new AuthResponseDto
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken.Token,
            ExpiresAt = expiresAt
        };
    }

    /// <inheritdoc />
    public async Task<AuthResponseDto> RefreshTokenAsync(
        string refreshToken,
        string? ipAddress,
        CancellationToken cancellationToken = default,
        bool persistent = false)
    {
        var storedToken = await _context.RefreshTokens
            .Include(rt => rt.User)
            .FirstOrDefaultAsync(rt => rt.Token == refreshToken, cancellationToken);

        if (storedToken is null || !storedToken.IsActive)
        {
            throw new UnauthorizedException("Refresh token không hợp lệ hoặc đã hết hạn.");
        }

        if (!storedToken.User.IsActive)
        {
            storedToken.RevokedAt = DateTime.UtcNow;
            _unitOfWork.RefreshTokens.Update(storedToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            throw new UnauthorizedException("Tài khoản đã bị vô hiệu hóa.");
        }

        storedToken.RevokedAt = DateTime.UtcNow;
        _unitOfWork.RefreshTokens.Update(storedToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return await GenerateTokensAsync(
            storedToken.User,
            ipAddress,
            cancellationToken,
            storedToken.Persistent || persistent);
    }

    /// <inheritdoc />
    public async Task RevokeRefreshTokenAsync(Guid userId, string refreshToken, CancellationToken cancellationToken = default)
    {
        var storedToken = await _unitOfWork.RefreshTokens.FirstOrDefaultAsync(
            rt => rt.UserId == userId && rt.Token == refreshToken,
            cancellationToken);

        if (storedToken is not null && storedToken.IsActive)
        {
            storedToken.RevokedAt = DateTime.UtcNow;
            _unitOfWork.RefreshTokens.Update(storedToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
    }

    /// <inheritdoc />
    public async Task RevokeAllRefreshTokensAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        await _context.RefreshTokens
            .Where(rt => rt.UserId == userId && rt.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(rt => rt.RevokedAt, now), cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetUserPermissionsAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var roleIds = await _context.UserRoles
            .Where(ur => ur.UserId == userId)
            .Select(ur => ur.RoleId)
            .ToListAsync(cancellationToken);

        if (roleIds.Count == 0)
        {
            return [];
        }

        var permissions = await _context.RolePermissions
            .Where(rp => roleIds.Contains(rp.RoleId))
            .Select(rp => rp.Permission.Code)
            .Distinct()
            .OrderBy(code => code)
            .ToListAsync(cancellationToken);

        return permissions;
    }

    private async Task<RefreshToken> CreateRefreshTokenAsync(
        Guid userId,
        string? ipAddress,
        bool persistent,
        CancellationToken cancellationToken)
    {
        var refreshToken = new RefreshToken
        {
            UserId = userId,
            Token = GenerateSecureToken(),
            ExpiresAt = ResolveRefreshExpiresAt(persistent),
            Persistent = persistent,
            CreatedByIp = ipAddress
        };

        await _unitOfWork.RefreshTokens.AddAsync(refreshToken, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return refreshToken;
    }

    private DateTime ResolveRefreshExpiresAt(bool persistent)
    {
        var days = persistent
            ? _jwtSettings.PersistentRefreshTokenExpirationDays
            : _jwtSettings.RefreshTokenExpirationDays;
        if (days < 1)
        {
            days = persistent ? 36500 : 30;
        }

        return DateTime.UtcNow.AddDays(days);
    }

    private static string GenerateSecureToken()
    {
        var randomBytes = RandomNumberGenerator.GetBytes(64);
        return Convert.ToBase64String(randomBytes);
    }
}
