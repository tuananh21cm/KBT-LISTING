namespace Application.DTOs.Platform;

/// <summary>
/// Lightweight platform option for dropdowns.
/// </summary>
public class PlatformOptionDto
{
    /// <summary>Platform identifier.</summary>
    public Guid Id { get; set; }

    /// <summary>Platform code.</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Display name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Avatar / logo image URL.</summary>
    public string? AvatarUrl { get; set; }
}
