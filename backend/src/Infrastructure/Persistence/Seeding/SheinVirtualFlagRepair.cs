using Application.Services;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Shared.Constants;

namespace Infrastructure.Persistence.Seeding;

/// <summary>
/// Gỡ cờ "hàng ảo" bị dán oan lên những lần mua vật lý.
///
/// <c>virtual_inventory_flag</c> nói về cách SHEIN trữ/giao món hàng, không nói "món này là gói
/// thành viên", nhưng nó từng được tính là dấu hiệu hàng ảo ở cả extension lẫn backend. Đo trên dữ
/// liệu thật: GSU18V24X001CSQ (<c>goods_id 499132132</c>) và GSU18G24X001XC3
/// (<c>goods_id 502358797</c>) đều mang <c>virtual_inventory_flag=1</c>, <c>is_prime_goods=false</c>,
/// <c>order_type=1</c>, SKU <c>sz2604…</c> — và cả hai đã <b>shipped/delivered kèm mã vận đơn</b>.
/// Nhánh xử lý hàng ảo là nhánh phá huỷ (gỡ khỏi đơn sàn, xoá dòng chi), nên hai lần mua thật bị
/// loại khỏi giá mua ở <b>mọi</b> lượt sync.
///
/// Luật nhận diện đã được siết ở cả hai phía, nhưng dữ liệu cũ không tự khỏi: cờ được lưu trên
/// dòng mua và trước đây dán một chiều nên không payload nào gỡ được. Ở đây chỉ gỡ đúng những dòng
/// mà chính payload SHEIN đã nói ngược lại — <c>order_type</c> có giá trị và không phải 5/6 — nên
/// gói Club/Saver thật (<c>order_type=5</c>) không bị chạm tới.
///
/// Cố tình <b>không</b> tự gắn lại vào đơn sàn: việc đó thuộc luồng khớp, và lượt Sync Order kế
/// tiếp sẽ gắn bằng đúng luật phone+zip thay vì một bản dịch thứ hai đặt ở đây.
/// </summary>
public static class SheinVirtualFlagRepair
{
    /// <summary>Khoá setting đánh dấu đợt sửa đã chạy xong.</summary>
    public const string CompletedSettingKey = "maintenance.shein_virtual_flag_repair_v1";

    /// <summary>Chạy đợt sửa nếu setting đánh dấu chưa tồn tại; đã chạy rồi thì thoát ngay.</summary>
    public static async Task RunAsync(
        ApplicationDbContext context,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        var marker = await context.Settings
            .FirstOrDefaultAsync(s => s.Key == CompletedSettingKey, cancellationToken);
        if (marker is not null && marker.Value == "done") return;

        // order_type null = SHEIN chưa nói gì; giữ nguyên kết luận cũ vì không có bằng chứng ngược.
        var mislabeled = await context.SupplierPurchases
            .Where(p => p.IsMembershipOrVirtual
                && p.SupplierOrderType != null
                && p.SupplierOrderType != 5
                && p.SupplierOrderType != 6)
            .ToListAsync(cancellationToken);

        if (mislabeled.Count == 0)
        {
            logger.LogInformation("Sửa cờ hàng ảo SHEIN: không có dòng nào bị dán oan.");
            await MarkCompletedAsync(context, marker, cancellationToken);
            return;
        }

        var now = DateTime.UtcNow;
        var affectedOrderIds = new HashSet<Guid>();

        foreach (var purchase in mislabeled)
        {
            purchase.IsMembershipOrVirtual = false;
            purchase.UpdatedAt = now;

            // Lý do bỏ qua cũ giờ là sai sự thật. Xoá nó đi để dòng hiện ở hàng đợi "chưa gắn" như
            // một lần mua bình thường chờ khớp, không phải như một gói Club đã bị loại.
            if (purchase.SkipReason == SheinSkipReasons.MembershipVirtual)
            {
                purchase.SkipReason = null;
                purchase.SkipEvidenceJson = null;
            }

            if (purchase.OrderId.HasValue) affectedOrderIds.Add(purchase.OrderId.Value);
        }

        // Dòng nào còn dính đơn sàn thì giá mua của đơn đó phải tính lại: cờ vừa gỡ có thể đưa nó
        // trở lại tập được tính tiền.
        var repriced = 0;
        if (affectedOrderIds.Count > 0)
        {
            var orders = await context.Orders
                .Include(o => o.SupplierPurchases)
                .Where(o => affectedOrderIds.Contains(o.Id))
                .ToListAsync(cancellationToken);

            foreach (var order in orders)
            {
                var links = order.SupplierPurchases ?? [];
                var beforeCharged = links.Where(l => l.IsCharged).Select(l => l.Id).ToHashSet();
                OrderSupplierCost.ApplyAggregates(order);
                if (!beforeCharged.SetEquals(links.Where(l => l.IsCharged).Select(l => l.Id)))
                {
                    order.UpdatedAt = now;
                    repriced++;
                }
            }
        }

        await MarkCompletedAsync(context, marker, cancellationToken);
        logger.LogInformation(
            "Sửa cờ hàng ảo SHEIN: gỡ cờ trên {Count} lần mua ({Billnos}), {Repriced} đơn đổi giá mua. "
            + "Lượt Sync Order kế tiếp sẽ gắn lại các dòng chưa thuộc đơn nào.",
            mislabeled.Count,
            string.Join(", ", mislabeled.Select(p => p.SupplierOrderNo ?? p.Id.ToString()).Take(20)),
            repriced);
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
                Label = "Đã gỡ cờ hàng ảo dán oan",
                Value = "done",
                Type = "text",
                Description = "Nội bộ: đánh dấu đợt gỡ cờ Club/Saver khỏi các lần mua vật lý đã chạy xong."
            });
        }
        else
        {
            marker.Value = "done";
        }

        await context.SaveChangesAsync(cancellationToken);
    }
}
