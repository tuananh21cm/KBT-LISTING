using Domain.Common;

namespace Domain.Entities;

/// <summary>
/// Product supply source (Shein, Temu, CJDropshipping, etc.).
/// </summary>
public class ProductSource : BaseEntity
{
    /// <summary>
    /// Product source identifier.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Unique source code (e.g. SHEIN, TEMU, CJ).
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// Display name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Logo image URL.
    /// </summary>
    public string? LogoUrl { get; set; }

    /// <summary>
    /// Host patterns for this source, separated by comma or newline (e.g. "shein.com,us.shein.com").
    /// Used by the browser extension to detect which source a product page belongs to.
    /// </summary>
    public string? Domains { get; set; }

    /// <summary>
    /// Optional description.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Whether the source is available for new products.
    /// </summary>
    public bool IsActive { get; set; } = true;

}
