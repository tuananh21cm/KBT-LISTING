using Application.Services;
using Shared.Helpers;
using Xunit;

namespace Application.Tests;

/// <summary>
/// Khoá hành vi của snapshot Compass: ngày dedupe theo giờ Việt Nam, khoảng last7days theo giờ
/// shop, và cách tính % so kỳ trước khi TikTok không gửi ratio.
/// </summary>
public class ListingAnalyticsMetricsTests
{
    [Fact]
    public void SnapshotDate_dung_ngay_Viet_Nam_khong_phai_UTC()
    {
        // 22/08 18:00 UTC = 23/08 01:00 giờ VN → snapshot thuộc ngày 23.
        var utc = new DateTime(2026, 8, 22, 18, 0, 0, DateTimeKind.Utc);

        Assert.Equal(new DateOnly(2026, 8, 23), ListingAnalyticsMetrics.SnapshotDateFor(utc));
    }

    [Fact]
    public void SnapshotDate_giu_ngay_khi_con_trong_ngay_Viet_Nam()
    {
        var utc = new DateTime(2026, 8, 22, 3, 0, 0, DateTimeKind.Utc);

        Assert.Equal(new DateOnly(2026, 8, 22), ListingAnalyticsMetrics.SnapshotDateFor(utc));
    }

    [Fact]
    public void PacificToday_lui_mot_ngay_khi_UTC_vua_sang_ngay_moi()
    {
        // 23/08 04:00 UTC vẫn là 22/08 ở Pacific — Compass tính theo giờ shop.
        var utc = new DateTime(2026, 8, 23, 4, 0, 0, DateTimeKind.Utc);

        Assert.Equal(new DateOnly(2026, 8, 22), ListingAnalyticsMetrics.PacificToday(utc));
        Assert.Equal(
            DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utc, AppTimeZones.Pacific)),
            ListingAnalyticsMetrics.PacificToday(utc));
    }

    [Fact]
    public void ResolveRange_mac_dinh_last7days_theo_gio_shop()
    {
        var pacificToday = new DateOnly(2026, 8, 22);

        var (start, end) = ListingAnalyticsMetrics.ResolveRange(null, null, pacificToday);

        Assert.Equal(new DateOnly(2026, 8, 15), start);
        Assert.Equal(pacificToday, end);
    }

    [Fact]
    public void ResolveRange_giu_khoang_extension_gui_len()
    {
        var (start, end) = ListingAnalyticsMetrics.ResolveRange(
            new DateOnly(2026, 8, 1),
            new DateOnly(2026, 8, 7),
            new DateOnly(2026, 8, 22));

        Assert.Equal(new DateOnly(2026, 8, 1), start);
        Assert.Equal(new DateOnly(2026, 8, 7), end);
    }

    [Fact]
    public void ResolveRange_khoang_dao_nguoc_thu_ve_mot_ngay()
    {
        var (start, end) = ListingAnalyticsMetrics.ResolveRange(
            new DateOnly(2026, 8, 20),
            new DateOnly(2026, 8, 10),
            new DateOnly(2026, 8, 22));

        Assert.Equal(new DateOnly(2026, 8, 10), start);
        Assert.Equal(new DateOnly(2026, 8, 10), end);
    }

    [Fact]
    public void ResolveRange_thieu_start_tinh_tu_end_extension_gui()
    {
        var (start, end) = ListingAnalyticsMetrics.ResolveRange(
            null,
            new DateOnly(2026, 8, 10),
            new DateOnly(2026, 8, 22));

        Assert.Equal(new DateOnly(2026, 8, 3), start);
        Assert.Equal(new DateOnly(2026, 8, 10), end);
    }

    [Fact]
    public void ComparisonSnapshotDate_lui_7_ngay_VN_de_so_ky_truoc_cung_do_dai()
    {
        var day = new DateOnly(2026, 8, 23);

        Assert.Equal(new DateOnly(2026, 8, 16), ListingAnalyticsMetrics.ComparisonSnapshotDate(day));
    }

    [Theory]
    [InlineData(120, 100, 0.2)]
    [InlineData(80, 100, -0.2)]
    [InlineData(0, 100, -1)]
    [InlineData(0, 0, 0)]
    public void Delta_tra_phan_so_so_ky_truoc(decimal current, decimal previous, decimal expected)
    {
        Assert.Equal(expected, ListingAnalyticsMetrics.Delta(current, previous));
    }

    [Fact]
    public void Delta_null_khi_thieu_du_lieu_hoac_moc_bang_0()
    {
        Assert.Null(ListingAnalyticsMetrics.Delta(null, 100m));
        Assert.Null(ListingAnalyticsMetrics.Delta(100m, null));
        // Từ 0 lên 10 không có % nào có nghĩa — để trống thay vì hiện +∞.
        Assert.Null(ListingAnalyticsMetrics.Delta(10m, 0m));
    }

    [Fact]
    public void Delta_lam_tron_6_chu_so_va_khong_vuot_cot_numeric()
    {
        Assert.Equal(0.333333m, ListingAnalyticsMetrics.Delta(4m, 3m));
        Assert.Equal(ListingAnalyticsMetrics.MaxRatio, ListingAnalyticsMetrics.Delta(1_000_000_000m, 1m));
    }

    [Fact]
    public void CtrFrom_null_khi_khong_co_impression()
    {
        Assert.Null(ListingAnalyticsMetrics.CtrFrom(5, 0));
        Assert.Equal(0.025m, ListingAnalyticsMetrics.CtrFrom(25, 1000));
    }

    [Fact]
    public void NormalizeMoney_khong_am_va_khong_vuot_cot()
    {
        Assert.Equal(0m, ListingAnalyticsMetrics.NormalizeMoney(null));
        Assert.Equal(0m, ListingAnalyticsMetrics.NormalizeMoney(-5m));
        Assert.Equal(1234.57m, ListingAnalyticsMetrics.NormalizeMoney(1234.567m));
        Assert.Equal(
            ListingAnalyticsMetrics.MaxMoney,
            ListingAnalyticsMetrics.NormalizeMoney(decimal.MaxValue / 2));
    }

    [Fact]
    public void NormalizeCount_va_ratio_chuan_hoa_gia_tri_bat_thuong()
    {
        Assert.Equal(0, ListingAnalyticsMetrics.NormalizeCount(-3));
        Assert.Equal(0, ListingAnalyticsMetrics.NormalizeCount(null));
        Assert.Equal(7, ListingAnalyticsMetrics.NormalizeCount(7));
        Assert.Equal(0L, ListingAnalyticsMetrics.NormalizeLong(-1));
        Assert.Null(ListingAnalyticsMetrics.NormalizeRatio(null));
        Assert.Equal(-0.125m, ListingAnalyticsMetrics.NormalizeRatio(-0.125m));
        Assert.Equal(-ListingAnalyticsMetrics.MaxRatio, ListingAnalyticsMetrics.NormalizeRatio(-9_999_999m));
    }
}
