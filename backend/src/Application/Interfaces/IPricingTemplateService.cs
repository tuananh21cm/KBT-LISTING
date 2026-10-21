using Application.DTOs.PricingTemplate;

namespace Application.Interfaces;

/// <summary>
/// Per-user pricing template management service.
/// </summary>
public interface IPricingTemplateService
{
    /// <summary>Lists templates owned by the user.</summary>
    Task<List<PricingTemplateDto>> GetAllAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Gets a template by ID (must belong to user).</summary>
    Task<PricingTemplateDto> GetByIdAsync(Guid userId, Guid id, CancellationToken cancellationToken = default);

    /// <summary>Creates a template for the user.</summary>
    Task<PricingTemplateDto> CreateAsync(Guid userId, CreatePricingTemplateDto dto, CancellationToken cancellationToken = default);

    /// <summary>Updates a template owned by the user.</summary>
    Task<PricingTemplateDto> UpdateAsync(Guid userId, Guid id, UpdatePricingTemplateDto dto, CancellationToken cancellationToken = default);

    /// <summary>Deletes a template owned by the user.</summary>
    Task DeleteAsync(Guid userId, Guid id, CancellationToken cancellationToken = default);
}
