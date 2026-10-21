using Application.Services;
using Xunit;

namespace Application.Tests;

/// <summary>
/// Khoá cách chuẩn hoá điểm Health Center: TikTok trả điểm lệch thang hoặc thừa chữ số thập phân
/// vẫn phải ghi được vào cột numeric(3,1), và thiếu điểm thì giữ nguyên giá trị cũ (null).
/// </summary>
public class ShopScoreNormalizerTests
{
    [Fact]
    public void PerformanceScore_GiuNguyenKhiTrongThang()
    {
        Assert.Equal(3.9m, ShopScoreNormalizer.NormalizePerformanceScore(3.9m));
    }

    [Fact]
    public void PerformanceScore_LamTronMotChuSo()
    {
        Assert.Equal(3.9m, ShopScoreNormalizer.NormalizePerformanceScore(3.85m));
        Assert.Equal(3.6m, ShopScoreNormalizer.NormalizePerformanceScore(3.649m));
    }

    [Fact]
    public void PerformanceScore_ClampNgoaiThang()
    {
        Assert.Equal(5m, ShopScoreNormalizer.NormalizePerformanceScore(7.4m));
        Assert.Equal(0m, ShopScoreNormalizer.NormalizePerformanceScore(-1.2m));
    }

    [Fact]
    public void PerformanceScore_NullTraNull()
    {
        Assert.Null(ShopScoreNormalizer.NormalizePerformanceScore(null));
    }

    [Fact]
    public void AhrScore_ClampVaGiuNull()
    {
        Assert.Equal(190, ShopScoreNormalizer.NormalizeAhrScore(190));
        Assert.Equal(0, ShopScoreNormalizer.NormalizeAhrScore(-5));
        Assert.Equal(ShopScoreNormalizer.MaxAhrScore, ShopScoreNormalizer.NormalizeAhrScore(99_999));
        Assert.Null(ShopScoreNormalizer.NormalizeAhrScore(null));
    }
}
