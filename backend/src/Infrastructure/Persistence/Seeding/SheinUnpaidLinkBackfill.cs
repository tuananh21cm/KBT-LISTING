using Application.Services;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Persistence.Seeding;

/// <summary>
/// Đánh dấu trạng thái thanh toán cho các billno SHEIN đã thu payload gốc, để loại đơn ghost khỏi
/// giá mua và khỏi mốc "Mua" ngay lập tức.
///
/// SHEIN cho tạo đơn trước rồi mới thu tiền và tự huỷ khi hết <c>order_expire_time</c> nếu không ai
/// trả. Đơn ghost đó vẫn có <c>addTime</c> nên trước đây được stamp "Đã mua", và vì là billno duy
/// nhất của đơn nên <c>OrderSupplierCost.SelectCharged</c> còn giữ nó lại làm chi phí — cả hai đều
/// là số bịa. Ca cụ thể: đơn TikTok 577494030896631910 với billno GSU18J247000TWB
/// (<c>pay_time = 0</c>, không gói hàng), trong khi đơn sàn được giao bằng mã SPX của người bán.
///
/// Lần sync SHEIN kế tiếp không tự sửa được vì billno đã link và không có delta nào nên
/// <c>HasSheinIncrementalDelta</c> bỏ qua. Vì vậy phải sửa một lần ở đây từ payload đã lưu.
///
/// Cố tình **không** xoá dòng link: bản ghi còn lại là dấu vết cho người đi đơn thấy lần mua đã
/// thất bại. Lượt Sync Fulfil kế tiếp mới gỡ hẳn billno ghost khỏi đơn sàn.
/// </summary>
public static class SheinUnpaidLinkBackfill
{
    /// <summary>
    /// Khoá setting đánh dấu đợt sửa đã chạy xong. <c>v1</c> chỉ đánh dấu được cờ chưa thanh toán
    /// trên link; <c>v2</c> gỡ thêm mốc "Mua" dương tính giả; <c>v3</c> xoá tiếp dòng chi CostUpdate
    /// mà đợt <c>SupplierCostBackfill</c> đã dựng cho các billno ghost đó.
    /// </summary>
    public const string CompletedSettingKey = "maintenance.shein_unpaid_ghost_links_v3";

    /// <summary>Chạy đợt sửa nếu setting đánh dấu chưa tồn tại; đã chạy rồi thì thoát ngay.</summary>
    public static async Task RunAsync(
        ApplicationDbContext context,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        var marker = await context.Settings
            .FirstOrDefaultAsync(s => s.Key == CompletedSettingKey, cancellationToken);
        if (marker is not null && marker.Value == "done") return;

        var captured = await context.SupplierPurchases
            .Where(l => l.RawPayload != null && l.OrderId != null)
            .Select(l => new { l.Id, OrderId = l.OrderId!.Value, l.RawPayload })
            .ToListAsync(cancellationToken);

        var paymentByLink = new Dictionary<Guid, SheinRawPaymentExtractor.Result>();
        var affectedOrderIds = new HashSet<Guid>();
        foreach (var row in captured)
        {
            var parsed = SheinRawPaymentExtractor.Extract(row.RawPayload);
            if (parsed.PaidAt is null && !parsed.Unpaid) continue;

            paymentByLink[row.Id] = parsed;
            affectedOrderIds.Add(row.OrderId);
        }

        if (paymentByLink.Count == 0)
        {
            await MarkCompletedAsync(context, marker, cancellationToken);
            return;
        }

        var orders = await context.Orders
            .Include(o => o.SupplierPurchases)
            .Where(o => affectedOrderIds.Contains(o.Id))
            .ToListAsync(cancellationToken);

        var ghostLinks = 0;
        var repricedOrders = 0;
        var clearedPurchased = 0;
        var ghostLinkIds = new List<Guid>();

        foreach (var order in orders)
        {
            var links = order.SupplierPurchases ?? [];
            foreach (var link in links)
            {
                if (!paymentByLink.TryGetValue(link.Id, out var parsed)) continue;

                if (parsed.PaidAt is not null)
                {
                    link.SupplierPaidAt = parsed.PaidAt;
                    link.SupplierUnpaid = false;
                }
                else if (parsed.Unpaid)
                {
                    link.SupplierUnpaid = true;
                    ghostLinkIds.Add(link.Id);
                    ghostLinks++;
                }
            }

            var beforeCharged = links.Where(l => l.IsCharged).Select(l => l.Id).ToHashSet();
            OrderSupplierCost.ApplyAggregates(order);
            if (!beforeCharged.SetEquals(links.Where(l => l.IsCharged).Select(l => l.Id)))
            {
                repricedOrders++;
            }

            if (OrderPurchaseEvidence.ClearPurchasedWhenOnlyGhostBillnosRemain(order))
            {
                clearedPurchased++;
            }

            order.UpdatedAt = DateTime.UtcNow;
        }

        // Đợt SupplierCostBackfill trước đây đã dựng dòng chi CostUpdate cho chính những billno này,
        // nên số dư tài khoản mua hàng vẫn đang bị trừ tiền cho lần mua chưa từng xảy ra.
        var staleLedgerRows = ghostLinkIds.Count == 0
            ? []
            : await context.FulfilTransactions
                .Where(t => t.SupplierPurchaseId != null
                    && ghostLinkIds.Contains(t.SupplierPurchaseId.Value)
                    && t.Source == FulfilTransaction.SourceCostUpdate)
                .ToListAsync(cancellationToken);
        if (staleLedgerRows.Count > 0)
        {
            context.FulfilTransactions.RemoveRange(staleLedgerRows);
        }

        await MarkCompletedAsync(context, marker, cancellationToken);
        logger.LogInformation(
            "Đánh dấu thanh toán SHEIN từ payload gốc: {Ghost} billno chưa trả tiền, "
            + "{Repriced} đơn đổi giá mua, {Cleared} đơn gỡ mốc Mua, {Ledger} dòng chi CostUpdate đã "
            + "xoá (trên {Total} đơn có payload).",
            ghostLinks,
            repricedOrders,
            clearedPurchased,
            staleLedgerRows.Count,
            orders.Count);
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
                Label = "Đã đánh dấu billno SHEIN chưa thanh toán",
                Value = "done",
                Type = "text",
                Description = "Nội bộ: đánh dấu đợt loại đơn SHEIN ghost khỏi giá mua đã chạy xong."
            });
        }
        else
        {
            marker.Value = "done";
        }

        await context.SaveChangesAsync(cancellationToken);
    }
}
