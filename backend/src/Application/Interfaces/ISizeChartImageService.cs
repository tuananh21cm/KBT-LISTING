using Application.DTOs.FourSeller;

namespace Application.Interfaces;

/// <summary>
/// Orchestrates Grok size-chart generation and 4Seller COS upload for a listing.
/// </summary>
public interface ISizeChartImageService
{
    /// <summary>
    /// Builds prompt from supplier size guide, generates image via x.ai, uploads to 4Seller COS.
    /// </summary>
    Task<(FourSellerCosUploadResultDto Result, string? Warning)> GenerateAndUploadAsync(
        Guid listingId,
        CancellationToken cancellationToken = default);
}
