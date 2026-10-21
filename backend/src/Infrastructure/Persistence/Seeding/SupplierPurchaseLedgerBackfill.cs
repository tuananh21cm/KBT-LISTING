using Application.Interfaces;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Persistence.Seeding;

/// <summary>
/// Dựng lại dòng chi CostUpdate cho những đơn có giá mua mà sổ cái đang trống — đúng triệu chứng
/// "đã mua mà không có trong sổ cái" của lượt <c>ext-2608040342-be285208</c>.
///
/// Nhánh cũ của <c>ReconcileCostUpdateTransactionsAsync</c> bỏ qua lần mua không quy kết được tài
/// khoản/thẻ (email billing SHEIN chưa map <c>FulfilAccounts</c>), nên đơn hiện giá mua trong khi sổ
/// cái không có đồng nào. Nhánh đó đã được sửa để vẫn ghi dòng chi kèm ghi chú "chưa rõ tài khoản
/// mua hàng", nhưng dữ liệu cũ không tự sửa: <c>HasSheinIncrementalDelta</c> không thấy delta nào
/// nên lượt sync kế tiếp bỏ qua billno đó, và mode <c>all</c> cũng chỉ quét cửa sổ Lookback nên đơn
/// cũ hơn không bao giờ được chạm lại.
///
/// Chạy qua <see cref="IOrderSyncService"/> thay vì viết SQL riêng: một bản dịch thứ hai của luật
/// dựng sổ cái là một chỗ nữa để lệch.
/// </summary>
public static class SupplierPurchaseLedgerBackfill
{
    /// <summary>Khoá setting đánh dấu đợt dựng lại đã chạy xong.</summary>
    public const string CompletedSettingKey = "maintenance.supplier_purchase_ledger_rebuild_v1";

    /// <summary>Chạy đợt dựng lại nếu setting đánh dấu chưa tồn tại; đã chạy rồi thì thoát ngay.</summary>
    public static async Task RunAsync(
        ApplicationDbContext context,
        IOrderSyncService orderSyncService,
        Guid actingUserId,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        var marker = await context.Settings
            .FirstOrDefaultAsync(s => s.Key == CompletedSettingKey, cancellationToken);
        if (marker is not null && marker.Value == "done") return;

        var rebuilt = await orderSyncService.RebuildCostUpdateLedgerAsync(actingUserId, cancellationToken);

        // Đọc lại marker: RebuildCostUpdateLedgerAsync dùng scope DbContext khác nên thực thể vừa
        // theo dõi ở đây có thể đã lạc khỏi trạng thái DB.
        marker = await context.Settings
            .FirstOrDefaultAsync(s => s.Key == CompletedSettingKey, cancellationToken);
        if (marker is null)
        {
            context.Settings.Add(new Setting
            {
                Group = "Bảo trì dữ liệu",
                Key = CompletedSettingKey,
                Label = "Đã dựng lại sổ cái từ đơn NCC",
                Value = "done",
                Type = "text",
                Description = "Nội bộ: đánh dấu đợt dựng lại dòng chi CostUpdate cho đơn có giá mua "
                    + "mà sổ cái trống đã chạy xong."
            });
        }
        else
        {
            marker.Value = "done";
        }

        await context.SaveChangesAsync(cancellationToken);
        logger.LogInformation(
            "Dựng lại sổ cái từ đơn NCC: {Rebuilt} đơn được ghi thêm dòng chi CostUpdate.",
            rebuilt);
    }
}
