using Application.DTOs.ShippingTemplate;

namespace Application.Interfaces;

/// <summary>
/// Per-user shipping template management service.
/// </summary>
public interface IShippingTemplateService
{
    /// <summary>Lists templates owned by the user.</summary>
    Task<List<ShippingTemplateDto>> GetAllAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Gets a template by ID (must belong to user).</summary>
    Task<ShippingTemplateDto> GetByIdAsync(Guid userId, Guid id, CancellationToken cancellationToken = default);

    /// <summary>Creates a template for the user.</summary>
    Task<ShippingTemplateDto> CreateAsync(Guid userId, CreateShippingTemplateDto dto, CancellationToken cancellationToken = default);

    /// <summary>Updates a template owned by the user.</summary>
    Task<ShippingTemplateDto> UpdateAsync(Guid userId, Guid id, UpdateShippingTemplateDto dto, CancellationToken cancellationToken = default);

    /// <summary>Deletes a template owned by the user.</summary>
    Task DeleteAsync(Guid userId, Guid id, CancellationToken cancellationToken = default);
}
