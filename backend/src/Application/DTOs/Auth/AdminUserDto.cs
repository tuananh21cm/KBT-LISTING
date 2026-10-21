namespace Application.DTOs.Auth;

/// <summary>
/// Admin user detail response DTO.
/// </summary>
public class AdminUserDto
{
    /// <summary>User identifier.</summary>
    public Guid Id { get; set; }

    /// <summary>Username.</summary>
    public string UserName { get; set; } = string.Empty;

    /// <summary>Display name.</summary>
    public string FullName { get; set; } = string.Empty;

    /// <summary>Email address.</summary>
    public string? Email { get; set; }

    /// <summary>Active flag.</summary>
    public bool IsActive { get; set; }

    /// <summary>Assigned role names.</summary>
    public List<string> Roles { get; set; } = [];

    /// <summary>Created at (UTC).</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>Updated at (UTC).</summary>
    public DateTime? UpdatedAt { get; set; }

    /// <summary>Whether the user has a 4Seller session cookie configured.</summary>
    public bool HasFourSellerCookie { get; set; }

    /// <summary>
    /// Persisted 4Seller login status: <c>none</c> / <c>unchecked</c> / <c>logged_in</c> / <c>expired</c>.
    /// </summary>
    public string FourSellerCookieLoginStatus { get; set; } = "none";

    /// <summary>Per-seller sticky egress proxy for 4Seller (null when not configured).</summary>
    public string? FourSellerProxy { get; set; }

    /// <summary>Whether the user has a Fulfil extension PIN configured (write-only; value never returned).</summary>
    public bool HasFulfilPin { get; set; }

    /// <summary>Username of the team account this user works under (CRM "tổ"); null = owns its own data.</summary>
    public string? TeamOwnerUserName { get; set; }
}
