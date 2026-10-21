using Application.Services;
using Xunit;

namespace Application.Tests;

/// <summary>
/// Khoá công thức «đề xuất hủy list»: sản phẩm đang bán / mới lên không bao giờ bị đề xuất, sản phẩm
/// chết hẳn phải chạm ngưỡng delist, và các trụ điểm không vượt trọng số đã công bố.
/// </summary>
public class ListingDelistScoringTests
{
    /// <summary>Shop mẫu: CTR 1%, thêm giỏ 10% trên click, chốt 20% trên giỏ, 10K hiển thị/SP.</summary>
    private static ListingDelistScoring.Benchmarks Shop() => new(
        MedianCtr: 0.01m,
        MedianAtcRate: 0.10m,
        MedianOrderRate: 0.20m,
        MedianImpressions: 10_000,
        TotalGmv: 5_000m);

    private static ListingDelistScoring.Candidate Row(
        decimal gmv = 0m,
        int orders = 0,
        long impressions = 0,
        long clicks = 0,
        int atc = 0,
        decimal? ctr = null,
        decimal? gmvRatio = null,
        decimal? ordersRatio = null,
        decimal? impressionsRatio = null,
        int? peakOrders = null,
        decimal? peakGmv = null,
        int daysTracked = 14,
        int? daysListed = 14) => new(
        gmv,
        orders,
        impressions,
        clicks,
        atc,
        ctr,
        gmvRatio,
        ordersRatio,
        impressionsRatio,
        peakOrders ?? orders,
        peakGmv ?? gmv,
        daysTracked,
        daysListed);

    [Fact]
    public void San_pham_khong_don_khong_hien_thi_bi_de_xuat_huy()
    {
        var verdict = ListingDelistScoring.Score(Row(), Shop(), "random gadget", null, null);

        Assert.Equal(ListingDelistScoring.VerdictDelist, verdict.Kind);
        Assert.True(verdict.Score >= ListingDelistScoring.DelistThreshold);
        Assert.Equal(ListingDelistScoring.SalesWeight, verdict.Breakdown.Sales);
        Assert.Equal(ListingDelistScoring.ReachWeight, verdict.Breakdown.Reach);
        Assert.NotEmpty(verdict.Reasons);
    }

    [Fact]
    public void Diem_chuan_hoa_theo_trong_so_do_duoc_nen_thieu_du_lieu_khong_lam_lech_nguong()
    {
        // Không có traffic để so funnel, không có ratio, không có keyword → chỉ Sales + Reach đo được.
        var verdict = ListingDelistScoring.Score(Row(), Shop(), null, null, null);

        Assert.Equal(ListingDelistScoring.SalesWeight + ListingDelistScoring.ReachWeight, verdict.Breakdown.Available);
        Assert.Equal(100, verdict.Score);
    }

    [Fact]
    public void San_pham_dang_ban_duoc_luon_giu_slot()
    {
        var row = Row(gmv: 300m, orders: 9, impressions: 40_000, clicks: 600, atc: 80);

        var verdict = ListingDelistScoring.Score(row, Shop(), "floral maxi dress", null, null);

        Assert.Equal(ListingDelistScoring.VerdictKeep, verdict.Kind);
        Assert.Equal(0, verdict.Score);
    }

    [Fact]
    public void Gmv_chiem_ty_trong_lon_van_giu_slot_du_it_don()
    {
        // 1 đơn nhưng 150/5000 = 3% GMV shop → trên ngưỡng WinnerGmvShare.
        var row = Row(gmv: 150m, orders: 1, impressions: 800, clicks: 5);

        var verdict = ListingDelistScoring.Score(row, Shop(), "silk slip dress", null, null);

        Assert.Equal(ListingDelistScoring.VerdictKeep, verdict.Kind);
    }

    [Fact]
    public void Lan_sync_dau_da_co_cua_so_7_ngay_thi_duoc_cham()
    {
        var row = Row(daysTracked: 1);

        var verdict = ListingDelistScoring.Score(row, Shop(), "random gadget", null, null);

        Assert.Equal(ListingDelistScoring.VerdictDelist, verdict.Kind);
        Assert.True(verdict.Score >= ListingDelistScoring.DelistThreshold);
    }

    [Fact]
    public void Chua_co_snapshot_thi_khong_cham_diem()
    {
        var row = Row(daysTracked: ListingDelistScoring.MinTrackedDays - 1);

        var verdict = ListingDelistScoring.Score(row, Shop(), "new arrival top", null, null);

        Assert.Equal(ListingDelistScoring.VerdictNew, verdict.Kind);
        Assert.Equal(0, verdict.Score);
    }

    [Fact]
    public void Moi_list_duoi_7_ngay_thi_khong_cham_diem()
    {
        var row = Row(daysListed: ListingDelistScoring.MinListedDays - 1);

        var verdict = ListingDelistScoring.Score(row, Shop(), "new arrival top", null, null);

        Assert.Equal(ListingDelistScoring.VerdictNew, verdict.Kind);
        Assert.Contains(verdict.Reasons, r => r.Contains("Mới list", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ComputeDaysListed_dung_ngay_VN()
    {
        var listedAt = new DateTime(2026, 8, 17, 3, 0, 0, DateTimeKind.Utc);
        var snapshot = new DateOnly(2026, 8, 23);

        var days = ListingDelistScoring.ComputeDaysListed(snapshot, listedAt, null);

        Assert.Equal(7, days);
    }

    [Fact]
    public void Dinh_don_trong_ky_truoc_keo_diem_sales_xuong()
    {
        // Hôm nay 0 đơn nhưng một cửa sổ 7 ngày gần đây có 3 đơn → chưa phải hàng chết.
        var dead = ListingDelistScoring.Score(Row(impressions: 9_000, clicks: 90), Shop(), null, null, null);
        var revived = ListingDelistScoring.Score(
            Row(impressions: 9_000, clicks: 90, peakOrders: 3),
            Shop(),
            null,
            null,
            null);

        Assert.True(revived.Breakdown.Sales < dead.Breakdown.Sales);
        Assert.True(revived.Score < dead.Score);
    }

    [Fact]
    public void Funnel_chi_cham_buoc_du_volume()
    {
        // 50 hiển thị < MinCtrImpressions → CTR không bị so trung vị (tránh phạt vì thiếu dữ liệu).
        var thin = ListingDelistScoring.Score(Row(impressions: 50, clicks: 0), Shop(), null, null, null);

        Assert.Equal(0, thin.Breakdown.Funnel);
    }

    [Fact]
    public void Funnel_phat_gan_toi_da_khi_thua_moi_buoc_so_trung_vi()
    {
        // Shop khoẻ: CTR 5%, thêm giỏ 20%/click, chốt 50%/giỏ.
        var shop = new ListingDelistScoring.Benchmarks(0.05m, 0.20m, 0.50m, 10_000, 5_000m);
        // Sản phẩm: CTR 0.5%, thêm giỏ 1%/click, 0 đơn trên 5 giỏ → thua cả 3 bước.
        var row = Row(impressions: 100_000, clicks: 500, atc: 5);

        var verdict = ListingDelistScoring.Score(row, shop, null, null, null);

        var maxFunnel = ListingDelistScoring.CtrWeight
            + ListingDelistScoring.AtcWeight
            + ListingDelistScoring.OrderRateWeight;
        Assert.Equal(24, verdict.Breakdown.Funnel);
        Assert.InRange(verdict.Breakdown.Funnel, maxFunnel - 2, maxFunnel);
    }

    [Fact]
    public void Hien_thi_bang_trung_vi_thi_khong_bi_tru_diem_reach()
    {
        var row = Row(impressions: 10_000, clicks: 100, atc: 10, orders: 2, ctr: 0.01m);

        var verdict = ListingDelistScoring.Score(row, Shop(), null, null, null);

        Assert.Equal(0, verdict.Breakdown.Reach);
    }

    [Fact]
    public void Giam_manh_so_ky_truoc_an_het_trong_so_trend()
    {
        var row = Row(impressions: 5_000, clicks: 40, impressionsRatio: -0.8m);

        var verdict = ListingDelistScoring.Score(row, Shop(), null, null, null);

        Assert.Equal(ListingDelistScoring.TrendWeight, verdict.Breakdown.Trend);
    }

    [Fact]
    public void Dang_tang_truong_duoc_tra_lai_diem_de_khong_bi_huy_oan()
    {
        var flat = ListingDelistScoring.Score(Row(impressions: 2_000, clicks: 10), Shop(), null, null, null);
        var rising = ListingDelistScoring.Score(
            Row(impressions: 2_000, clicks: 10, impressionsRatio: 1.5m),
            Shop(),
            null,
            null,
            null);

        Assert.Equal(ListingDelistScoring.RisingReliefMax, rising.Breakdown.RisingRelief);
        Assert.True(rising.Score < flat.Score);
    }

    [Fact]
    public void Khong_co_keyword_shop_thi_tru_keyword_bo_trong()
    {
        var verdict = ListingDelistScoring.Score(Row(impressions: 1_000), Shop(), "anything", null, null);

        Assert.Equal(0, verdict.Breakdown.Keyword);
        Assert.Null(verdict.KeywordFit);
    }

    [Fact]
    public void Ten_khop_keyword_shop_thi_khong_bi_phat_keyword()
    {
        string[][] tokenSets = [["floral", "midi", "dress"]];
        string[] labels = ["floral midi dress"];

        var onNiche = ListingDelistScoring.Score(
            Row(impressions: 1_000),
            Shop(),
            "Floral Midi Dress for Women Summer",
            tokenSets,
            labels);
        var offNiche = ListingDelistScoring.Score(
            Row(impressions: 1_000),
            Shop(),
            "Stainless Steel Kitchen Funnel",
            tokenSets,
            labels);

        Assert.Equal(0, onNiche.Breakdown.Keyword);
        Assert.Equal("floral midi dress", onNiche.BestKeyword);
        Assert.Equal(ListingDelistScoring.KeywordWeight, offNiche.Breakdown.Keyword);
        Assert.Contains(offNiche.Reasons, r => r.Contains("keyword", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Diem_khong_bao_gio_vuot_100_hay_am()
    {
        var worst = ListingDelistScoring.Score(
            Row(impressions: 20_000, clicks: 40, atc: 10, ctr: 0.001m, gmvRatio: -1m, impressionsRatio: -1m),
            Shop(),
            "unrelated item",
            [["floral", "midi", "dress"]],
            ["floral midi dress"]);

        Assert.InRange(worst.Score, 0, 100);
        Assert.Equal(ListingDelistScoring.VerdictDelist, worst.Kind);
    }

    [Fact]
    public void Trung_vi_bo_qua_dong_thieu_volume()
    {
        // Chỉ dòng 10K hiển thị đủ điều kiện tính CTR; dòng 10 hiển thị bị bỏ.
        var rows = new[]
        {
            Row(impressions: 10_000, clicks: 200, atc: 40, orders: 8, ctr: 0.02m),
            Row(impressions: 10, clicks: 0)
        };

        var benchmarks = ListingDelistScoring.BuildBenchmarks(rows);

        Assert.Equal(0.02m, benchmarks.MedianCtr);
        Assert.Equal(0.20m, benchmarks.MedianAtcRate);
        Assert.Equal(0.20m, benchmarks.MedianOrderRate);
        Assert.Equal(5_005, benchmarks.MedianImpressions);
    }
}
