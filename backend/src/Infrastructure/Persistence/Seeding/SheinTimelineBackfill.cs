using Application.Services;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Persistence.Seeding;

/// <summary>
/// Sửa một lần các mốc Transit / Giao được stamp bằng bản extension cũ.
///
/// Bản cũ lấy <c>delivery_time</c> của SHEIN làm mốc giao, nhưng đó là mốc rời kho: đối chiếu 156
/// payload gốc cho thấy <c>signed_time</c> mới là mốc giao và luôn muộn hơn trung bình 5,9 ngày
/// (nhiều nhất 9,5 ngày). Không thể chờ lần sync sau tự sửa, vì
/// <see cref="OrderFulfilTimeline.Stamp"/> chỉ ghi đè khi mốc mới **sớm hơn** ở cùng rank — mốc
/// đúng lại muộn hơn nên sẽ bị từ chối vĩnh viễn. Vì vậy phải xoá mốc nguồn <c>shein</c> trước rồi
/// stamp lại từ payload gốc đã lưu.
///
/// Đợt v2 dựng lại thêm các mốc Transit bị luật <c>Track ≤ Transit</c> cũ xoá oan: hàng rời kho
/// SHEIN trước khi mã vận đơn lên sàn là chuyện thường (49/246 đơn đo được), nhưng luật cũ coi đó là
/// nghịch lý và xoá mốc. Luật đã bỏ, còn giá trị đã mất thì chỉ payload gốc dựng lại được.
///
/// Đợt v4 chạy ngay sau <see cref="FabricatedTransitCleanup"/>: đợt kia vừa xoá 93 mốc Transit chỉ
/// là bản sao clock 4Seller, nên phải stamp lại từ payload gốc cho những đơn đã thu được
/// <c>delivery_time</c> thật.
///
/// Chạy một lần mỗi phiên bản khoá, đánh dấu bằng setting để lần khởi động sau bỏ qua.
/// </summary>
public static class SheinTimelineBackfill
{
    /// <summary>Khoá setting đánh dấu đợt sửa đã chạy xong.</summary>
    public const string CompletedSettingKey = "maintenance.shein_timeline_delivered_fix_v4";

    public static async Task RunAsync(
        ApplicationDbContext context,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        var marker = await context.Settings
            .FirstOrDefaultAsync(s => s.Key == CompletedSettingKey, cancellationToken);
        if (marker is not null && marker.Value == "done")
        {
            logger.LogInformation(
                "Bỏ qua đợt sửa mốc fulfil: khoá {Key} đã đánh dấu xong.", CompletedSettingKey);
            return;
        }

        var links = await context.SupplierPurchases
            .Where(l => l.RawPayload != null && l.OrderId != null)
            .Select(l => new { OrderId = l.OrderId!.Value, l.RawPayload })
            .ToListAsync(cancellationToken);

        // Mọi nhánh đều phải log: một đợt bảo trì im lặng không làm gì là thứ khó phát hiện nhất.
        if (links.Count == 0)
        {
            logger.LogInformation("Đợt sửa mốc fulfil: không có link nào lưu payload gốc.");
            await MarkCompletedAsync(context, marker, cancellationToken);
            return;
        }

        // Đơn nhiều link SHEIN: gộp theo đơn, lấy gói rời kho sớm nhất và ký nhận muộn nhất.
        var byOrder = new Dictionary<Guid, SheinRawTimelineExtractor.Result>();
        foreach (var link in links)
        {
            var parsed = SheinRawTimelineExtractor.Extract(link.RawPayload);
            if (parsed.ShippedAt is null && parsed.DeliveredAt is null) continue;

            if (!byOrder.TryGetValue(link.OrderId, out var current))
            {
                byOrder[link.OrderId] = parsed;
                continue;
            }

            byOrder[link.OrderId] = new SheinRawTimelineExtractor.Result(
                Earliest(current.ShippedAt, parsed.ShippedAt),
                Latest(current.DeliveredAt, parsed.DeliveredAt));
        }

        if (byOrder.Count == 0)
        {
            logger.LogInformation(
                "Đợt sửa mốc fulfil: {Links} payload gốc nhưng không bóc được mốc nào.",
                links.Count);
            await MarkCompletedAsync(context, marker, cancellationToken);
            return;
        }

        var orderIds = byOrder.Keys.ToList();
        var orders = await context.Orders
            .Where(o => orderIds.Contains(o.Id))
            .ToListAsync(cancellationToken);

        var repaired = 0;
        foreach (var order in orders)
        {
            if (!byOrder.TryGetValue(order.Id, out var parsed)) continue;

            var beforeTransit = order.InTransitAt;
            var beforeDelivered = order.DeliveredAt;

            if (parsed.ShippedAt is not null && !IsManual(order, OrderFulfilTimeline.Milestone.InTransit))
            {
                OrderFulfilTimeline.Clear(order, OrderFulfilTimeline.Milestone.InTransit);
                OrderFulfilTimeline.Stamp(
                    order, OrderFulfilTimeline.Milestone.InTransit,
                    parsed.ShippedAt, OrderFulfilTimeline.Source.Shein);
            }

            if (IsManual(order, OrderFulfilTimeline.Milestone.Delivered))
            {
                // Người sửa tay thắng payload: Clear() bỏ qua rank nên không đưa mốc này vào đợt sửa.
            }
            else if (parsed.DeliveredAt is not null)
            {
                OrderFulfilTimeline.Clear(order, OrderFulfilTimeline.Milestone.Delivered);
                OrderFulfilTimeline.Stamp(
                    order, OrderFulfilTimeline.Milestone.Delivered,
                    parsed.DeliveredAt, OrderFulfilTimeline.Source.Shein);
            }
            else if (order.DeliveredAt is not null && IsSheinSourced(order, "deliveredAt"))
            {
                // Payload nói đơn chưa giao xong: mốc giao cũ là giá trị bịa từ delivery_time.
                OrderFulfilTimeline.Clear(order, OrderFulfilTimeline.Milestone.Delivered);
            }

            OrderFulfilTimeline.Normalize(order);

            if (order.InTransitAt != beforeTransit || order.DeliveredAt != beforeDelivered)
            {
                repaired++;
            }
        }

        await MarkCompletedAsync(context, marker, cancellationToken);
        logger.LogInformation(
            "Sửa mốc fulfil từ payload SHEIN gốc: {Repaired}/{Total} đơn thay đổi ({Links} payload).",
            repaired,
            orders.Count,
            links.Count);
    }

    /// <summary>
    /// Mốc do người sửa tay. Phải hỏi trước khi <see cref="OrderFulfilTimeline.Clear"/> vì Clear bỏ
    /// qua rank nguồn, nên chạy lại đợt sửa sẽ xoá mất chỉnh sửa thủ công.
    /// </summary>
    private static bool IsManual(Order order, OrderFulfilTimeline.Milestone milestone) =>
        OrderFulfilTimeline.SourceOf(order, milestone) == OrderFulfilTimeline.Source.Manual;

    private static bool IsSheinSourced(Order order, string key)
    {
        var sources = order.FulfilTimelineSources;
        return !string.IsNullOrWhiteSpace(sources)
            && sources.Contains($"\"{key}\":\"shein\"", StringComparison.OrdinalIgnoreCase);
    }

    private static DateTime? Earliest(DateTime? a, DateTime? b)
    {
        if (a is null) return b;
        if (b is null) return a;
        return a.Value <= b.Value ? a : b;
    }

    private static DateTime? Latest(DateTime? a, DateTime? b)
    {
        if (a is null) return b;
        if (b is null) return a;
        return a.Value >= b.Value ? a : b;
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
                Label = "Đã sửa mốc giao từ payload SHEIN gốc",
                Value = "done",
                Type = "text",
                Description = "Nội bộ: đánh dấu đợt dựng lại mốc Transit/Giao đã chạy xong."
            });
        }
        else
        {
            marker.Value = "done";
        }

        await context.SaveChangesAsync(cancellationToken);
    }
}
