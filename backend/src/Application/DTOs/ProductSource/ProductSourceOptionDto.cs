namespace Application.DTOs.ProductSource;

/// <summary>
/// Lightweight product source option for dropdowns.
/// </summary>
public class ProductSourceOptionDto
{
    /// <summary>Product source identifier.</summary>
    public Guid Id { get; set; }

    /// <summary>Source code.</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Display name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Logo image URL.</summary>
    public string? LogoUrl { get; set; }

    /// <summary>Host patterns (comma/newline separated) used by the extension to detect this source.</summary>
    public string? Domains { get; set; }
}
