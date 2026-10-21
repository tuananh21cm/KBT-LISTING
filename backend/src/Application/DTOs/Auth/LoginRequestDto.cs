namespace Application.DTOs.Auth;

/// <summary>
/// Login request payload.
/// </summary>
public class LoginRequestDto
{
    /// <summary>
    /// Username.
    /// </summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// Password.
    /// </summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>
    /// When true, issues a long-lived refresh token (stay signed in until revoked).
    /// Used by the TikTok Shop extension; web clients omit this (default false = 30 days).
    /// </summary>
    public bool Persistent { get; set; }
}
