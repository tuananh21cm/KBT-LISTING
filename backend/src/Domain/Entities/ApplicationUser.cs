using Microsoft.AspNetCore.Identity;

namespace Domain.Entities;

/// <summary>
/// Platform user account shared across all applications.
/// </summary>
public class ApplicationUser : IdentityUser<Guid>
{
    /// <summary>
    /// Display name.
    /// </summary>
    public string FullName { get; set; } = string.Empty;

    /// <summary>
    /// Account active flag.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Creation timestamp (UTC).
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Last update timestamp (UTC).
    /// </summary>
    public DateTime? UpdatedAt { get; set; }

    /// <summary>
    /// Per-user 4Seller HTTP session cookie for API calls.
    /// </summary>
    public string? FourSellerCookie { get; set; }

    /// <summary>
    /// Last known 4Seller login status for the stored cookie
    /// (<c>none</c> / <c>unchecked</c> / <c>logged_in</c> / <c>expired</c>).
    /// </summary>
    public string FourSellerCookieLoginStatus { get; set; } = "none";

    /// <summary>
    /// Per-seller sticky egress proxy for 4Seller calls (survives EdgeOne 567 IP blocks).
    /// Formats: <c>ip:port</c>, <c>ip:port:user:pass</c>, or <c>[scheme://][user:pass@]host:port</c>.
    /// Null = use pool/direct.
    /// </summary>
    public string? FourSellerProxy { get; set; }

    /// <summary>
    /// UI appearance preference: <c>light</c> or <c>dark</c> (synced across devices).
    /// </summary>
    public string Theme { get; set; } = "light";

    /// <summary>
    /// HMAC-SHA256 hex of the 4-digit Fulfil extension PIN (write-only; null = no PIN).
    /// Unique when set so PIN-only login can resolve a single user.
    /// </summary>
    public string? FulfilPinHash { get; set; }

    /// <summary>
    /// Navigation to refresh tokens.
    /// </summary>
    public ICollection<RefreshToken> RefreshTokens { get; set; } = [];
}
