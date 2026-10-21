namespace Application.DTOs.ProductSource;

/// <summary>
/// Create product source request DTO.
/// </summary>
public class CreateProductSourceDto
{
    /// <summary>Unique source code.</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Display name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Logo image URL.</summary>
    public string? LogoUrl { get; set; }

    /// <summary>Host patterns (comma/newline separated), e.g. "shein.com,us.shein.com".</summary>
    public string? Domains { get; set; }

    /// <summary>Description.</summary>
    public string? Description { get; set; }

    /// <summary>Active flag.</summary>
    public bool IsActive { get; set; } = true;
}
