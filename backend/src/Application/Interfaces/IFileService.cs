using Application.DTOs.File;
using Microsoft.AspNetCore.Http;

namespace Application.Interfaces;

/// <summary>
/// User file management service.
/// </summary>
public interface IFileService
{
    /// <summary>Lists files for the current user.</summary>
    Task<List<FileDto>> GetAllAsync(Guid ownerId, CancellationToken cancellationToken = default);

    /// <summary>Gets a file by ID.</summary>
    Task<FileDto> GetByIdAsync(Guid ownerId, Guid fileId, CancellationToken cancellationToken = default);

    /// <summary>Uploads a file with duplicate detection.</summary>
    Task<FileDto> UploadAsync(Guid ownerId, IFormFile file, string? linkedEntityType, Guid? linkedEntityId, string? metadata, CancellationToken cancellationToken = default);

    /// <summary>Checks if a file hash already exists.</summary>
    Task<FileDto?> CheckDuplicateAsync(Guid ownerId, string fileHash, CancellationToken cancellationToken = default);

    /// <summary>Soft-deletes a file and removes from Bunny storage.</summary>
    Task DeleteAsync(Guid ownerId, Guid fileId, CancellationToken cancellationToken = default);
}
