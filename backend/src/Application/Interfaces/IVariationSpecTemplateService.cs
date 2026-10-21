using Application.DTOs.VariationSpecTemplate;

namespace Application.Interfaces;

/// <summary>
/// Variation spec template management service.
/// </summary>
public interface IVariationSpecTemplateService
{
    /// <summary>Lists templates, optionally filtered by spec type.</summary>
    Task<List<VariationSpecTemplateDto>> GetAllAsync(string? specType = null, CancellationToken cancellationToken = default);

    /// <summary>Lists active templates for product form pickers.</summary>
    Task<List<VariationSpecTemplateDto>> GetOptionsAsync(string? specType = null, CancellationToken cancellationToken = default);

    /// <summary>Gets a template by ID.</summary>
    Task<VariationSpecTemplateDto> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Creates a template.</summary>
    Task<VariationSpecTemplateDto> CreateAsync(CreateVariationSpecTemplateDto dto, CancellationToken cancellationToken = default);

    /// <summary>Updates a template.</summary>
    Task<VariationSpecTemplateDto> UpdateAsync(Guid id, UpdateVariationSpecTemplateDto dto, CancellationToken cancellationToken = default);

    /// <summary>Deletes a template.</summary>
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Reorders templates within a spec type.</summary>
    Task ReorderAsync(ReorderVariationSpecTemplatesDto dto, CancellationToken cancellationToken = default);
}
