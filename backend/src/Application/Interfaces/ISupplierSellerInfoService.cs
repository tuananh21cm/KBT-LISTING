using Application.DTOs.Supplier;

namespace Application.Interfaces;

/// <summary>Read-only supplier shop (NCC) list / detail with revenue attribution.</summary>
public interface ISupplierSellerInfoService
{
    /// <summary>Paged supplier shops with period KPI columns.</summary>
    Task<SupplierSellerListPageDto> ListAsync(
        SupplierSellerListQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>Shop detail + KPI + paged linked source products.</summary>
    Task<SupplierSellerDetailDto> GetByIdAsync(
        Guid id,
        SupplierSellerListQuery query,
        CancellationToken cancellationToken = default);
}
