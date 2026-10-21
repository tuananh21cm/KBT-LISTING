using Application.DTOs.Shop;
using Application.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Application.Services;

/// <summary>Lists seeded <see cref="Domain.Entities.AgeRange"/> presets.</summary>
public class AgeRangeService : IAgeRangeService
{
    private readonly IApplicationDbContext _db;

    /// <summary>Creates the service.</summary>
    public AgeRangeService(IApplicationDbContext db)
    {
        _db = db;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AgeRangeDto>> ListActiveAsync(
        CancellationToken cancellationToken = default)
    {
        return await _db.AgeRanges
            .AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.SortOrder)
            .Select(x => new AgeRangeDto
            {
                Id = x.Id,
                Code = x.Code,
                Label = x.Label,
                AgeMin = x.AgeMin,
                AgeMax = x.AgeMax,
                PainPoint = x.PainPoint,
                Desire = x.Desire,
                ProductGroups = x.ProductGroups,
                SortOrder = x.SortOrder,
            })
            .ToListAsync(cancellationToken);
    }
}
