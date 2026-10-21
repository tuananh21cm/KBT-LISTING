using Domain.Common;

namespace Domain.Entities;

/// <summary>
/// User-uploaded file stored in Bunny CDN.
/// </summary>
public class UserFile : BaseEntity
{
    /// <summary>
    /// File identifier.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Owner user identifier.
    /// </summary>
    public Guid OwnerId { get; set; }

    /// <summary>
    /// Original file name.
    /// </summary>
    public string OriginalFileName { get; set; } = string.Empty;

    /// <summary>
    /// Storage path within Bunny zone.
    /// </summary>
    public string StoragePath { get; set; } = string.Empty;

    /// <summary>
    /// Public CDN URL if available.
    /// </summary>
    public string? PublicUrl { get; set; }

    /// <summary>
    /// MIME content type.
    /// </summary>
    public string ContentType { get; set; } = string.Empty;

    /// <summary>
    /// File size in bytes.
    /// </summary>
    public long FileSizeBytes { get; set; }

    /// <summary>
    /// SHA256 hash (hex) for duplicate detection.
    /// </summary>
    public string FileHash { get; set; } = string.Empty;

    /// <summary>
    /// Hash algorithm used.
    /// </summary>
    public string HashAlgorithm { get; set; } = "SHA256";

    /// <summary>
    /// File metadata (JSON): dimensions, tags, alt text, etc.
    /// </summary>
    public string? Metadata { get; set; }

    /// <summary>
    /// Linked entity type (Product, Shop).
    /// </summary>
    public string? LinkedEntityType { get; set; }

    /// <summary>
    /// Linked entity identifier.
    /// </summary>
    public Guid? LinkedEntityId { get; set; }

    /// <summary>
    /// Soft delete flag.
    /// </summary>
    public bool IsDeleted { get; set; }

    /// <summary>
    /// Navigation to owner.
    /// </summary>
    public ApplicationUser Owner { get; set; } = null!;
}
