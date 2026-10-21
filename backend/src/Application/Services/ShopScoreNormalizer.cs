namespace Application.Services;

/// <summary>
/// Chuẩn hoá điểm Health Center trước khi ghi lên <c>Shops</c>.
/// TikTok đôi khi trả điểm ngoài thang hoặc thừa chữ số thập phân so với cột <c>numeric(3,1)</c>.
/// </summary>
public static class ShopScoreNormalizer
{
    /// <summary>Thang điểm Shop Performance Score.</summary>
    public const decimal MaxPerformanceScore = 5m;

    /// <summary>Trần AHR score của TikTok.</summary>
    public const int MaxAhrScore = 1000;

    /// <summary>Clamp về 0–5 và làm tròn 1 chữ số thập phân (khớp cột numeric(3,1)).</summary>
    public static decimal? NormalizePerformanceScore(decimal? value)
    {
        if (value is null)
        {
            return null;
        }

        var clamped = Math.Clamp(value.Value, 0m, MaxPerformanceScore);
        return Math.Round(clamped, 1, MidpointRounding.AwayFromZero);
    }

    /// <summary>Clamp AHR về 0–1000.</summary>
    public static int? NormalizeAhrScore(int? value)
    {
        if (value is null)
        {
            return null;
        }

        return Math.Clamp(value.Value, 0, MaxAhrScore);
    }
}
