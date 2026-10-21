using Application.DTOs.FourSeller;

namespace Application.Interfaces;

/// <summary>
/// Browse and delete 4Seller listings for the acting user.
/// </summary>
public interface IListingImportService
{
    /// <summary>
    /// Proxies listing page for the acting user's 4Seller cookie.
    /// </summary>
    Task<FourSellerListingPageDto> GetPageAsync(
        Guid userId,
        string platform,
        FourSellerListingPageRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes selected listings on 4Seller.
    /// </summary>
    Task DeleteAsync(
        Guid userId,
        string platform,
        FourSellerDeleteListingsRequest request,
        CancellationToken cancellationToken = default);
}
