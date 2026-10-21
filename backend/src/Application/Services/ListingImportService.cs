using Application.DTOs.FourSeller;
using Application.Interfaces;
using Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Application.Services;

/// <summary>
/// Proxies 4Seller listing browse/delete for the acting user.
/// </summary>
public class ListingImportService : IListingImportService
{
    private readonly IFourSellerApiService _fourSellerApiService;
    private readonly IApplicationDbContext _context;

    /// <summary>
    /// Initializes the listing import service.
    /// </summary>
    public ListingImportService(
        IFourSellerApiService fourSellerApiService,
        IApplicationDbContext context)
    {
        _fourSellerApiService = fourSellerApiService;
        _context = context;
    }

    /// <inheritdoc />
    public async Task<FourSellerListingPageDto> GetPageAsync(
        Guid userId,
        string platform,
        FourSellerListingPageRequest request,
        CancellationToken cancellationToken = default)
    {
        var platformCode = await ResolveAllowedListingPlatformAsync(platform, cancellationToken);
        return await _fourSellerApiService.GetListingPageAsync(
            userId,
            platformCode,
            request,
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(
        Guid userId,
        string platform,
        FourSellerDeleteListingsRequest request,
        CancellationToken cancellationToken = default)
    {
        var listingIds = (request.ListingIds ?? [])
            .Where(id => id > 0)
            .Distinct()
            .ToList();

        if (listingIds.Count == 0)
        {
            throw new BusinessException("Chọn ít nhất một listing để xóa.");
        }

        var platformCode = await ResolveAllowedListingPlatformAsync(platform, cancellationToken);
        await _fourSellerApiService.DeleteListingsAsync(
            userId,
            platformCode,
            listingIds,
            cancellationToken);
    }

    /// <summary>
    /// Ensures <paramref name="platform"/> matches an active system Platform; returns 4Seller path slug (lowercase code).
    /// </summary>
    private async Task<string> ResolveAllowedListingPlatformAsync(
        string platform,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(platform))
        {
            throw new BusinessException("Chọn nền tảng (platform) trước khi tải listing.");
        }

        var requested = platform.Trim();
        var platforms = await _context.Platforms
            .AsNoTracking()
            .Where(p => p.IsActive)
            .Select(p => p.Code)
            .ToListAsync(cancellationToken);

        var matched = platforms.FirstOrDefault(code =>
            string.Equals(code, requested, StringComparison.OrdinalIgnoreCase)
            || string.Equals(code.ToLowerInvariant(), requested.ToLowerInvariant(), StringComparison.Ordinal));

        if (matched is null)
        {
            throw new BusinessException(
                $"Nền tảng '{requested}' không được phép hoặc chưa kích hoạt. Chỉ dùng platform trong Quản lý nền tảng.");
        }

        return matched.Trim().ToLowerInvariant();
    }
}
