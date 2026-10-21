namespace Application.DTOs.Auth;

/// <summary>
/// Authentication token response.
/// </summary>
public class AuthResponseDto
{
    /// <summary>
    /// JWT access token.
    /// </summary>
    public string AccessToken { get; set; } = string.Empty;

    /// <summary>
    /// Refresh token.
    /// </summary>
    public string RefreshToken { get; set; } = string.Empty;

    /// <summary>
    /// Access token expiration (UTC).
    /// </summary>
    public DateTime ExpiresAt { get; set; }
}
