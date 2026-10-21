using Domain.Entities;
using Domain.Orders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Persistence.Seeding;

/// <summary>
/// One-shot: recompute <see cref="Order.SellerNetAmount"/> from stored TikTok breakdown
/// after platform discount was excluded again from seller net
/// (TikTok/platform shipping still ignores net ship).
/// </summary>
public static class SellerNetPlatformDiscountBackfill
{
    /// <summary>Khoá setting đánh dấu đợt sửa đã chạy xong.</summary>
    public const string CompletedSettingKey = "orders.seller_net.exclude_platform_discount_v1";

    private const int BatchSize = 500;

    /// <summary>Chạy một lần: recompute seller net từ cột local (không gọi 4Seller).</summary>
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

        var scanned = 0;
        var changed = 0;
        var lastId = Guid.Empty;

        while (!cancellationToken.IsCancellationRequested)
        {
            var batch = await context.Orders
                .Where(o =>
                    o.PricingDetailSyncedAt != null
                    && o.ProductAmountBeforeDiscount != null
                    && o.Id > lastId)
                .OrderBy(o => o.Id)
                .Take(BatchSize)
                .ToListAsync(cancellationToken);

            if (batch.Count == 0)
            {
                break;
            }

            lastId = batch[^1].Id;
            scanned += batch.Count;
            var nowUtc = DateTime.UtcNow;

            foreach (var order in batch)
            {
                var next = TikTokSellerNetCalculator.Compute(
                    order.ProductAmountBeforeDiscount ?? 0,
                    order.ProductSellerDiscount ?? 0,
                    order.ProductPlatformDiscount ?? 0,
                    order.ShippingFeeBeforeDiscount ?? 0,
                    order.ShippingFeeSellerDiscount ?? 0,
                    order.PlatformTotalTax ?? 0,
                    order.PlatformOtherFee ?? 0,
                    order.ShippingMethod);

                if (order.SellerNetAmount == next)
                {
                    continue;
                }

                order.SellerNetAmount = next;
                order.UpdatedAt = nowUtc;
                changed++;
            }

            await context.SaveChangesAsync(cancellationToken);

            foreach (var entry in context.ChangeTracker.Entries<Order>().ToList())
            {
                entry.State = EntityState.Detached;
            }
        }

        if (marker is null)
        {
            context.Settings.Add(new Setting
            {
                Group = "Bảo trì dữ liệu",
                Key = CompletedSettingKey,
                Label = "Đã recompute SellerNet (không trừ platform discount)",
                Value = "done",
                Type = "text",
                Description =
                    "Nội bộ: đợt một lần tính lại SellerNetAmount — ProductPlatformDiscount chỉ display; TikTok ship vẫn không ± netShip."
            });
        }
        else
        {
            marker.Value = "done";
        }

        await context.SaveChangesAsync(cancellationToken);
        logger.LogInformation(
            "SellerNetPlatformDiscountBackfill: quét {Scanned} đơn đã enrich, cập nhật {Changed} SellerNetAmount.",
            scanned,
            changed);
    }
}
