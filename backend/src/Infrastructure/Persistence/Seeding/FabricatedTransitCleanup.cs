using Application.Services;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Persistence.Seeding;

/// <summary>
/// Xoá những mốc Transit không phải clock rời kho thật, mà chỉ là bản sao của mốc Track.
///
/// Một phiên bản trước từng stamp Transit bằng chính <c>OrderShippedTime</c> của 4Seller — cùng giá
/// trị đã dùng cho mốc Track. Đo trên 246 đơn có đủ hai mốc: 93 đơn mang Transit nguồn
/// <c>platform</c> và cả 93 đơn đó có Transit **trùng khít từng giây** với Track, khoảng cách đúng
/// 0 giờ ở mọi đơn. Đó là lý do dải timeline không hiện bộ đếm giữa Track và Transit: hai mốc trùng
/// nhau nên không có khoảng nào để đếm.
///
/// Mốc rời kho thật chỉ SHEIN biết (<c>delivery_time</c>), và mã hiện tại chỉ stamp Transit với
/// nguồn <c>shein</c>. Vì vậy mọi Transit nguồn <c>platform</c> là dữ liệu bịa: xoá đi để chặng hiện
/// "Transit?" trung thực, rồi <see cref="SheinTimelineBackfill"/> và truy vấn refresh của Sync Order
/// SHEIN sẽ dựng lại từ payload gốc khi có.
/// </summary>
public static class FabricatedTransitCleanup
{
    /// <summary>Khoá setting đánh dấu đợt dọn đã chạy xong.</summary>
    public const string CompletedSettingKey = "maintenance.fabricated_transit_cleanup_v1";

    public static async Task RunAsync(
        ApplicationDbContext context,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        var marker = await context.Settings
            .FirstOrDefaultAsync(s => s.Key == CompletedSettingKey, cancellationToken);
        if (marker is not null && marker.Value == "done")
        {
            return;
        }

        var candidates = await context.Orders
            .Where(o => o.InTransitAt != null && o.FulfilTimelineSources != null)
            .ToListAsync(cancellationToken);

        var cleared = candidates.Count(OrderTransitEvidence.ClearFabricatedTransit);

        await MarkCompletedAsync(context, marker, cancellationToken);
        logger.LogInformation(
            "Dọn mốc Transit bịa (bản sao mốc Track): xoá {Cleared}/{Total} đơn.",
            cleared,
            candidates.Count);
    }

    private static async Task MarkCompletedAsync(
        ApplicationDbContext context,
        Setting? marker,
        CancellationToken cancellationToken)
    {
        if (marker is null)
        {
            context.Settings.Add(new Setting
            {
                Group = "Bảo trì dữ liệu",
                Key = CompletedSettingKey,
                Label = "Đã dọn mốc Transit bịa từ clock 4Seller",
                Value = "done",
                Type = "text",
                Description = "Nội bộ: đánh dấu đợt xoá mốc Transit trùng khít mốc Track đã chạy xong."
            });
        }
        else
        {
            marker.Value = "done";
        }

        await context.SaveChangesAsync(cancellationToken);
    }
}
