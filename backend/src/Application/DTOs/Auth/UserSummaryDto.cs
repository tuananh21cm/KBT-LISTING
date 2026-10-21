namespace Application.DTOs.Auth;

/// <summary>
/// Lightweight user summary for admin listings.
/// </summary>
public class UserSummaryDto
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
}
