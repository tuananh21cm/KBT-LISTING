namespace Application.Interfaces;

/// <summary>
/// Bunny CDN storage operations.
/// </summary>
public interface IBunnyStorageService
{
    /// <summary>
    /// Uploads file content to Bunny storage.
    /// </summary>
    Task UploadAsync(string storagePath, Stream content, string contentType, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a file from Bunny storage.
    /// </summary>
    Task DeleteAsync(string storagePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Builds the public CDN URL for a storage path.
    /// </summary>
    Task<string?> BuildPublicUrlAsync(string storagePath, CancellationToken cancellationToken = default);
}
