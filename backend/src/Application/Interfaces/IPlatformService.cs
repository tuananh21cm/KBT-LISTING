using Application.DTOs.Platform;

namespace Application.Interfaces;

/// <summary>
/// Platform management service.
/// </summary>
public interface IPlatformService
{
    /// <summary>Lists all platforms.</summary>
    Task<List<PlatformDto>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Gets a platform by ID.</summary>
    Task<PlatformDto> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Lists active platforms for dropdowns.</summary>
    Task<List<PlatformOptionDto>> GetOptionsAsync(CancellationToken cancellationToken = default);

    /// <summary>Creates a platform.</summary>
    Task<PlatformDto> CreateAsync(CreatePlatformDto dto, CancellationToken cancellationToken = default);

    /// <summary>Updates a platform.</summary>
    Task<PlatformDto> UpdateAsync(Guid id, UpdatePlatformDto dto, CancellationToken cancellationToken = default);

    /// <summary>Deletes a platform.</summary>
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
