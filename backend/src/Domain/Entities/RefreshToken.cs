namespace Domain.Entities;

/// <summary>
/// Persisted refresh token for JWT rotation.
/// </summary>
public class RefreshToken
{
    /// <summary>
    /// Token identifier.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Owner user identifier.
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// Token value.
    /// </summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>
    /// Expiration timestamp (UTC).
    /// </summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>
    /// Long-lived session (extension stay-signed-in). Inherited when the token is rotated.
    /// </summary>
    public bool Persistent { get; set; }

    /// <summary>
    /// Revocation timestamp (UTC).
    /// </summary>
    public DateTime? RevokedAt { get; set; }

    /// <summary>
    /// Client IP at creation.
    /// </summary>
    public string? CreatedByIp { get; set; }

    /// <summary>
    /// Creation timestamp (UTC).
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Navigation to user.
    /// </summary>
    public ApplicationUser User { get; set; } = null!;

    /// <summary>
    /// Whether the token is currently active.
    /// </summary>
    public bool IsActive => RevokedAt is null && ExpiresAt > DateTime.UtcNow;
}
