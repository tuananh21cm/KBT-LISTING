namespace Shared.Constants;

/// <summary>
/// JWT and claim constants.
/// </summary>
public static class ClaimConstants
{
    /// <summary>
    /// Permission claim type.
    /// </summary>
    public const string Permission = "permission";

    /// <summary>
    /// User identifier claim type.
    /// </summary>
    public const string UserId = "uid";

    /// <summary>
    /// ASP.NET Identity security stamp — invalidated on password change / account revoke.
    /// </summary>
    public const string SecurityStamp = "ss";

    /// <summary>
    /// Data-owner id: the team account a member belongs to (<c>ApplicationUser.TeamOwnerId</c>), or the user itself.
    /// Seller-scoped controllers filter by this, not by <see cref="UserId"/>.
    /// </summary>
    // Not "oid": JwtBearer's inbound claim map rewrites that to a Microsoft URI and FindFirst("oid") misses.
    public const string OwnerId = "owner";
}

/// <summary>
/// Default role names stored in database.
/// </summary>
public static class RoleNames
{
    /// <summary>Administrator role.</summary>
    public const string Admin = "Admin";

    /// <summary>Manager role.</summary>
    public const string Manager = "Manager";

    /// <summary>Leader role.</summary>
    public const string Leader = "Leader";

    /// <summary>Seller role.</summary>
    public const string Seller = "Seller";

    /// <summary>Fulfil role — purchase / đi đơn (extension KINHBAC FULFIL).</summary>
    public const string Fulfil = "Fulfil";

    /// <summary>All default roles.</summary>
    public static readonly string[] All = [Admin, Manager, Leader, Seller, Fulfil];
}

/// <summary>
/// Application identifiers for permission scoping.
/// </summary>
public static class ApplicationNames
{
    /// <summary>Seller web application.</summary>
    public const string SellerWeb = "seller-web";

    /// <summary>Admin web application.</summary>
    public const string AdminWeb = "admin-web";
}
