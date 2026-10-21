namespace Application.DTOs.Platform;

/// <summary>
/// Platform response DTO.
/// </summary>
public class PlatformDto
{
    /// <summary>Platform identifier.</summary>
    public Guid Id { get; set; }

    /// <summary>Unique platform code.</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Display name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Description.</summary>
    public string? Description { get; set; }

    /// <summary>Avatar / logo image URL.</summary>
    public string? AvatarUrl { get; set; }

    /// <summary>Active flag.</summary>
    public bool IsActive { get; set; }

    /// <summary>Number of shops using this platform.</summary>
    public int ShopCount { get; set; }

    /// <summary>Created at (UTC).</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>Updated at (UTC).</summary>
    public DateTime? UpdatedAt { get; set; }
}
