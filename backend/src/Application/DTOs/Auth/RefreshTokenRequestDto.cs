namespace Application.DTOs.Auth;

/// <summary>
/// Refresh token request payload.
/// </summary>
public class RefreshTokenRequestDto
{
    /// <summary>
    /// Refresh token value.
    /// </summary>
    public string RefreshToken { get; set; } = string.Empty;

    /// <summary>
    /// When true (or when the stored token is already persistent), the rotated
    /// refresh token stays long-lived. TikTok Shop extension always sends true.
    /// </summary>
    public bool Persistent { get; set; }
}
