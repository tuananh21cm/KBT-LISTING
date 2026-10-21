using Application.DTOs.Setting;

namespace Application.Interfaces;

/// <summary>
/// System settings management service.
/// </summary>
public interface ISettingService
{
    /// <summary>Lists all settings.</summary>
    Task<List<SettingDto>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Gets a setting by ID.</summary>
    Task<SettingDto> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Creates a setting.</summary>
    Task<SettingDto> CreateAsync(CreateSettingDto dto, CancellationToken cancellationToken = default);

    /// <summary>Updates a setting.</summary>
    Task<SettingDto> UpdateAsync(Guid id, UpdateSettingDto dto, CancellationToken cancellationToken = default);

    /// <summary>Deletes a setting.</summary>
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
