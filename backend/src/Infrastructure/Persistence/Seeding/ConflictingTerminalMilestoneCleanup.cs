using Application.Services;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Persistence.Seeding;

/// <summary>
/// Đưa timeline fulfil về chuẩn status sàn: xoá <c>RefundedAt</c>/<c>CanceledAt</c> khi
/// <c>PlatformOrderStatus</c> không phải Hủy/Refund (stamp tay hoặc SHEIN hoàn NCC gây lệch
/// "Refund" trên timeline vs "Đã giao"/"Đang giao" trên cột trạng thái).
/// </summary>
public static class ConflictingTerminalMilestoneCleanup
{
    /// <summary>Khoá setting đánh dấu đợt dọn đã chạy xong.</summary>
    public const string CompletedSettingKey = "maintenance.conflicting_terminal_milestone_cleanup_v1";

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
            .Where(o => o.RefundedAt != null || o.CanceledAt != null)
            .ToListAsync(cancellationToken);

        var cleared = 0;
        foreach (var order in candidates)
        {
            if (OrderTerminalEvidence.ClearConflictingTerminals(order))
            {
                order.UpdatedAt = DateTime.UtcNow;
                cleared++;
            }
        }

        if (cleared > 0)
        {
            await context.SaveChangesAsync(cancellationToken);
        }

        await MarkCompletedAsync(context, marker, cancellationToken);
        logger.LogInformation(
            "Dọn mốc Hủy/Refund lệch status sàn: xoá {Cleared}/{Total} đơn.",
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
                Label = "Đã dọn mốc Hủy/Refund lệch PlatformOrderStatus",
                Value = "done",
                Type = "text",
                Description =
                    "Nội bộ: đánh dấu đợt xoá RefundedAt/CanceledAt khi sàn không phải Hủy/Refund đã chạy xong."
            });
        }
        else
        {
            marker.Value = "done";
        }

        await context.SaveChangesAsync(cancellationToken);
    }
}
