using Application.Services;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Shared.Constants;

namespace Infrastructure.Persistence.Seeding;

/// <summary>
/// Gỡ một lần các billno SHEIN được mua <b>trước</b> khi đơn sàn được đặt.
///
/// Khớp billno với đơn sàn dựa trên phone + zip của người nhận, nên một khách mua lặp lại về cùng
/// địa chỉ sẽ trùng khoá với mọi đơn của chính họ — kể cả đơn đặt sau lần mua đó. Hàng đã về tay
/// khách trước khi đơn tồn tại thì không thể là hàng của đơn đó, nên chi phí, mốc Mua và mốc Giao
/// gắn theo đều sai. Ca thật: đơn <c>577499611162513640</c> đặt 29/07 16:04 nhận hai billno mua
/// 21/07 và ký nhận 29/07 13:04.
///
/// Luồng sync đã chặn từ gốc (<c>IsPurchasedBeforePlatformOrder</c>), nhưng bản ghi cũ vẫn nằm
/// trong DB và unique index <c>SupplierOrderNo</c> khiến chúng không tự biến mất — phải gỡ một lượt.
///
/// Chạy một lần mỗi phiên bản khoá, đánh dấu bằng setting để lần khởi động sau bỏ qua.
/// </summary>
public static class SheinCausalityLinkCleanup
{
    /// <summary>Khoá setting đánh dấu đợt gỡ đã chạy xong.</summary>
    public const string CompletedSettingKey = "maintenance.shein_causality_link_cleanup_v1";

    /// <summary>
    /// Cùng biên độ với <c>OrderSyncService.PlatformOrderCausalitySlack</c>: hai mốc đến từ hai hệ
    /// thống nên chênh vài giờ là bình thường, chỉ lệch lớn hơn ngưỡng mới là billno của đơn khác.
    /// </summary>
    private static readonly TimeSpan Slack = TimeSpan.FromHours(12);

    /// <summary>Chạy đợt gỡ nếu khoá setting chưa đánh dấu xong.</summary>
    /// <param name="context">DbContext của lượt seeding.</param>
    /// <param name="logger">Logger để mọi nhánh đều ghi lại kết quả.</param>
    /// <param name="cancellationToken">Token huỷ.</param>
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
                "Bỏ qua đợt gỡ billno ngược nhân quả: khoá {Key} đã đánh dấu xong.",
                CompletedSettingKey);
            return;
        }

        var orders = await context.Orders
            .Include(o => o.SupplierPurchases)
            .Where(o => o.OrderCreateTime != null && o.SupplierPurchases.Any())
            .ToListAsync(cancellationToken);

        // Mọi nhánh đều phải log: một đợt bảo trì im lặng không làm gì là thứ khó phát hiện nhất.
        if (orders.Count == 0)
        {
            logger.LogInformation("Đợt gỡ billno ngược nhân quả: không có đơn nào đã gắn billno.");
            await MarkCompletedAsync(context, marker, cancellationToken);
            return;
        }

        var staleByOrder = new List<(Order Order, List<SupplierPurchase> Links)>();
        foreach (var order in orders)
        {
            var placedAt = AsUtc(order.OrderCreateTime!.Value);
            var stale = (order.SupplierPurchases ?? [])
                .Where(l => PurchasedAt(l) is { } bought && placedAt - bought > Slack)
                .ToList();
            if (stale.Count > 0) staleByOrder.Add((order, stale));
        }

        if (staleByOrder.Count == 0)
        {
            logger.LogInformation(
                "Đợt gỡ billno ngược nhân quả: không có billno nào mua trước khi đơn được đặt ({Total} đơn).",
                orders.Count);
            await MarkCompletedAsync(context, marker, cancellationToken);
            return;
        }

        // Dòng sổ cái CostUpdate của billno bị gỡ phải xoá: khoản chi đó không thuộc đơn này.
        var staleIds = staleByOrder.SelectMany(x => x.Links).Select(l => l.Id).ToList();
        var ledgerRows = await context.FulfilTransactions
            .Where(t => t.SupplierPurchaseId != null
                && staleIds.Contains(t.SupplierPurchaseId.Value)
                && t.Source == FulfilTransaction.SourceCostUpdate)
            .ToListAsync(cancellationToken);
        context.FulfilTransactions.RemoveRange(ledgerRows);

        var now = DateTime.UtcNow;
        var removedLinks = 0;
        foreach (var (order, stale) in staleByOrder)
        {
            var placedAt = AsUtc(order.OrderCreateTime!.Value);
            foreach (var link in stale)
            {
                logger.LogWarning(
                    "Gỡ billno {Billno} khỏi đơn {OrderNo}: mua {BoughtAt:O} trước khi đơn được đặt {PlacedAt:O}.",
                    link.SupplierOrderNo,
                    order.PlatformOrderId ?? order.FourSellerOrderId.ToString(),
                    PurchasedAt(link),
                    placedAt);

                // Gỡ khỏi đơn nhưng giữ dòng mua: lần mua đó có thật, chỉ là không thuộc đơn này.
                // Nó quay về hàng đợi chưa gắn để người xử lý quyết định.
                order.SupplierPurchases!.Remove(link);
                link.OrderId = null;
                link.MatchState = SupplierPurchaseMatchStates.Unmatched;
                link.SkipReason = SheinSkipReasons.PurchasedBeforeOrder;
                link.MatchedAt = null;
                link.MatchedByUserId = null;
                link.IsCharged = false;
                link.UpdatedAt = now;
                removedLinks++;
            }

            OrderSupplierCost.ApplyAggregates(order);
            OrderSheinMilestoneEvidence.ClearMilestonesWithoutLinkEvidence(order);
            OrderPurchaseEvidence.ClearAttributionWhenNothingPurchased(order);
            order.UpdatedAt = DateTime.UtcNow;
        }

        var affectedOrders = staleByOrder.Count;

        await MarkCompletedAsync(context, marker, cancellationToken);
        logger.LogInformation(
            "Gỡ billno ngược nhân quả: {Links} link + {Ledger} dòng sổ cái trên {Orders}/{Total} đơn.",
            removedLinks,
            ledgerRows.Count,
            affectedOrders,
            orders.Count);
    }

    /// <summary>Mốc mua muộn nhất biết được, để không gỡ oan khi một trường vắng.</summary>
    private static DateTime? PurchasedAt(SupplierPurchase link)
    {
        DateTime? latest = null;
        foreach (var candidate in new[] { link.OrderedAt, link.SupplierPaidAt })
        {
            if (candidate is null) continue;
            var utc = AsUtc(candidate.Value);
            if (latest is null || utc > latest) latest = utc;
        }
        return latest;
    }

    private static DateTime AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };

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
                Label = "Đã gỡ billno mua trước khi đơn được đặt",
                Value = "done",
                Type = "text",
                Description = "Nội bộ: đánh dấu đợt gỡ billno ngược nhân quả đã chạy xong."
            });
        }
        else
        {
            marker.Value = "done";
        }

        await context.SaveChangesAsync(cancellationToken);
    }
}
