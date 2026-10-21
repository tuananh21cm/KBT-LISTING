using Domain.Common;

namespace Domain.Entities;

/// <summary>
/// System configuration setting (key-value with type and group).
/// </summary>
public class Setting : BaseEntity
{
    /// <summary>
    /// Setting identifier.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Configuration group (e.g. Bunny Config, Storage).
    /// </summary>
    public string Group { get; set; } = string.Empty;

    /// <summary>
    /// Unique setting key within the system.
    /// </summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// Display label for admin UI.
    /// </summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>
    /// Setting value stored as string.
    /// </summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>
    /// Value type: text, number, boolean, password.
    /// </summary>
    public string Type { get; set; } = "text";

    /// <summary>
    /// Optional description or help text.
    /// </summary>
    public string? Description { get; set; }
}
