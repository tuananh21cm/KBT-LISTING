namespace Application.DTOs.Auth;

/// <summary>
/// Admin update user request DTO.
/// </summary>
public class UpdateAdminUserDto
{
    /// <summary>Email address.</summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>Display name.</summary>
    public string FullName { get; set; } = string.Empty;

    /// <summary>Active flag.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>Role names to assign.</summary>
    public List<string> Roles { get; set; } = [];

    /// <summary>
    /// Per-user 4Seller session cookie. Null = keep existing; empty string = clear; non-empty = replace.
    /// </summary>
    public string? FourSellerCookie { get; set; }

    /// <summary>
    /// Per-seller sticky egress proxy for 4Seller. Null = keep existing; empty string = clear; non-empty = replace.
    /// Format: <c>scheme://user:pass@host:port</c> (scheme optional, defaults to http CONNECT).
    /// </summary>
    public string? FourSellerProxy { get; set; }

    /// <summary>
    /// 4-digit Fulfil extension PIN. Null = keep existing; empty string = clear; 4 digits = replace.
    /// Must be unique system-wide when set.
    /// </summary>
    public string? FulfilPin { get; set; }
}
