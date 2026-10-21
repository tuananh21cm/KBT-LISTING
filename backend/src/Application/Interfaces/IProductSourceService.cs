using Application.DTOs.ProductSource;

namespace Application.Interfaces;

/// <summary>
/// Product source management service.
/// </summary>
public interface IProductSourceService
{
    /// <summary>Lists all product sources.</summary>
    Task<List<ProductSourceDto>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Gets a product source by ID.</summary>
    Task<ProductSourceDto> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Lists active product sources for dropdowns.</summary>
    Task<List<ProductSourceOptionDto>> GetOptionsAsync(CancellationToken cancellationToken = default);

    /// <summary>Creates a product source.</summary>
    Task<ProductSourceDto> CreateAsync(CreateProductSourceDto dto, CancellationToken cancellationToken = default);

    /// <summary>Updates a product source.</summary>
    Task<ProductSourceDto> UpdateAsync(Guid id, UpdateProductSourceDto dto, CancellationToken cancellationToken = default);

    /// <summary>Deletes a product source.</summary>
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
