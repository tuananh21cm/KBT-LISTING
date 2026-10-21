namespace Application.DTOs.Auth;

/// <summary>
/// Admin create user request DTO.
/// </summary>
public class CreateAdminUserDto
{
    /// <summary>Username.</summary>
    public string UserName { get; set; } = string.Empty;

    /// <summary>Email address.</summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>Display name.</summary>
    public string FullName { get; set; } = string.Empty;

    /// <summary>Initial password.</summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>Role names to assign.</summary>
    public List<string> Roles { get; set; } = [];

    /// <summary>Optional per-user 4Seller session cookie.</summary>
    public string? FourSellerCookie { get; set; }

    /// <summary>Optional per-seller sticky egress proxy for 4Seller (<c>scheme://user:pass@host:port</c>).</summary>
    public string? FourSellerProxy { get; set; }

    /// <summary>
    /// Optional 4-digit Fulfil extension PIN. Null/omit = none; must be unique system-wide.
    /// </summary>
    public string? FulfilPin { get; set; }
}
