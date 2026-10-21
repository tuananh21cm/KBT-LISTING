namespace Application.DTOs.Auth;

/// <summary>
/// Optional filter for bulk 4Seller login checks. Empty/null = all users with a stored cookie.
/// </summary>
public class AdminFourSellerLoginCheckRequest
{
    /// <summary>
    /// When set, only these user IDs are checked (still skipped if they have no cookie).
    /// </summary>
    public List<Guid>? UserIds { get; set; }
}
