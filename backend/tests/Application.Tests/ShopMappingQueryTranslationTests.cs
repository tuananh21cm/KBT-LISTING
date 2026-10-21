using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Application.Tests;

/// <summary>
/// The shop-mapping queries are exercised elsewhere on the InMemory provider, which evaluates
/// everything client-side and therefore accepts shapes Npgsql cannot translate. These tests build
/// the same shapes against the real provider and only generate SQL (no connection is opened).
/// </summary>
public class ShopMappingQueryTranslationTests : IDisposable
{
    private readonly ApplicationDbContext _db;

    public ShopMappingQueryTranslationTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=translation-only;Database=none;Username=none;Password=none")
            .Options;
        _db = new ApplicationDbContext(options);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public void RematchStalePairProbe_TranslatesToSql()
    {
        var fsIds = new List<long> { 241315, 241316 };

        var sql = _db.Orders
            .Where(o => o.FourSellerShopId.HasValue && fsIds.Contains(o.FourSellerShopId.Value))
            .GroupBy(o => new { o.FourSellerShopId, o.ShopId })
            .Select(g => new { g.Key.FourSellerShopId, g.Key.ShopId })
            .ToQueryString();

        Assert.Contains("GROUP BY", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MappingHealthGroups_TranslatesToSql()
    {
        var sql = _db.Orders
            .AsNoTracking()
            .Where(o => o.ShopId == null)
            .GroupBy(o => new { o.FourSellerShopId, o.OwnerId })
            .Select(g => new
            {
                g.Key.FourSellerShopId,
                g.Key.OwnerId,
                OrderCount = g.Count(),
                FirstOrderAt = g.Min(o => o.OrderCreateTime ?? o.CreatedAt),
                LastOrderAt = g.Max(o => o.OrderCreateTime ?? o.CreatedAt)
            })
            .OrderByDescending(g => g.OrderCount)
            .Take(50)
            .ToQueryString();

        Assert.Contains("COALESCE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("GROUP BY", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MappingHealthKnownFourSellerIds_TranslatesToSql()
    {
        var fsIds = new List<string> { "241315", "241316" };

        var sql = _db.Shops
            .AsNoTracking()
            .Where(s =>
                (s.FourSellerShopId != null && fsIds.Contains(s.FourSellerShopId))
                || (s.PreviousFourSellerShopId != null && fsIds.Contains(s.PreviousFourSellerShopId)))
            .Select(s => new { s.FourSellerShopId, s.PreviousFourSellerShopId })
            .ToQueryString();

        Assert.Contains("SELECT", sql, StringComparison.OrdinalIgnoreCase);
    }
}
