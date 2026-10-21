using Application.DTOs.Auth;
using Domain.Entities;

namespace Application.Interfaces;

/// <summary>
/// Authentication service contract.
/// </summary>
public interface IAuthService
{
    /// <summary>
    /// Authenticates a user and issues tokens.
    /// </summary>
    Task<AuthResponseDto> LoginAsync(LoginRequestDto request, string? ipAddress, CancellationToken cancellationToken = default);

    /// <summary>
    /// Authenticates via Fulfil extension 4-digit PIN and issues tokens.
    /// Only active users with role Fulfil or Admin; throttled by IP on failure.
    /// </summary>
    Task<AuthResponseDto> LoginWithPinAsync(PinLoginRequestDto request, string? ipAddress, CancellationToken cancellationToken = default);

    /// <summary>
    /// Revokes the active refresh token.
    /// </summary>
    Task LogoutAsync(Guid userId, string refreshToken, CancellationToken cancellationToken = default);

    /// <summary>
    /// Rotates refresh token and issues a new access token.
    /// </summary>
    Task<AuthResponseDto> RefreshTokenAsync(RefreshTokenRequestDto request, string? ipAddress, CancellationToken cancellationToken = default);
}
