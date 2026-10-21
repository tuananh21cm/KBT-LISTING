using System.Text.Json;
using System.Text.Json.Nodes;
using Application.DTOs.Order;
using Application.Services.Shein;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Shared.Constants;

namespace Infrastructure.Persistence.Seeding;

/// <summary>
/// Đọc lại <c>usedWalletPrice</c> / <c>retail_delivery_fee</c> từ <c>RawPayload</c> đã lưu
/// và ghi đè breakdown tiền SHEIN bị lệch ở lượt sync trước khi parser tách hai field này.
///
/// Case thật: GSU18V24000M10P / GSU18J2480004PR — thiếu wallet khiến reconcile đổ số ví vào coupon,
/// cột Coupon trên Lịch sử mua hàng hiện -$17.55 trong khi Wallet = $0.
///
/// <c>v5</c>: Amount sổ cái = SupplierTotal (totalPrice hóa đơn / thẻ) — không cộng Wallet;
/// Wallet chỉ nằm ở WalletAmount. Profit list = Revenue − TrueSpend (Amount + Wallet + Club).
/// </summary>
public static class SheinWalletCostBackfill
{
    /// <summary>Khoá setting đánh dấu đợt sửa đã chạy.</summary>
    public const string CompletedSettingKey = "maintenance.shein_wallet_retail_delivery_v5";

    /// <summary>Chạy một lần khi API khởi động nếu chưa đánh dấu xong.</summary>
    public static async Task RunAsync(
        ApplicationDbContext context,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        var marker = await context.Settings
            .FirstOrDefaultAsync(s => s.Key == CompletedSettingKey, cancellationToken);
        if (marker is not null && marker.Value == "done") return;

        var rows = await context.SupplierPurchases
            .Where(p =>
                p.RawPayload != null
                && p.Source == SupplierPurchaseSources.Shein
                && p.SupplierOrderNo != null)
            .ToListAsync(cancellationToken);

        var fixedPurchaseIds = new List<Guid>();
        foreach (var purchase in rows)
        {
            if (!TryRepairFromRaw(purchase)) continue;
            fixedPurchaseIds.Add(purchase.Id);
        }

        // Snapshot: Amount = Total (thẻ); Wallet/Coupon tách cột.
        var ledgerCandidates = await context.SupplierPurchases
            .Where(p => p.IsCharged)
            .Select(p => new
            {
                p.Id,
                Total = p.SupplierTotal,
                Wallet = p.SupplierWalletPrice,
                Coupon = p.SupplierCouponPrice,
            })
            .ToListAsync(cancellationToken);

        var ledgerFixed = 0;
        if (ledgerCandidates.Count > 0)
        {
            var ids = ledgerCandidates.Select(c => c.Id).ToList();
            var txns = await context.FulfilTransactions
                .Where(t =>
                    t.SupplierPurchaseId != null
                    && ids.Contains(t.SupplierPurchaseId.Value)
                    && t.Source == FulfilTransaction.SourceCostUpdate)
                .ToListAsync(cancellationToken);

            var byPurchase = ledgerCandidates.ToDictionary(c => c.Id);
            foreach (var txn in txns)
            {
                if (!byPurchase.TryGetValue(txn.SupplierPurchaseId!.Value, out var c)) continue;
                var wallet = c.Wallet ?? 0m;
                var coupon = c.Coupon ?? 0m;
                var cardAmount = c.Total ?? 0m;
                var changed =
                    Math.Abs(txn.Amount - cardAmount) >= 0.005m
                    || txn.CouponAmount != coupon
                    || txn.WalletAmount != wallet;
                if (!changed) continue;

                txn.Amount = cardAmount;
                txn.CouponAmount = coupon;
                txn.WalletAmount = wallet;
                txn.UpdatedAt = DateTime.UtcNow;
                ledgerFixed++;
            }
        }

        var stillSuspect = await context.SupplierPurchases
            .CountAsync(
                p => p.IsCharged
                    && p.Source == SupplierPurchaseSources.Shein
                    && p.SupplierTotal == 0m
                    && (p.SupplierCouponPrice ?? 0m) > 0m
                    && (p.SupplierWalletPrice ?? 0m) == 0m,
                cancellationToken);

        await context.SaveChangesAsync(cancellationToken);
        logger.LogInformation(
            "SheinWalletCostBackfill v5: sửa breakdown {PurchaseCount} lần mua, "
            + "cập nhật snapshot sổ cái {LedgerCount} dòng (Amount=Total thẻ), "
            + "còn nghi ví→coupon {SuspectCount}.",
            fixedPurchaseIds.Count,
            ledgerFixed,
            stillSuspect);

        await MarkCompletedAsync(context, marker, cancellationToken);
    }

    /// <summary>
    /// Re-parse detail (hoặc listItem) trong phong bì raw; ghi đè khi parse ra wallet hoặc retail delivery &gt; 0.
    /// </summary>
    internal static bool TryRepairFromRaw(SupplierPurchase purchase)
    {
        var payloadJson = ReadBestPayloadJson(purchase.RawPayload);
        if (string.IsNullOrWhiteSpace(payloadJson)) return false;

        JsonNode? node;
        try
        {
            node = JsonNode.Parse(payloadJson);
        }
        catch (JsonException)
        {
            return false;
        }

        if (node is null) return false;

        var billno = purchase.SupplierOrderNo ?? string.Empty;
        var dto = SheinOrderNormalizer.StubOrder(billno);
        SheinOrderNormalizer.ApplyDetail(node, dto, captureRaw: false);

        var wallet = dto.WalletPrice ?? 0m;
        var rdf = dto.RetailDeliveryFee ?? 0m;
        if (wallet <= 0m && rdf <= 0m) return false;
        if (!SheinOrderNormalizer.IsRichBreakdown(dto) && !HasSubAndTotal(dto)) return false;

        var replaceTotal = dto.Total is > 0m
            ? dto.Total
            : IsExplicitZeroTotal(dto) ? 0m : null;
        if (!replaceTotal.HasValue) return false;

        // Đã đúng sẵn → bỏ qua.
        if (Same(purchase.SupplierWalletPrice, dto.WalletPrice)
            && Same(purchase.SupplierRetailDeliveryFee, dto.RetailDeliveryFee)
            && Same(purchase.SupplierCouponPrice, dto.CouponPrice)
            && Same(purchase.SupplierMembershipDiscount, dto.ClubDiscount)
            && Same(purchase.SupplierTotal, replaceTotal))
        {
            return false;
        }

        purchase.SupplierRetailPrice = dto.RetailPrice ?? 0m;
        purchase.SupplierSubtotal = dto.Subtotal ?? 0m;
        purchase.SupplierShippingFee = dto.ShippingFee ?? 0m;
        purchase.SupplierRetailDeliveryFee = dto.RetailDeliveryFee ?? 0m;
        purchase.SupplierOnTimeGuarantee = dto.OnTimeGuarantee ?? 0m;
        purchase.SupplierCouponPrice = dto.CouponPrice ?? 0m;
        purchase.SupplierMembershipDiscount = dto.ClubDiscount ?? 0m;
        purchase.SupplierPointsDiscount = dto.PointsDiscount ?? 0m;
        purchase.SupplierWalletPrice = dto.WalletPrice ?? 0m;
        purchase.SupplierTax = dto.Tax ?? 0m;
        purchase.SupplierTotal = replaceTotal;
        purchase.UpdatedAt = DateTime.UtcNow;
        return true;
    }

    private static bool HasSubAndTotal(SheinOrderDto dto) =>
        (dto.Subtotal ?? 0m) > 0m && dto.Total is >= 0m;

    /// <summary>Total = 0 tường minh kèm tín hiệu breakdown (gồm wallet trả hết).</summary>
    private static bool IsExplicitZeroTotal(SheinOrderDto dto)
    {
        if (dto.Total is not 0m) return false;
        return (dto.RetailPrice ?? 0m) > 0m
            || (dto.Subtotal ?? 0m) > 0m
            || (dto.CouponPrice ?? 0m) > 0m
            || (dto.ClubDiscount ?? 0m) > 0m
            || (dto.PointsDiscount ?? 0m) > 0m
            || (dto.WalletPrice ?? 0m) > 0m
            || (dto.RetailDeliveryFee ?? 0m) > 0m
            || (dto.Tax ?? 0m) > 0m;
    }

    /// <summary>Ưu tiên detail; fallback listItem (một số đơn chỉ lưu list).</summary>
    private static string? ReadBestPayloadJson(string? envelopeJson)
    {
        var detail = ReadEnvelopePart(envelopeJson, "detail");
        if (!string.IsNullOrWhiteSpace(detail) && LooksLikeOrderJson(detail)) return detail;
        var listItem = ReadEnvelopePart(envelopeJson, "listItem");
        if (!string.IsNullOrWhiteSpace(listItem) && LooksLikeOrderJson(listItem)) return listItem;
        return detail ?? listItem;
    }

    private static bool LooksLikeOrderJson(string json) =>
        json.Contains("usedWalletPrice", StringComparison.OrdinalIgnoreCase)
        || json.Contains("used_wallet_price", StringComparison.OrdinalIgnoreCase)
        || json.Contains("totalPrice", StringComparison.OrdinalIgnoreCase)
        || json.Contains("newSubTotalPrice", StringComparison.OrdinalIgnoreCase);

    private static string? ReadEnvelopePart(string? envelopeJson, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(envelopeJson)) return null;
        try
        {
            using var doc = JsonDocument.Parse(envelopeJson);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            if (!root.TryGetProperty(propertyName, out var part)
                && !root.TryGetProperty(
                    char.ToUpperInvariant(propertyName[0]) + propertyName[1..],
                    out part))
            {
                return null;
            }

            return part.ValueKind == JsonValueKind.String ? part.GetString() : part.GetRawText();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool Same(decimal? a, decimal? b) =>
        a is null && b is null
        || a.HasValue && b.HasValue && Math.Abs(a.Value - b.Value) < 0.005m;

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
                Label = "Đã tách ví Shein; Amount sổ cái = Total thẻ (v5)",
                Value = "done",
                Type = "text",
                Description =
                    "Nội bộ: Wallet/Coupon đúng từ payload; CostUpdate Amount = SupplierTotal; "
                    + "WalletAmount tách; Profit list = Revenue − TrueSpend.",
            });
        }
        else
        {
            marker.Value = "done";
            marker.UpdatedAt = DateTime.UtcNow;
        }

        await context.SaveChangesAsync(cancellationToken);
    }
}
