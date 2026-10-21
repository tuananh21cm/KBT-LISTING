namespace Application.DTOs.Setting;

/// <summary>
/// System setting response DTO.
/// </summary>
public class SettingDto
{
    /// <summary>Setting identifier.</summary>
    public Guid Id { get; set; }

    /// <summary>Configuration group.</summary>
    public string Group { get; set; } = string.Empty;

    /// <summary>Unique setting key.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Display label.</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>Setting value.</summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>Value type (text, number, boolean, password).</summary>
    public string Type { get; set; } = "text";

    /// <summary>Optional description.</summary>
    public string? Description { get; set; }

    /// <summary>Created at (UTC).</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>Updated at (UTC).</summary>
    public DateTime? UpdatedAt { get; set; }
}
