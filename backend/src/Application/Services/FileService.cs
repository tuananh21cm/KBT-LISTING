using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Application.DTOs.File;
using Application.Interfaces;
using Domain.Entities;
using Domain.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Shared.Constants;

namespace Application.Services;

/// <summary>
/// User file management service implementation.
/// </summary>
public class FileService : IFileService
{
    private const int DefaultMaxFileSizeMb = 20;

    private static readonly string[] AllowedContentTypePrefixes =
    [
        "image/",
        "video/",
        "application/pdf"
    ];

    private readonly IApplicationDbContext _context;
    private readonly IBunnyStorageService _bunnyStorage;
    private readonly ISettingProvider _settingProvider;

    /// <summary>
    /// Initializes the file service.
    /// </summary>
    public FileService(
        IApplicationDbContext context,
        IBunnyStorageService bunnyStorage,
        ISettingProvider settingProvider)
    {
        _context = context;
        _bunnyStorage = bunnyStorage;
        _settingProvider = settingProvider;
    }

    /// <inheritdoc />
    public async Task<List<FileDto>> GetAllAsync(Guid ownerId, CancellationToken cancellationToken = default)
    {
        var files = await _context.UserFiles
            .Where(f => f.OwnerId == ownerId && !f.IsDeleted)
            .OrderByDescending(f => f.CreatedAt)
            .ToListAsync(cancellationToken);

        var changed = false;
        var result = new List<FileDto>(files.Count);
        foreach (var file in files)
        {
            changed |= await EnsurePublicUrlAsync(file, cancellationToken);
            result.Add(MapToDto(file));
        }

        if (changed)
        {
            await _context.SaveChangesAsync(cancellationToken);
        }

        return result;
    }

    /// <inheritdoc />
    public async Task<FileDto> GetByIdAsync(Guid ownerId, Guid fileId, CancellationToken cancellationToken = default)
    {
        var file = await FindOwnedFileAsync(ownerId, fileId, cancellationToken);
        if (await EnsurePublicUrlAsync(file, cancellationToken))
        {
            await _context.SaveChangesAsync(cancellationToken);
        }

        return MapToDto(file);
    }

    /// <inheritdoc />
    public async Task<FileDto> UploadAsync(
        Guid ownerId,
        IFormFile file,
        string? linkedEntityType,
        Guid? linkedEntityId,
        string? metadata,
        CancellationToken cancellationToken = default)
    {
        ValidateFile(file, await GetMaxFileSizeBytesAsync(cancellationToken));

        await using var stream = file.OpenReadStream();
        var hash = await ComputeSha256HexAsync(stream, cancellationToken);
        stream.Position = 0;

        // Unique index is (OwnerId, FileHash) including soft-deleted rows.
        var existing = await _context.UserFiles.FirstOrDefaultAsync(
            f => f.OwnerId == ownerId && f.FileHash == hash,
            cancellationToken);

        if (existing is not null)
        {
            if (existing.IsDeleted)
            {
                // Soft-deleted earlier also removed Bunny object — re-upload & restore row.
                await _bunnyStorage.UploadAsync(existing.StoragePath, stream, file.ContentType, cancellationToken);
                existing.IsDeleted = false;
                existing.OriginalFileName = file.FileName;
                existing.ContentType = file.ContentType;
                existing.FileSizeBytes = file.Length;
                existing.Metadata = metadata;
                existing.LinkedEntityType = linkedEntityType;
                existing.LinkedEntityId = linkedEntityId;
                existing.UpdatedAt = DateTime.UtcNow;
                existing.PublicUrl =
                    await _bunnyStorage.BuildPublicUrlAsync(existing.StoragePath, cancellationToken)
                    ?? existing.PublicUrl;
                await _context.SaveChangesAsync(cancellationToken);
                return MapToDto(existing);
            }

            if (await EnsurePublicUrlAsync(existing, cancellationToken))
            {
                await _context.SaveChangesAsync(cancellationToken);
            }

            var duplicateDto = MapToDto(existing);
            duplicateDto.IsDuplicate = true;
            return duplicateDto;
        }

        var fileId = Guid.NewGuid();
        var sanitizedName = SanitizeFileName(file.FileName);
        var storagePath = $"{ownerId}/{DateTime.UtcNow:yyyy}/{DateTime.UtcNow:MM}/{fileId}_{sanitizedName}";

        await _bunnyStorage.UploadAsync(storagePath, stream, file.ContentType, cancellationToken);

        var userFile = new UserFile
        {
            Id = fileId,
            OwnerId = ownerId,
            OriginalFileName = file.FileName,
            StoragePath = storagePath,
            PublicUrl = await _bunnyStorage.BuildPublicUrlAsync(storagePath, cancellationToken),
            ContentType = file.ContentType,
            FileSizeBytes = file.Length,
            FileHash = hash,
            HashAlgorithm = "SHA256",
            Metadata = metadata,
            LinkedEntityType = linkedEntityType,
            LinkedEntityId = linkedEntityId
        };

        _context.UserFiles.Add(userFile);
        await _context.SaveChangesAsync(cancellationToken);

        return MapToDto(userFile);
    }

    /// <inheritdoc />
    public async Task<FileDto?> CheckDuplicateAsync(Guid ownerId, string fileHash, CancellationToken cancellationToken = default)
    {
        var existing = await _context.UserFiles
            .AsNoTracking()
            .FirstOrDefaultAsync(
                f => f.OwnerId == ownerId && f.FileHash == fileHash.ToLowerInvariant() && !f.IsDeleted,
                cancellationToken);

        return existing is null ? null : MapToDto(existing);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(Guid ownerId, Guid fileId, CancellationToken cancellationToken = default)
    {
        var file = await FindOwnedFileAsync(ownerId, fileId, cancellationToken);

        await _bunnyStorage.DeleteAsync(file.StoragePath, cancellationToken);

        file.IsDeleted = true;
        file.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);
    }

    private async Task<UserFile> FindOwnedFileAsync(Guid ownerId, Guid fileId, CancellationToken cancellationToken)
    {
        var file = await _context.UserFiles.FirstOrDefaultAsync(
            f => f.Id == fileId && f.OwnerId == ownerId && !f.IsDeleted,
            cancellationToken);

        if (file is null)
        {
            throw new NotFoundException("Không tìm thấy file.");
        }

        return file;
    }

    private async Task<bool> EnsurePublicUrlAsync(UserFile file, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(file.PublicUrl) || string.IsNullOrWhiteSpace(file.StoragePath))
        {
            return false;
        }

        var url = await _bunnyStorage.BuildPublicUrlAsync(file.StoragePath, cancellationToken);
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        file.PublicUrl = url;
        file.UpdatedAt = DateTime.UtcNow;
        return true;
    }

    private async Task<long> GetMaxFileSizeBytesAsync(CancellationToken cancellationToken)
    {
        var raw = await _settingProvider.GetValueAsync(BunnySettingKeys.MaxFileSizeMb, cancellationToken);
        if (int.TryParse(raw, out var mb) && mb > 0)
        {
            return mb * 1024L * 1024L;
        }

        return DefaultMaxFileSizeMb * 1024L * 1024L;
    }

    private static void ValidateFile(IFormFile file, long maxFileSizeBytes)
    {
        if (file.Length == 0)
        {
            throw new BusinessException("File không được rỗng.");
        }

        if (file.Length > maxFileSizeBytes)
        {
            var maxMb = maxFileSizeBytes / (1024 * 1024);
            throw new BusinessException($"File vượt quá giới hạn {maxMb}MB.");
        }

        if (!AllowedContentTypePrefixes.Any(p => file.ContentType.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
        {
            throw new BusinessException("Loại file không được hỗ trợ. Chỉ chấp nhận image, video hoặc PDF.");
        }
    }

    private static async Task<string> ComputeSha256HexAsync(Stream stream, CancellationToken cancellationToken)
    {
        var hashBytes = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    private static string SanitizeFileName(string fileName)
    {
        var name = Path.GetFileName(fileName);
        name = Regex.Replace(name, @"[^\w\.\-]", "_");
        return string.IsNullOrWhiteSpace(name) ? "file" : name;
    }

    private static FileDto MapToDto(UserFile file) => new()
    {
        Id = file.Id,
        OriginalFileName = file.OriginalFileName,
        StoragePath = file.StoragePath,
        PublicUrl = file.PublicUrl,
        ContentType = file.ContentType,
        FileSizeBytes = file.FileSizeBytes,
        FileHash = file.FileHash,
        Metadata = file.Metadata,
        LinkedEntityType = file.LinkedEntityType,
        LinkedEntityId = file.LinkedEntityId,
        IsDuplicate = false,
        CreatedAt = file.CreatedAt
    };
}
