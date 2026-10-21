using Domain.Entities;
using Domain.Orders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Persistence.Seeding;

/// <summary>
/// Dựng lại <c>PlatformOrderStatus</c> cho những đơn đã bị lượt sync 4Seller sau đó đẩy khỏi
/// <c>REFUND</c>: 4Seller thôi gửi <c>cancelReason</c> khi case hoàn đã đóng và báo đơn về status
/// thương mại (<c>shipped</c> / <c>COMPLETED</c>), nên đơn còn mã vận đơn rơi về tab
/// <c>IN_TRANSIT_GROUP</c> và hiện "Đang giao" dù đã hoàn xong.
///
/// Hai việc trong một lượt:
/// <list type="bullet">
/// <item>Chuẩn hoá <c>COMPLETED</c> → <c>DELIVERED</c> theo alias mới trong
/// <see cref="PlatformOrderStatuses.Normalize"/> (rows cũ lưu giá trị chưa map).</item>
/// <item>Trả status về <c>REFUND</c> khi mốc <c>RefundedAt</c> còn đó — mốc này chỉ do Sync 4Seller
/// stamp nên là bằng chứng hoàn của sàn.</item>
/// </list>
///
/// Phải chạy sau <see cref="ConflictingTerminalMilestoneCleanup"/>: đợt đó xoá mốc Refund stamp tay /
/// SHEIN hoàn NCC, nên mốc còn sót lại mới là bằng chứng đáng tin để suy ra <c>REFUND</c>.
/// <c>WorkflowTab</c> được <c>SaveChangesAsync</c> tính lại từ status vừa sửa.
/// </summary>
public static class RefundStatusLatchRepair
{
    /// <summary>Khoá setting đánh dấu đợt dựng lại đã chạy xong.</summary>
    public const string CompletedSettingKey = "maintenance.refund_status_latch_repair_v1";

    private const string LegacyCompletedStatus = "COMPLETED";

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
            .Where(o => o.RefundedAt != null
                || (o.PlatformOrderStatus != null
                    && o.PlatformOrderStatus.ToUpper() == LegacyCompletedStatus))
            .ToListAsync(cancellationToken);

        var normalized = 0;
        var restored = 0;
        foreach (var order in candidates)
        {
            var changed = false;

            var canonical = PlatformOrderStatuses.Normalize(order.PlatformOrderStatus);
            if (canonical is not null && !string.Equals(canonical, order.PlatformOrderStatus, StringComparison.Ordinal))
            {
                order.PlatformOrderStatus = canonical;
                normalized++;
                changed = true;
            }

            // Hủy thật vẫn thắng: đơn hoàn rồi bị TikTok chốt CANCELLED thì giữ CANCELLED.
            if (order.RefundedAt is not null
                && !PlatformOrderStatuses.IsRefund(order.PlatformOrderStatus)
                && !PlatformOrderStatuses.IsCanceled(order.PlatformOrderStatus))
            {
                order.PlatformOrderStatus = PlatformOrderStatuses.Refund;
                restored++;
                changed = true;
            }

            if (changed)
            {
                order.UpdatedAt = DateTime.UtcNow;
            }
        }

        if (normalized > 0 || restored > 0)
        {
            await context.SaveChangesAsync(cancellationToken);
        }

        await MarkCompletedAsync(context, marker, cancellationToken);
        logger.LogInformation(
            "Dựng lại status hoàn: chuẩn hoá {Normalized} đơn, trả REFUND {Restored}/{Total} đơn.",
            normalized,
            restored,
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
                Label = "Đã dựng lại PlatformOrderStatus REFUND bị sync ghi đè",
                Value = "done",
                Type = "text",
                Description =
                    "Nội bộ: đánh dấu đợt map COMPLETED → DELIVERED và trả REFUND cho đơn còn mốc "
                    + "RefundedAt đã chạy xong."
            });
        }
        else
        {
            marker.Value = "done";
        }

        await context.SaveChangesAsync(cancellationToken);
    }
}
