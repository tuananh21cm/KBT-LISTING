namespace Application.DTOs.Auth;

/// <summary>
/// Current user profile response.
/// </summary>
public class CurrentUserDto
{
    /// <summary>
    /// User identifier.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Username.
    /// </summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// Email address.
    /// </summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// Full name.
    /// </summary>
    public string FullName { get; set; } = string.Empty;

    /// <summary>
    /// Assigned roles.
    /// </summary>
    public IReadOnlyList<string> Roles { get; set; } = [];

    /// <summary>
    /// Effective permissions.
    /// </summary>
    public IReadOnlyList<string> Permissions { get; set; } = [];

    /// <summary>
    /// Whether the user has a stored 4Seller session cookie (value is never returned).
    /// </summary>
    public bool HasFourSellerCookie { get; set; }

    /// <summary>
    /// Persisted 4Seller login status: <c>none</c> / <c>unchecked</c> / <c>logged_in</c> / <c>expired</c>.
    /// </summary>
    public string FourSellerCookieLoginStatus { get; set; } = "none";

    /// <summary>
    /// UI appearance preference: <c>light</c> or <c>dark</c>.
    /// </summary>
    public string Theme { get; set; } = "light";

    /// <summary>Username of the team account this user works under (null = owns its own data).</summary>
    public string? TeamOwnerUserName { get; set; }
}
