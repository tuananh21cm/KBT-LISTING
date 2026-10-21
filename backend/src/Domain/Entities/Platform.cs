using Domain.Common;

namespace Domain.Entities;

/// <summary>
/// E-commerce platform (TikTok, Amazon, Shopify, etc.).
/// </summary>
public class Platform : BaseEntity
{
    /// <summary>
    /// Platform identifier.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Unique platform code (e.g. TikTok, Amazon).
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// Display name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Optional description.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Optional avatar / logo image URL.
    /// </summary>
    public string? AvatarUrl { get; set; }

    /// <summary>
    /// Whether the platform is available for new shops.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Shops on this platform.
    /// </summary>
    public ICollection<Shop> Shops { get; set; } = [];
}
