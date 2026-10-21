using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Shared.Constants;

namespace Infrastructure.Persistence.Seeding;

/// <summary>
/// Chuyển các lần mua đã bị gỡ theo lệnh xoá dòng chi trên sổ cái sang trạng thái <c>ignored</c>.
///
/// Xoá một dòng chi <c>CostUpdate</c> là phát biểu "lần mua này không phải chi phí của đơn", và luồng
/// gỡ đã tôn trọng nó bằng cách đưa dòng mua về hàng đợi. Nhưng <c>unmatched</c> nghĩa là "chờ máy
/// khớp", nên lượt sync kế tiếp có billno đó trong cửa sổ sẽ gắn lại và dựng lại đúng dòng chi vừa
/// xoá — thao tác của người dùng bị huỷ trong im lặng, và chu kỳ đó lặp mãi. Chỉ <c>ignored</c> mới là
/// trạng thái luồng sync không chạm tới.
///
/// Luồng gỡ đã được sửa để đánh dấu <c>ignored</c> ngay từ đầu; ở đây xử lý phần dữ liệu đã tồn tại
/// trước bản sửa (đo trên DB thật: 306 dòng mang lý do <c>ledger_deleted</c> sau đợt xoá sổ cái lúc
/// 13:28 ngày 04/08). Chỉ chạm dòng còn đúng cả ba điều kiện — chưa thuộc đơn nào, đang
/// <c>unmatched</c>, lý do là <c>ledger_deleted</c> — nên dòng nào đã được gắn tay lại thì không bị
/// kéo ngược.
///
/// Mở lại bằng nút "Trả về hàng đợi" ở trang Đơn NCC chưa gắn.
/// </summary>
public static class LedgerDeletedIgnoreBackfill
{
    /// <summary>Khoá setting đánh dấu đợt sửa đã chạy xong.</summary>
    public const string CompletedSettingKey = "maintenance.ledger_deleted_ignore_v1";

    /// <summary>Chạy đợt sửa nếu setting đánh dấu chưa tồn tại; đã chạy rồi thì thoát ngay.</summary>
    public static async Task RunAsync(
        ApplicationDbContext context,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        var marker = await context.Settings
            .FirstOrDefaultAsync(s => s.Key == CompletedSettingKey, cancellationToken);
        if (marker is not null && marker.Value == "done") return;

        var updated = await context.SupplierPurchases
            .Where(p => p.OrderId == null
                && p.MatchState == SupplierPurchaseMatchStates.Unmatched
                && p.SkipReason == SheinSkipReasons.LedgerDeleted)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(p => p.MatchState, SupplierPurchaseMatchStates.Ignored)
                    .SetProperty(p => p.IsCharged, false)
                    .SetProperty(p => p.UpdatedAt, DateTime.UtcNow),
                cancellationToken);

        await MarkCompletedAsync(context, marker, cancellationToken);
        logger.LogInformation(
            "Giữ quyết định xoá sổ cái: {Count} lần mua chuyển sang đã bỏ qua, lượt sync sau không "
            + "gắn lại nữa.",
            updated);
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
                Label = "Đã giữ quyết định xoá dòng chi sổ cái",
                Value = "done",
                Type = "text",
                Description = "Nội bộ: đánh dấu đợt chuyển lần mua bị gỡ theo lệnh xoá sổ cái sang "
                    + "trạng thái đã bỏ qua đã chạy xong."
            });
        }
        else
        {
            marker.Value = "done";
        }

        await context.SaveChangesAsync(cancellationToken);
    }
}
