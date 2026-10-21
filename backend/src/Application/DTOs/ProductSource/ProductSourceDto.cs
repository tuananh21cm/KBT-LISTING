namespace Application.DTOs.ProductSource;

/// <summary>
/// Product source response DTO.
/// </summary>
public class ProductSourceDto
{
    /// <summary>Product source identifier.</summary>
    public Guid Id { get; set; }

    /// <summary>Unique source code.</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Display name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Logo image URL.</summary>
    public string? LogoUrl { get; set; }

    /// <summary>Host patterns (comma/newline separated) used to detect this source from a product URL.</summary>
    public string? Domains { get; set; }

    /// <summary>Description.</summary>
    public string? Description { get; set; }

    /// <summary>Active flag.</summary>
    public bool IsActive { get; set; }

    /// <summary>Created at (UTC).</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>Updated at (UTC).</summary>
    public DateTime? UpdatedAt { get; set; }
}
