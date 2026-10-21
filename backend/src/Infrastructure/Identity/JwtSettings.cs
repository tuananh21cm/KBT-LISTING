namespace Infrastructure.Identity;

/// <summary>
/// JWT configuration settings.
/// </summary>
public class JwtSettings
{
    /// <summary>
    /// Configuration section name.
    /// </summary>
    public const string SectionName = "Jwt";

    /// <summary>
    /// Signing secret key.
    /// </summary>
    public string Secret { get; set; } = string.Empty;

    /// <summary>
    /// Pepper for Fulfil extension PIN HMAC. Empty → fall back to <see cref="Secret"/>.
    /// Changing this value invalidates every stored PIN hash.
    /// </summary>
    public string PinPepper { get; set; } = string.Empty;

    /// <summary>
    /// Token issuer.
    /// </summary>
    public string Issuer { get; set; } = string.Empty;

    /// <summary>
    /// Token audience.
    /// </summary>
    public string Audience { get; set; } = string.Empty;

    /// <summary>
    /// Access token lifetime in minutes.
    /// </summary>
    public int AccessTokenExpirationMinutes { get; set; } = 15;

    /// <summary>
    /// Refresh token lifetime in days.
    /// </summary>
    public int RefreshTokenExpirationDays { get; set; } = 30;

    /// <summary>
    /// Refresh token lifetime in days when login/refresh sends <c>persistent: true</c>
    /// (TikTok Shop extension stay-signed-in until uninstall / logout).
    /// </summary>
    public int PersistentRefreshTokenExpirationDays { get; set; } = 36500;
}
