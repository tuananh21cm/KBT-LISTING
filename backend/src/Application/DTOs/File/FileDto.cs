namespace Application.DTOs.File;

/// <summary>
/// User file response DTO.
/// </summary>
public class FileDto
{
    /// <summary>File identifier.</summary>
    public Guid Id { get; set; }

    /// <summary>Original file name.</summary>
    public string OriginalFileName { get; set; } = string.Empty;

    /// <summary>Storage path.</summary>
    public string StoragePath { get; set; } = string.Empty;

    /// <summary>Public CDN URL.</summary>
    public string? PublicUrl { get; set; }

    /// <summary>Content type.</summary>
    public string ContentType { get; set; } = string.Empty;

    /// <summary>File size in bytes.</summary>
    public long FileSizeBytes { get; set; }

    /// <summary>File hash.</summary>
    public string FileHash { get; set; } = string.Empty;

    /// <summary>Metadata JSON.</summary>
    public string? Metadata { get; set; }

    /// <summary>Linked entity type.</summary>
    public string? LinkedEntityType { get; set; }

    /// <summary>Linked entity ID.</summary>
    public Guid? LinkedEntityId { get; set; }

    /// <summary>Whether this was an existing duplicate.</summary>
    public bool IsDuplicate { get; set; }

    /// <summary>Created at (UTC).</summary>
    public DateTime CreatedAt { get; set; }
}
