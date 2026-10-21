namespace Application.DTOs.Platform;

/// <summary>
/// Create platform request DTO.
/// </summary>
public class CreatePlatformDto
{
    /// <summary>Unique platform code.</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Display name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Description.</summary>
    public string? Description { get; set; }

    /// <summary>Avatar / logo image URL.</summary>
    public string? AvatarUrl { get; set; }

    /// <summary>Active flag.</summary>
    public bool IsActive { get; set; } = true;
}
