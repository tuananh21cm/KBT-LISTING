using Application.DTOs.Shop;

namespace Application.Interfaces;

/// <summary>Read-only age-bracket presets for shop catalog profile.</summary>
public interface IAgeRangeService
{
    /// <summary>Lists active age ranges ordered by <c>SortOrder</c>.</summary>
    Task<IReadOnlyList<AgeRangeDto>> ListActiveAsync(CancellationToken cancellationToken = default);
}
