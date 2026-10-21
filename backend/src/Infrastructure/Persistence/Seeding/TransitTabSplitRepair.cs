using Domain.Entities;
using Domain.Orders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Persistence.Seeding;

/// <summary>
/// Clears legacy <c>WorkflowTab = IN_TRANSIT_GROUP</c> so
/// <see cref="OrderWorkflowSignalsBackfill"/> can re-stamp rows into
/// <c>AWAITING_COLLECTION_GROUP</c> or <c>IN_TRANSIT</c> after the tab split.
/// </summary>
public static class TransitTabSplitRepair
{
    /// <summary>Khoá setting đánh dấu đợt tách tab đã chạy xong.</summary>
    public const string CompletedSettingKey = "maintenance.transit_tab_split_v1";

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

        var cleared = await context.Orders
            .Where(o => o.WorkflowTab == OrderWorkflowTabs.InTransitGroup)
            .ExecuteUpdateAsync(
                s => s.SetProperty(o => o.WorkflowTab, (string?)null),
                cancellationToken);

        await MarkCompletedAsync(context, marker, cancellationToken);
        logger.LogInformation(
            "TransitTabSplitRepair: đã xoá WorkflowTab cũ IN_TRANSIT_GROUP trên {Count} đơn "
            + "(OrderWorkflowSignalsBackfill sẽ stamp lại).",
            cleared);
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
                Label = "Đã tách WorkflowTab IN_TRANSIT_GROUP → Chờ lấy / Đang giao",
                Value = "done",
                Type = "text",
                Description =
                    "Nội bộ: đánh dấu đợt clear WorkflowTab=IN_TRANSIT_GROUP để backfill stamp "
                    + "AWAITING_COLLECTION_GROUP / IN_TRANSIT đã chạy xong."
            });
        }
        else
        {
            marker.Value = "done";
        }

        await context.SaveChangesAsync(cancellationToken);
    }
}
