using Application.Services;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Persistence.Seeding;

/// <summary>
/// Dựng lại snapshot giá mua <c>Orders.Charged*</c> cho những đơn mà nó đã lệch khỏi
/// <c>SupplierPurchases</c>.
///
/// Đo trên dữ liệu thật: 9/4063 đơn có **toàn bộ** cột <c>Charged*</c> = NULL trong khi dòng mua vẫn
/// đủ tiền và <c>IsCharged = true</c> — ví dụ đơn 577592679686312424 (billno GSU1SH28T00NJKR:
/// subtotal 15.14 + ship 3.99 + tax 1.25) có dòng sổ cái CostUpdate ghi
/// <c>purchaseCostAmount = 20.38</c> đúng, mà <c>totalCost</c> của API trả NULL. Hệ quả: card đơn
/// hiện Cost/Profit là <c>—</c> trong khi drawer ngay cạnh hiện đủ breakdown, và cột Profit trên
/// Lịch sử mua hàng tụt theo.
///
/// Nguyên nhân: <c>Charged*</c> từng phải gọi tay <see cref="OrderSupplierCost.ApplyAggregates(Order)"/>
/// ở ~8 chỗ, và <c>Order.SupplierPurchases</c> khởi tạo sẵn list rỗng nên một lần gọi với navigation
/// chưa nạp xoá sạch snapshot mà không ai phân biệt được với "đơn không có lần mua". Từ nay
/// <c>ApplicationDbContext.SaveChangesAsync</c> chốt lại phép tính trên tập link nạp kèm
/// <c>Include</c>, nên lệch mới không phát sinh được — nhưng dữ liệu cũ không tự khỏi vì đơn đã
/// đứng yên thì không lượt ghi nào chạm tới.
/// </summary>
public static class OrderChargedAggregatesRepair
{
    /// <summary>Khoá setting đánh dấu đợt sửa đã chạy xong.</summary>
    public const string CompletedSettingKey = "maintenance.order_charged_aggregates_repair_v1";

    /// <summary>Số đơn nạp kèm link mỗi lô — bảng mua giữ <c>RawPayload</c> ~55 KB/dòng.</summary>
    private const int ChunkSize = 100;

    /// <summary>Chạy đợt sửa nếu setting đánh dấu chưa tồn tại; đã chạy rồi thì thoát ngay.</summary>
    public static async Task RunAsync(
        ApplicationDbContext context,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        var marker = await context.Settings
            .FirstOrDefaultAsync(s => s.Key == CompletedSettingKey, cancellationToken);
        if (marker is not null && marker.Value == "done") return;

        // Chỉ chiếu hai cột neo thay vì cả mười hai: `Subtotal` quyết định đường breakdown và
        // `Total` quyết định đường ledger của `ResolveGrossPurchaseCost`, nên lệch ở giá mua luôn
        // hiện ra ở ít nhất một trong hai. Projection thay cho Include để không kéo `RawPayload`
        // của toàn bộ đơn vào RAM chỉ để so hai con số.
        var snapshots = await context.Orders
            .AsNoTracking()
            .Where(o => o.SupplierPurchases.Any())
            .Select(o => new
            {
                o.Id,
                SnapshotSubtotal = o.ChargedSupplierSubtotal,
                SnapshotTotal = o.ChargedSupplierTotal,
                LiveSubtotal = o.SupplierPurchases
                    .Where(p => p.IsCharged)
                    .Sum(p => p.SupplierSubtotal),
                LiveTotal = o.SupplierPurchases
                    .Where(p => p.IsCharged)
                    .Sum(p => p.SupplierTotal)
            })
            .ToListAsync(cancellationToken);

        var driftedIds = snapshots
            .Where(s => Differs(s.SnapshotSubtotal, s.LiveSubtotal)
                || Differs(s.SnapshotTotal, s.LiveTotal))
            .Select(s => s.Id)
            .ToList();

        if (driftedIds.Count == 0)
        {
            logger.LogInformation(
                "Dựng lại giá mua đơn: {Scanned} đơn có lần mua, không đơn nào lệch snapshot.",
                snapshots.Count);
            await MarkCompletedAsync(context, marker, cancellationToken);
            return;
        }

        var now = DateTime.UtcNow;
        var repaired = 0;
        var samples = new List<string>();

        foreach (var chunk in driftedIds.Chunk(ChunkSize))
        {
            var orders = await context.Orders
                .Include(o => o.SupplierPurchases)
                .Where(o => chunk.Contains(o.Id))
                .ToListAsync(cancellationToken);

            foreach (var order in orders)
            {
                var before = Snapshot(order);
                OrderSupplierCost.ApplyAggregates(order);
                if (before.SequenceEqual(Snapshot(order))) continue;

                order.UpdatedAt = now;
                repaired++;
                if (samples.Count < 20)
                {
                    // Số báo là giá mua sau khi dựng lại, đọc từ chính cột vừa ghi. Không so với
                    // "trước": `ResolveOrderTotalCost` có lưới an toàn lùi về SUM live nên trên đơn
                    // đã nạp kèm link nó trả cùng con số ở cả hai đầu, và đợt sửa trông như no-op.
                    samples.Add(
                        $"{order.PlatformOrderId ?? order.FourSellerOrderId.ToString()}="
                        + (OrderSupplierCost.ResolveOrderTotalCost(order)?.ToString() ?? "—"));
                }
            }

            await context.SaveChangesAsync(cancellationToken);
        }

        await MarkCompletedAsync(context, marker, cancellationToken);
        logger.LogInformation(
            "Dựng lại giá mua đơn: {Scanned} đơn có lần mua, {Drifted} lệch snapshot, "
            + "{Repaired} đơn dựng lại snapshot ({Samples}).",
            snapshots.Count,
            driftedIds.Count,
            repaired,
            string.Join(", ", samples));
    }

    /// <summary>
    /// Chụp cả 12 cột chứ không chỉ hai cột dùng để dò lệch: đợt sửa ghi cả 12, nên đây mới là
    /// phép đo đúng cho câu hỏi "đơn này có thực sự đổi gì không".
    /// </summary>
    private static decimal?[] Snapshot(Order order) =>
    [
        order.ChargedSupplierRetailPrice,
        order.ChargedSupplierSubtotal,
        order.ChargedSupplierShippingFee,
        order.ChargedSupplierRetailDeliveryFee,
        order.ChargedSupplierOnTimeGuarantee,
        order.ChargedSupplierCouponPrice,
        order.ChargedSupplierMembershipDiscount,
        order.ChargedSupplierPointsDiscount,
        order.ChargedSupplierWalletPrice,
        order.ChargedSupplierTax,
        order.ChargedSupplierTotal,
        order.ChargedTrueSpend
    ];

    /// <summary>
    /// NULL và 0 là hai câu trả lời khác nhau ("chưa lấy được" so với "bằng 0"), nên phép so phải
    /// phân biệt chúng thay vì coalesce cả hai về 0 — chính ca NULL-mà-đáng-ra-có-số là lỗi cần sửa.
    /// </summary>
    private static bool Differs(decimal? snapshot, decimal? live)
    {
        if (snapshot is null && live is null) return false;
        if (snapshot is null || live is null) return true;
        return Math.Abs(snapshot.Value - live.Value) >= 0.005m;
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
                Label = "Đã dựng lại snapshot giá mua đơn",
                Value = "done",
                Type = "text",
                Description = "Nội bộ: đánh dấu đợt dựng lại Orders.Charged* từ SupplierPurchases đã chạy xong."
            });
        }
        else
        {
            marker.Value = "done";
        }

        await context.SaveChangesAsync(cancellationToken);
    }
}
