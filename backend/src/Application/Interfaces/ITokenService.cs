using Application.DTOs.Auth;
using Domain.Entities;

namespace Application.Interfaces;

/// <summary>
/// JWT and refresh token service contract.
/// </summary>
public interface ITokenService
{
    /// <summary>
    /// Generates access and refresh tokens for a user.
    /// </summary>
    /// <param name="persistent">Long-lived refresh token (extension stay-signed-in).</param>
    Task<AuthResponseDto> GenerateTokensAsync(
        ApplicationUser user,
        string? ipAddress,
        CancellationToken cancellationToken = default,
        bool persistent = false);

    /// <summary>
    /// Rotates refresh token and issues a new access token.
    /// </summary>
    /// <param name="persistent">Keep or upgrade the session to a long-lived refresh token.</param>
    Task<AuthResponseDto> RefreshTokenAsync(
        string refreshToken,
        string? ipAddress,
        CancellationToken cancellationToken = default,
        bool persistent = false);

    /// <summary>
    /// Revokes a refresh token for a user.
    /// </summary>
    Task RevokeRefreshTokenAsync(Guid userId, string refreshToken, CancellationToken cancellationToken = default);

    /// <summary>
    /// Revokes all active refresh tokens for a user (all sessions).
    /// </summary>
    Task RevokeAllRefreshTokensAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets permission codes for a user.
    /// </summary>
    Task<IReadOnlyList<string>> GetUserPermissionsAsync(Guid userId, CancellationToken cancellationToken = default);
}
