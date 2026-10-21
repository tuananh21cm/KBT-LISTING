using System.Globalization;
using System.Net;
using System.Text;
using Application.DTOs.Order;
using Application.DTOs.Shein;
using Application.Interfaces;
using Application.Services;
using Domain.Entities;
using Domain.Orders;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shared.Constants;
using Shared.Helpers;

namespace Infrastructure.Integrations.Telegram;

/// <summary>
/// Settings-gated Telegram notifications for new orders, cancel/refund, fulfil handling,
/// stale no-tracking, and daily summaries.
/// BotToken from appsettings; chat ids + toggles/schedules from Settings DB (orders chat falls back to appsettings).
/// </summary>
public class TelegramNotifier : ITelegramNotifier
{
    private const int ProductTitleMaxChars = 38;
    /// <summary>Max display width for new-order line <c>• $amount • title</c> (mobile one-liner).</summary>
    private const int NewOrderAmountProductLineMaxChars = 36;
    private const int TelegramMessageMaxChars = 4000;
    private static readonly TimeSpan WaitingPurchaseOverdue = TimeSpan.FromHours(12);

    private static readonly string[] NewOrderMotivations =
    [
        "🔥 Thu nhập là phần thưởng của nỗ lực!",
        "🔥 Thành công bắt đầu từ kỷ luật!",
        "🔥 Mỗi đơn là một bước tiến!",
        "🔥 Cơ hội luôn dành cho người bền bỉ!",
        "🔥 Đừng dừng lại khi đang tiến lên!",
        "🔥 Mỗi đơn đều đáng giá!",
        "🔥 Kiên trì sẽ tạo nên khác biệt!",
        "🔥 Cứ tiến lên, thành công sẽ đến!",
        "🔥 Mỗi đơn là một chiến thắng!",
        "🔥 Thu nhập tăng khi giá trị tăng.",
        "🔥 Kỷ luật hôm nay, tự do ngày mai.",
        "🔥 Cố thêm một chút, tiến thêm một bước!",
        "🔥 Nỗ lực hôm nay tạo nên tương lai!",
        "🔥 Cứ tiến lên, thành quả sẽ đến!",
        "🔥 Một bước nhỏ, thành công lớn!",
        "🔥 Bền bỉ là chìa khóa chiến thắng!",
        "🔥 Hành động mỗi ngày, thu nhập mỗi tháng!",
        "🔥 Mọi thành quả đều cần cố gắng!",
        "🔥 Giữ vững phong độ, tiếp tục bứt phá!",
        "🔥 Thành công thuộc về người hành động!",
    ];

    private static readonly TimeZoneInfo VietnamTz = ResolveVietnamTimeZone();

    private readonly ISettingProvider _settings;
    private readonly IOptions<TelegramSettings> _telegramOptions;
    private readonly IApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly TelegramApiService _api;
    private readonly ILogger<TelegramNotifier> _logger;

    /// <summary>
    /// Initializes the notifier.
    /// </summary>
    public TelegramNotifier(
        ISettingProvider settings,
        IOptions<TelegramSettings> telegramOptions,
        IApplicationDbContext db,
        UserManager<ApplicationUser> userManager,
        TelegramApiService api,
        ILogger<TelegramNotifier> logger)
    {
        _settings = settings;
        _telegramOptions = telegramOptions;
        _db = db;
        _userManager = userManager;
        _api = api;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<int> NotifyNewOrdersAsync(
        IReadOnlyList<Order> orders,
        IReadOnlyDictionary<Guid, string?>? shopNameFallbacks = null,
        CancellationToken cancellationToken = default)
    {
        return await SendOrderNotificationsAsync(
            orders,
            shopNameFallbacks,
            isCancelRefund: false,
            BuildNewOrderMessage,
            "new-order",
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task NotifyCanceledOrdersAsync(
        IReadOnlyList<Order> orders,
        IReadOnlyDictionary<Guid, string?>? shopNameFallbacks = null,
        CancellationToken cancellationToken = default)
    {
        await SendOrderNotificationsAsync(
            orders,
            shopNameFallbacks,
            isCancelRefund: true,
            (order, shop, fallback, manager, sheinPurchased) =>
                BuildCanceledOrderMessage(order, shop, fallback, manager, sheinPurchased),
            "canceled-order",
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<int> NotifyAfterSalesOrdersAsync(
        IReadOnlyList<OrderAfterSalesNotice> notices,
        CancellationToken cancellationToken = default)
    {
        if (notices.Count == 0)
        {
            return 0;
        }

        var values = await _settings.GetValuesAsync(
            [AfterSalesSyncSettingKeys.NotifyEnabled, AfterSalesSyncSettingKeys.TelegramChatId],
            cancellationToken);
        values.TryGetValue(AfterSalesSyncSettingKeys.NotifyEnabled, out var notifyEnabled);
        values.TryGetValue(AfterSalesSyncSettingKeys.TelegramChatId, out var chatIdSetting);

        if (!IsTruthy(notifyEnabled))
        {
            return 0;
        }

        var chatId = FirstNonEmpty(chatIdSetting, AfterSalesSyncSettingKeys.DefaultTelegramChatId)!.Trim();
        var noticeByOrderId = notices
            .GroupBy(n => n.Order.Id)
            .ToDictionary(g => g.Key, g => g.Last());

        return await SendOrderNotificationsAsync(
            notices.Select(n => n.Order).ToList(),
            noticeByOrderId.ToDictionary(kv => kv.Key, kv => kv.Value.ShopNameFallback),
            isCancelRefund: true,
            (order, shop, fallback, manager, sheinPurchased) =>
                BuildAfterSalesOrderMessage(
                    order, shop, fallback, manager, sheinPurchased, noticeByOrderId[order.Id]),
            "after-sales-order",
            cancellationToken,
            chatIdOverride: chatId);
    }

    // chatIdOverride routes to a dedicated group and skips the cancel/refund toggle: the
    // after-sales flow has its own on/off switch, and reusing that toggle would let one setting
    // silence two features.
    private async Task<int> SendOrderNotificationsAsync(
        IReadOnlyList<Order> orders,
        IReadOnlyDictionary<Guid, string?>? shopNameFallbacks,
        bool isCancelRefund,
        Func<Order, Shop?, string?, string?, bool, string> buildMessage,
        string notificationKind,
        CancellationToken cancellationToken,
        string? chatIdOverride = null)
    {
        if (orders.Count == 0)
        {
            return 0;
        }

        var sent = 0;
        try
        {
            var config = await LoadConfigAsync(cancellationToken);
            if (config is null)
            {
                return 0;
            }

            string? targetChatId;
            if (!string.IsNullOrWhiteSpace(chatIdOverride))
            {
                targetChatId = chatIdOverride.Trim();
            }
            else if (isCancelRefund)
            {
                if (!IsTruthy(config.NotifyCancelRefund) || string.IsNullOrWhiteSpace(config.CancelRefundChatId))
                {
                    return 0;
                }

                targetChatId = config.CancelRefundChatId;
            }
            else
            {
                if (!IsTruthy(config.NotifyNewOrder) || string.IsNullOrWhiteSpace(config.OrdersChatId))
                {
                    return 0;
                }

                targetChatId = config.OrdersChatId;
            }

            var shopIds = orders
                .Where(o => o.ShopId.HasValue)
                .Select(o => o.ShopId!.Value)
                .Distinct()
                .ToList();

            var shops = shopIds.Count == 0
                ? new Dictionary<Guid, Shop>()
                : await _db.Shops
                    .AsNoTracking()
                    .Where(s => shopIds.Contains(s.Id))
                    .ToDictionaryAsync(s => s.Id, cancellationToken);

            var managerUserIds = shops.Values
                .Select(s => s.OwnerId)
                .Concat(orders.Select(o => o.OwnerId))
                .Distinct()
                .ToList();

            var managerNames = managerUserIds.Count == 0
                ? new Dictionary<Guid, string>()
                : await _db.Users
                    .AsNoTracking()
                    .Where(u => managerUserIds.Contains(u.Id))
                    .Select(u => new { u.Id, u.FullName, u.UserName })
                    .ToDictionaryAsync(
                        u => u.Id,
                        u => string.IsNullOrWhiteSpace(u.FullName) ? (u.UserName ?? u.Id.ToString()) : u.FullName,
                        cancellationToken);

            var sheinPurchasedByOrderId = isCancelRefund
                ? await LoadSheinPurchasedFlagsAsync(orders, cancellationToken)
                : null;

            var now = DateTime.UtcNow;
            foreach (var order in orders)
            {
                if (!isCancelRefund)
                {
                    if (order.TelegramNewOrderNotifiedAt is not null)
                    {
                        continue;
                    }
                }

                if (!HasCompleteProductInfo(order))
                {
                    _logger.LogInformation(
                        "Skip Telegram {NotificationKind} notify for order {OrderId}: product info incomplete",
                        notificationKind,
                        order.Id);
                    continue;
                }

                shops.TryGetValue(order.ShopId ?? Guid.Empty, out var shop);
                string? fallbackName = null;
                shopNameFallbacks?.TryGetValue(order.Id, out fallbackName);

                // New-order must resolve a local shop — otherwise seller name falls back to the
                // (possibly wrong) sync OwnerId and Profile/Shop render as "—".
                if (!isCancelRefund && shop is null)
                {
                    _logger.LogInformation(
                        "Skip Telegram {NotificationKind} notify for order {OrderId}: shop unresolved (fourSellerShopId={FourSellerShopId})",
                        notificationKind,
                        order.Id,
                        order.FourSellerShopId);
                    continue;
                }

                // Claim stamp in DB before send so sync and the pending sweep cannot both
                // deliver the same "new order" message (ExecuteUpdate is cross-context atomic).
                if (!isCancelRefund
                    && !await TryClaimNewOrderNotifyAsync(order, now, cancellationToken))
                {
                    continue;
                }

                var managerUserId = shop?.OwnerId ?? order.OwnerId;
                managerNames.TryGetValue(managerUserId, out var managerFullName);

                var sheinPurchased = false;
                if (sheinPurchasedByOrderId is not null)
                {
                    if (!sheinPurchasedByOrderId.TryGetValue(order.Id, out sheinPurchased))
                    {
                        sheinPurchased = order.FulfilPurchasedAt.HasValue
                            || (order.SupplierPurchases is { Count: > 0 });
                    }
                }

                var text = buildMessage(order, shop, fallbackName, managerFullName, sheinPurchased);

                var photoUrl = order.Items
                    .Select(i => i.ImgUrl)
                    .FirstOrDefault(u => !string.IsNullOrWhiteSpace(u));

                try
                {
                    await _api.SendHtmlMessageAsync(
                        config.BotToken,
                        targetChatId,
                        text,
                        previewUrl: string.IsNullOrWhiteSpace(photoUrl) ? null : photoUrl!.Trim(),
                        cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    if (!isCancelRefund)
                    {
                        await ReleaseNewOrderNotifyClaimAsync(order, cancellationToken);
                    }

                    _logger.LogWarning(
                        ex,
                        "Telegram {NotificationKind} send failed for order {OrderId}; claim released for retry",
                        notificationKind,
                        order.Id);
                    continue;
                }

                sent++;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to send Telegram {NotificationKind} notifications for {Count} orders",
                notificationKind,
                orders.Count);
        }

        return sent;
    }

    private async Task<Dictionary<Guid, bool>> LoadSheinPurchasedFlagsAsync(
        IReadOnlyList<Order> orders,
        CancellationToken cancellationToken)
    {
        var orderIds = orders.Select(o => o.Id).Where(id => id != Guid.Empty).Distinct().ToList();
        if (orderIds.Count == 0)
        {
            return new Dictionary<Guid, bool>();
        }

        return await _db.Orders
            .AsNoTracking()
            .Where(o => orderIds.Contains(o.Id))
            .Select(o => new
            {
                o.Id,
                Purchased = o.FulfilPurchasedAt != null || o.SupplierPurchases.Any()
            })
            .ToDictionaryAsync(x => x.Id, x => x.Purchased, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> TrySendDailySummaryAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var config = await LoadConfigAsync(cancellationToken);
            if (config is null
                || !IsTruthy(config.DailySummaryEnabled)
                || string.IsNullOrWhiteSpace(config.OrdersChatId))
            {
                return false;
            }

            var localNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, VietnamTz);
            TelegramSummarySchedule.TryParseLocalClock(config.DailySummaryHour, out var hour, out var minute);
            if (!TelegramSummarySchedule.TryResolveSummaryLocalDate(
                    localNow, hour, minute, config.DailySummaryLastSentDate, out var summaryDate))
            {
                return false;
            }

            var summaryDateKey = summaryDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var dayStartLocal = summaryDate;
            var dayEndLocal = dayStartLocal.AddDays(1);
            var dayStartUtc = TimeZoneInfo.ConvertTimeToUtc(dayStartLocal, VietnamTz);
            var dayEndUtc = TimeZoneInfo.ConvertTimeToUtc(dayEndLocal, VietnamTz);

            var dayOrders = await _db.Orders
                .AsNoTracking()
                .Where(o =>
                    (o.OrderCreateTime ?? o.OrderPaidTime ?? o.CreatedAt) >= dayStartUtc &&
                    (o.OrderCreateTime ?? o.OrderPaidTime ?? o.CreatedAt) < dayEndUtc)
                .Select(o => o.PlatformOrderStatus)
                .ToListAsync(cancellationToken);

            var onHold = CountStatus(dayOrders, "ON_HOLD");
            var awaitingShipment = CountStatus(dayOrders, "AWAITING_SHIPMENT");
            var awaitingCollection = CountStatus(dayOrders, "AWAITING_COLLECTION");
            var inTransit = CountStatus(dayOrders, "IN_TRANSIT");
            var delivered = CountStatus(dayOrders, "DELIVERED");
            var canceled = CountStatus(dayOrders, "CANCELLED") + CountStatus(dayOrders, "CANCELED");

            var text =
                $"Tổng hợp đơn hôm nay ({summaryDate:dd/MM/yyyy})\n" +
                $"• Tạm giữ: {onHold}\n" +
                $"• Chờ giao: {awaitingShipment}\n" +
                $"• Chờ lấy hàng: {awaitingCollection}\n" +
                $"• Đang vận chuyển: {inTransit}\n" +
                $"• Đã giao: {delivered}\n" +
                $"• Đã hủy: {canceled}";

            await _api.SendHtmlMessageAsync(
                config.BotToken, config.OrdersChatId, text, previewUrl: null, cancellationToken);
            await _settings.SetValueAsync(TelegramSettingKeys.DailySummaryLastSentDate, summaryDateKey, cancellationToken);
            _logger.LogInformation("Sent Telegram daily order summary for {Date}", summaryDateKey);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send Telegram daily order summary");
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<bool> TrySendSellerSummaryAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var config = await LoadConfigAsync(cancellationToken);
            if (config is null
                || !IsTruthy(config.SellerSummaryEnabled)
                || string.IsNullOrWhiteSpace(config.OrdersChatId))
            {
                return false;
            }

            var localNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, VietnamTz);
            TelegramSummarySchedule.TryParseLocalClock(config.SellerSummaryHour, out var hour, out var minute);
            if (!TelegramSummarySchedule.TryResolveSummaryLocalDate(
                    localNow, hour, minute, config.SellerSummaryLastSentDate, out var summaryDate))
            {
                return false;
            }

            return await SendSellerSummaryForLocalDateAsync(config, summaryDate, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send Telegram seller summary");
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<bool> TrySendFulfilSummaryAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var config = await LoadConfigAsync(cancellationToken);
            if (config is null
                || !IsTruthy(config.FulfilSummaryEnabled)
                || string.IsNullOrWhiteSpace(config.CancelRefundChatId))
            {
                return false;
            }

            var localNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, VietnamTz);
            TelegramSummarySchedule.TryParseLocalClock(config.FulfilSummaryHour, out var hour, out var minute);
            if (!TelegramSummarySchedule.TryResolveSummaryLocalDate(
                    localNow, hour, minute, config.FulfilSummaryLastSentDate, out var summaryDate))
            {
                return false;
            }

            return await SendFulfilSummaryForLocalDateAsync(config, summaryDate, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send Telegram fulfil summary");
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<bool> ForceSendSellerSummaryForLocalDateAsync(
        DateTime summaryLocalDate,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var config = await LoadConfigAsync(cancellationToken);
            if (config is null || string.IsNullOrWhiteSpace(config.OrdersChatId))
            {
                _logger.LogWarning("Force seller summary skipped: Telegram BotToken/OrdersChatId missing");
                return false;
            }

            var day = summaryLocalDate.Date;
            return await SendSellerSummaryForLocalDateAsync(config, day, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to force-send Telegram seller summary for {Date}", summaryLocalDate);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<bool> ForceSendSheinDetailAddTrackTestAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var botToken = _telegramOptions.Value.BotToken?.Trim();
            if (string.IsNullOrWhiteSpace(botToken))
            {
                _logger.LogWarning("Force Add-track test skipped: Telegram BotToken missing");
                return false;
            }

            var chatId = FirstNonEmpty(
                await _settings.GetValueAsync(
                    SheinDetailSyncSettingKeys.TelegramChatId,
                    cancellationToken));
            if (chatId is null)
            {
                _logger.LogWarning(
                    "Force Add-track test skipped: missing {Key}",
                    SheinDetailSyncSettingKeys.TelegramChatId);
                return false;
            }

            var now = DateTime.UtcNow;
            var orderedAt = now.AddDays(-3).AddHours(-2);
            var link = new SupplierPurchase
            {
                Id = Guid.NewGuid(),
                SupplierOrderNo = "TEST-BILLNO",
                TrackingKind = SupplierTrackingKinds.Carrier,
                TrackingNo = "TESTTRACK0000000000",
                Carrier = "SpeedX",
                AutoMarkShipStatus = AutoMarkShipStatuses.Succeeded
            };
            var order = new Order
            {
                Id = Guid.NewGuid(),
                OwnerId = Guid.NewGuid(),
                PlatformOrderId = "TEST-ADD-TRACK",
                OrderCreateTime = orderedAt,
                FulfilPurchasedAt = orderedAt.AddHours(6),
                Items = [new OrderItem { Title = "TEST — Add track sample (bỏ qua)" }]
            };

            var text = BuildSheinDetailAddTrackMessage(
                new SheinDetailAddTrackNotifyItem(order, link, link.SupplierOrderNo, now),
                profileName: "TEST-PROFILE",
                managerFullName: "TEST Seller");
            // Đánh dấu rõ đây là tin thử — không phải đơn thật.
            text = text.Replace(
                "📦 <b>Add track</b> · ✅ Thành công",
                "📦 <b>Add track</b> · ✅ Thành công <i>(TEST)</i>",
                StringComparison.Ordinal);

            await _api.SendHtmlMessageAsync(botToken, chatId, text, previewUrl: null, cancellationToken);
            _logger.LogInformation(
                "Telegram SHEIN add-track TEST sent: chatId={ChatId}",
                chatId);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to force-send Telegram SHEIN add-track test");
            return false;
        }
    }

    /// <inheritdoc />
    public async Task NotifyShopProfileAlertAsync(
        string kind,
        string sellerDisplayName,
        string? profileName,
        string? shopName,
        string? oldValue,
        string? newValue,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var opts = _telegramOptions.Value;
            var botToken = opts.BotToken?.Trim();
            if (string.IsNullOrWhiteSpace(botToken))
            {
                return;
            }

            var chatId = await _settings.GetValueAsync(
                ExtensionTikTokShopSyncSettingKeys.TelegramChatId,
                cancellationToken);
            chatId = string.IsNullOrWhiteSpace(chatId)
                ? ExtensionTikTokShopSyncSettingKeys.DefaultTelegramChatId
                : chatId.Trim();
            if (string.IsNullOrWhiteSpace(chatId))
            {
                return;
            }

            var text = BuildShopProfileAlertMessage(
                kind,
                sellerDisplayName,
                profileName,
                shopName,
                oldValue,
                newValue);
            await _api.SendHtmlMessageAsync(botToken, chatId, text, previewUrl: null, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to send TikTok shop profile Telegram alert kind={Kind}",
                kind);
        }
    }

    /// <inheritdoc />
    public async Task NotifyShopStatusChangeAsync(
        Shop shop,
        string previousStatus,
        string sellerDisplayName,
        string changeSource,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var opts = _telegramOptions.Value;
            var botToken = opts.BotToken?.Trim();
            if (string.IsNullOrWhiteSpace(botToken))
            {
                return;
            }

            var chatId = await _settings.GetValueAsync(
                ExtensionTikTokShopSyncSettingKeys.TelegramChatId,
                cancellationToken);
            chatId = string.IsNullOrWhiteSpace(chatId)
                ? ExtensionTikTokShopSyncSettingKeys.DefaultTelegramChatId
                : chatId.Trim();
            if (string.IsNullOrWhiteSpace(chatId))
            {
                return;
            }

            var text = BuildShopStatusChangeMessage(shop, previousStatus, sellerDisplayName, changeSource);
            await _api.SendHtmlMessageAsync(botToken, chatId, text, previewUrl: null, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to send shop status change Telegram alert for shop {ShopId}",
                shop.Id);
        }
    }

    /// <inheritdoc />
    public async Task NotifyNewTikTokPayoutAsync(
        string sellerDisplayName,
        string? profileName,
        string? shopName,
        decimal amount,
        string currency,
        DateTime occurredAtUtc,
        string paymentId,
        string? receivingCardSourceName,
        string? receivingCardMaskedNumber,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var opts = _telegramOptions.Value;
            var botToken = opts.BotToken?.Trim();
            if (string.IsNullOrWhiteSpace(botToken))
            {
                return;
            }

            var chatId = await _settings.GetValueAsync(
                ExtensionTikTokPayoutSyncSettingKeys.TelegramChatId,
                cancellationToken);
            chatId = string.IsNullOrWhiteSpace(chatId)
                ? ExtensionTikTokPayoutSyncSettingKeys.DefaultTelegramChatId
                : chatId.Trim();
            if (string.IsNullOrWhiteSpace(chatId))
            {
                return;
            }

            var text = BuildNewTikTokPayoutMessage(
                sellerDisplayName,
                profileName,
                shopName,
                amount,
                currency,
                occurredAtUtc,
                paymentId,
                receivingCardSourceName,
                receivingCardMaskedNumber);
            await _api.SendHtmlMessageAsync(botToken, chatId, text, previewUrl: null, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send TikTok payout Telegram alert");
        }
    }

    private static string BuildNewTikTokPayoutMessage(
        string sellerDisplayName,
        string? profileName,
        string? shopName,
        decimal amount,
        string currency,
        DateTime occurredAtUtc,
        string paymentId,
        string? receivingCardSourceName,
        string? receivingCardMaskedNumber)
    {
        var seller = EscapeHtml(FirstNonEmpty(sellerDisplayName, "Seller")!);
        var profile = EscapeHtml(NullDash(FirstNonEmpty(profileName)));
        var shop = EscapeHtml(NullDash(FirstNonEmpty(shopName)));
        var amountText = EscapeHtml(FormatPayoutAmount(amount, currency));
        var when = EscapeHtml(FormatVnPayoutDateTime(occurredAtUtc));
        var txnId = EscapeHtml(FirstNonEmpty(paymentId, "—")!);
        var cardParts = new[]
            {
                FirstNonEmpty(receivingCardSourceName),
                FirstNonEmpty(receivingCardMaskedNumber)
            }
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => EscapeHtml(s!))
            .ToList();
        var cardLine = cardParts.Count > 0
            ? string.Join(" ", cardParts)
            : EscapeHtml("—");

        // 🔥 {Seller} vừa về tiền
        // • $x • HH:mm dd/MM/yyyy
        // • {Profile} • {Shop}
        // • {paymentId}
        // • {SourceName} {maskedAccount}
        var sb = new StringBuilder();
        sb.Append("🔥 <b>")
            .Append(seller)
            .Append("</b> vừa về tiền\n")
            .Append("• <b>")
            .Append(amountText)
            .Append("</b> • ")
            .Append(when)
            .Append('\n')
            .Append("• ")
            .Append(profile)
            .Append(" • ")
            .Append(shop)
            .Append('\n')
            .Append("• <code>")
            .Append(txnId)
            .Append("</code>\n")
            .Append("• ")
            .Append(cardLine);
        return sb.ToString();
    }

    /// <summary>VN local: <c>09:05 25/06/2026</c>.</summary>
    private static string FormatVnPayoutDateTime(DateTime utc)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), VietnamTz);
        return local.ToString("HH:mm dd/MM/yyyy", CultureInfo.InvariantCulture);
    }

    private static string FormatPayoutAmount(decimal amount, string? currency)
    {
        var code = string.IsNullOrWhiteSpace(currency) ? "USD" : currency.Trim().ToUpperInvariant();
        var num = amount.ToString("0.##", CultureInfo.InvariantCulture);
        if (code == "USD")
        {
            return "$" + num;
        }

        return num + " " + code;
    }

    private static string BuildShopProfileAlertMessage(
        string kind,
        string sellerDisplayName,
        string? profileName,
        string? shopName,
        string? oldValue,
        string? newValue)
    {
        var action = kind switch
        {
            "email" => "thay đổi email",
            "mobile" => "thay đổi mobile",
            "bank" => "thay đổi bank",
            "bank_last4" => "bank không khớp hệ thống",
            _ => "thay đổi profile"
        };

        var seller = EscapeHtml(FirstNonEmpty(sellerDisplayName, "Seller")!);
        var profile = EscapeHtml(NullDash(FirstNonEmpty(profileName)));
        var shop = EscapeHtml(NullDash(FirstNonEmpty(shopName)));

        var sb = new StringBuilder();
        sb.Append("SHOP :")
            .Append(seller)
            .Append(' ')
            .Append(profile)
            .Append(' ')
            .Append(shop)
            .Append(' ')
            .Append(EscapeHtml(action));

        if (string.Equals(kind, "bank_last4", StringComparison.OrdinalIgnoreCase))
        {
            sb.Append('\n')
                .Append(EscapeHtml(MaskForTelegram(newValue, kind)));
        }
        else
        {
            sb.Append('\n')
                .Append(EscapeHtml(MaskForTelegram(oldValue, kind)))
                .Append(" sang ")
                .Append(EscapeHtml(MaskForTelegram(newValue, kind)));
        }

        sb.Append("\nTime :  ")
            .Append(EscapeHtml(FormatVnDateTimeFull(DateTime.UtcNow)));

        return sb.ToString();
    }

    private static string BuildShopStatusChangeMessage(
        Shop shop,
        string previousStatus,
        string sellerDisplayName,
        string changeSource)
    {
        var newStatus = shop.Status ?? string.Empty;
        var emoji = newStatus switch
        {
            ShopStatuses.Active => "✅",
            ShopStatuses.Deactivated => "🚫",
            ShopStatuses.Suspended => "⛔",
            _ => "⚠️"
        };

        var seller = EscapeHtml(FirstNonEmpty(sellerDisplayName, "Seller")!);
        var profile = EscapeHtml(NullDash(FirstNonEmpty(shop.ProfileName)));
        var shopName = EscapeHtml(NullDash(FirstNonEmpty(shop.Name)));

        // 🚫 TikTok đã deactive shop
        // • Active → Deactivated
        // • Seller: {Seller} • {Profile} • {Shop}
        // • {ShopCode} • {ExternalShopId} • {Region} • TTS {label}
        // • {ShopUrl}
        // Time :  HH:mm:ss d/M/yyyy
        var sb = new StringBuilder();
        sb.Append(emoji)
            .Append(' ')
            .Append(EscapeHtml(BuildShopStatusHeadline(changeSource, newStatus)))
            .Append('\n')
            .Append("• <b>")
            .Append(EscapeHtml(NullDash(FirstNonEmpty(previousStatus))))
            .Append("</b> → <b>")
            .Append(EscapeHtml(NullDash(FirstNonEmpty(newStatus))))
            .Append("</b>\n")
            .Append("• Seller: <b>")
            .Append(seller)
            .Append("</b> • ")
            .Append(profile)
            .Append(" • ")
            .Append(shopName);

        var identity = new[]
        {
            FirstNonEmpty(shop.ShopCode),
            FirstNonEmpty(shop.ExternalShopId),
            FirstNonEmpty(shop.Region),
            FormatTikTokShopStatus(shop.TikTokShopStatus)
        }
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => EscapeHtml(s!))
            .ToList();
        if (identity.Count > 0)
        {
            sb.Append("\n• ").Append(string.Join(" • ", identity));
        }

        var shopUrl = FirstNonEmpty(shop.ShopUrl);
        if (!string.IsNullOrWhiteSpace(shopUrl))
        {
            sb.Append("\n• ").Append(EscapeHtml(shopUrl));
        }

        sb.Append("\nTime :  ")
            .Append(EscapeHtml(FormatVnDateTimeFull(DateTime.UtcNow)));

        return sb.ToString();
    }

    /// <summary>
    /// Tiêu đề nói rõ **ai** gây ra thay đổi: TTS sync là TikTok tự đổi (không phải seller làm),
    /// còn admin/seller là chỉnh tay trên hệ thống.
    /// </summary>
    private static string BuildShopStatusHeadline(string changeSource, string newStatus) =>
        changeSource switch
        {
            "tiktok_sync" => newStatus switch
            {
                ShopStatuses.Active => "TikTok đã kích hoạt lại shop",
                ShopStatuses.Deactivated => "TikTok đã deactive shop",
                ShopStatuses.Suspended => "TikTok đã suspend shop",
                _ => "TikTok đổi trạng thái shop"
            },
            "admin" => "Admin đổi trạng thái shop trên hệ thống",
            "seller" => "Seller đổi trạng thái shop trên hệ thống",
            _ => "Shop đổi trạng thái"
        };

    /// <summary>Raw TTS <c>shop_status</c> → label dùng chung với web/extension.</summary>
    private static string? FormatTikTokShopStatus(int? status) => status switch
    {
        null => null,
        1 => "TTS Activating",
        2 => "TTS Active",
        3 => "TTS Deactivated",
        4 => "TTS Suspended",
        _ => $"TTS {status}"
    };

    /// <summary>VN local: <c>09:06:08 25/3/2026</c>.</summary>
    private static string FormatVnDateTimeFull(DateTime utc)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), VietnamTz);
        return local.ToString("HH:mm:ss d/M/yyyy", CultureInfo.InvariantCulture);
    }

    private static string MaskForTelegram(string? value, string kind)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "—";
        }

        var trimmed = value.Trim();
        if (trimmed.Contains('*', StringComparison.Ordinal))
        {
            return trimmed;
        }

        return kind switch
        {
            "email" => MaskEmail(trimmed),
            "mobile" => MaskKeepLast(trimmed, 4),
            "bank" or "bank_last4" => MaskKeepLast(trimmed, 4),
            _ => trimmed
        };
    }

    private static string MaskEmail(string email)
    {
        var at = email.IndexOf('@');
        if (at <= 0)
        {
            return MaskKeepLast(email, 2);
        }

        var local = email[..at];
        var domain = email[(at + 1)..];
        if (local.Length <= 2)
        {
            return $"{local[0]}***@{domain}";
        }

        return $"{local[0]}***{local[^1]}@{domain}";
    }

    private static string MaskKeepLast(string value, int keep)
    {
        var digitsOrChars = value;
        if (digitsOrChars.Length <= keep)
        {
            return new string('*', Math.Max(4, digitsOrChars.Length)) + digitsOrChars;
        }

        var maskedLen = Math.Max(4, digitsOrChars.Length - keep);
        return new string('*', maskedLen) + digitsOrChars[^keep..];
    }

    private async Task<bool> SendFulfilSummaryForLocalDateAsync(
        TelegramConfig config,
        DateTime summaryDate,
        CancellationToken cancellationToken)
    {
        var summaryDateKey = summaryDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var dayStartLocal = summaryDate.Date;
        var dayEndLocal = dayStartLocal.AddDays(1);
        var dayStartUtc = TimeZoneInfo.ConvertTimeToUtc(dayStartLocal, VietnamTz);
        var dayEndUtc = TimeZoneInfo.ConvertTimeToUtc(dayEndLocal, VietnamTz);
        var overdueBeforeUtc = DateTime.UtcNow - WaitingPurchaseOverdue;

        // Đã mua: FulfilPurchasedAt trong ngày summary, group OwnerId.
        var purchasedToday = await _db.Orders
            .AsNoTracking()
            .Where(o =>
                o.FulfilPurchasedAt != null &&
                o.FulfilPurchasedAt >= dayStartUtc &&
                o.FulfilPurchasedAt < dayEndUtc)
            .Select(o => new { o.OwnerId, o.PaidByUserId })
            .ToListAsync(cancellationToken);

        var purchasedByOwner = purchasedToday
            .GroupBy(o => o.OwnerId)
            .ToDictionary(g => g.Key, g => g.Count());

        var totalPurchased = purchasedToday.Count;
        var purchasedByProcessor = purchasedToday
            .Where(o => o.PaidByUserId.HasValue)
            .GroupBy(o => o.PaidByUserId!.Value)
            .ToDictionary(g => g.Key, g => g.Count());

        // Chờ mua: snapshot PENDING_PROCESS.
        var waitingRows = await OrderWorkflowTabFilter
            .WhereWorkflowTab(_db.Orders.AsNoTracking(), OrderWorkflowTabs.PendingProcess)
            .Select(o => new
            {
                o.OwnerId,
                OrderAt = o.OrderCreateTime ?? o.OrderPaidTime ?? o.CreatedAt
            })
            .ToListAsync(cancellationToken);

        var waitingByOwner = waitingRows
            .GroupBy(o => o.OwnerId)
            .ToDictionary(g => g.Key, g => g.Count());

        var overdueByOwner = waitingRows
            .Where(o => o.OrderAt < overdueBeforeUtc)
            .GroupBy(o => o.OwnerId)
            .ToDictionary(g => g.Key, g => g.Count());

        var ownerIds = purchasedByOwner.Keys
            .Concat(waitingByOwner.Keys)
            .Distinct()
            .ToList();
        var processorIds = purchasedByProcessor.Keys.ToList();
        var nameIds = ownerIds.Concat(processorIds).Distinct().ToList();

        var displayNames = nameIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _db.Users
                .AsNoTracking()
                .Where(u => nameIds.Contains(u.Id))
                .Select(u => new { u.Id, u.FullName, u.UserName })
                .ToDictionaryAsync(
                    u => u.Id,
                    u => string.IsNullOrWhiteSpace(u.FullName) ? (u.UserName ?? u.Id.ToString()) : u.FullName!.Trim(),
                    cancellationToken);

        var sellerLines = new List<string>();
        foreach (var ownerId in ownerIds
                     .OrderBy(id => displayNames.GetValueOrDefault(id, id.ToString()), StringComparer.OrdinalIgnoreCase))
        {
            purchasedByOwner.TryGetValue(ownerId, out var purchased);
            waitingByOwner.TryGetValue(ownerId, out var waiting);
            overdueByOwner.TryGetValue(ownerId, out var overdue);
            if (purchased == 0 && waiting == 0 && overdue == 0)
            {
                continue;
            }

            var name = EscapeHtml(displayNames.GetValueOrDefault(ownerId, ownerId.ToString()));
            sellerLines.Add(
                $"• {name}: Đã mua {purchased} • Chờ mua {waiting} • 12h {overdue}");
        }

        var processorLines = new List<string>();
        if (totalPurchased > 0)
        {
            foreach (var (userId, count) in purchasedByProcessor
                         .OrderByDescending(kv => kv.Value)
                         .ThenBy(kv => displayNames.GetValueOrDefault(kv.Key, kv.Key.ToString()), StringComparer.OrdinalIgnoreCase))
            {
                var name = EscapeHtml(displayNames.GetValueOrDefault(userId, userId.ToString()));
                processorLines.Add($"• {name}: {count}/{totalPurchased}");
            }
        }

        if (sellerLines.Count == 0 && processorLines.Count == 0)
        {
            var emptyText =
                $"📦 Xử lý đơn hàng ({summaryDate:dd/MM/yyyy})\n————————————————————\n(Không có dữ liệu)";
            await _api.SendHtmlMessageAsync(
                config.BotToken, config.CancelRefundChatId, emptyText, previewUrl: null, cancellationToken);
        }
        else
        {
            var sb = new StringBuilder();
            sb.Append("📦 Xử lý đơn hàng (").Append(summaryDate.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)).Append(')');
            sb.Append("\n————————————————————");
            foreach (var line in sellerLines)
            {
                sb.Append('\n').Append(line);
            }

            if (processorLines.Count > 0)
            {
                sb.Append("\n————————————————————");
                foreach (var line in processorLines)
                {
                    sb.Append('\n').Append(line);
                }
            }

            await _api.SendHtmlMessageAsync(
                config.BotToken, config.CancelRefundChatId, sb.ToString(), previewUrl: null, cancellationToken);
        }

        await _settings.SetValueAsync(TelegramSettingKeys.FulfilSummaryLastSentDate, summaryDateKey, cancellationToken);
        _logger.LogInformation("Sent Telegram fulfil summary for {Date}", summaryDateKey);
        return true;
    }

    /// <inheritdoc />
    public async Task<int> NotifyFulfilHandlingAsync(
        IReadOnlyList<Order> orders,
        IReadOnlyDictionary<Guid, string?>? shopNameFallbacks = null,
        CancellationToken cancellationToken = default)
    {
        if (orders.Count == 0)
        {
            return 0;
        }

        var sent = 0;
        try
        {
            var config = await LoadConfigAsync(cancellationToken);
            if (config is null || string.IsNullOrWhiteSpace(config.CancelRefundChatId))
            {
                return 0;
            }

            var notifyUspsEnabled = IsTruthy(config.NotifyUsps);
            var notifyWarehouseEnabled = IsTruthy(config.NotifyWarehouseTrack);
            if (!notifyUspsEnabled && !notifyWarehouseEnabled)
            {
                return 0;
            }

            // Ensure items + purchases exist for title / signals when callers only loaded partial graph.
            var missingItemIds = orders
                .Where(o => o.Id != Guid.Empty && (o.Items is null || o.Items.Count == 0))
                .Select(o => o.Id)
                .Distinct()
                .ToList();
            if (missingItemIds.Count > 0)
            {
                var items = await _db.OrderItems
                    .AsNoTracking()
                    .Where(i => missingItemIds.Contains(i.OrderId))
                    .ToListAsync(cancellationToken);
                foreach (var group in items.GroupBy(i => i.OrderId))
                {
                    var order = orders.FirstOrDefault(o => o.Id == group.Key);
                    if (order is not null) order.Items = group.ToList();
                }
            }

            var missingPurchaseIds = orders
                .Where(o => o.Id != Guid.Empty && o.SupplierPurchases is null)
                .Select(o => o.Id)
                .Distinct()
                .ToList();
            if (missingPurchaseIds.Count > 0)
            {
                var purchases = await _db.SupplierPurchases
                    .AsNoTracking()
                    .Where(p => p.OrderId != null && missingPurchaseIds.Contains(p.OrderId.Value))
                    .ToListAsync(cancellationToken);
                foreach (var group in purchases.GroupBy(p => p.OrderId!.Value))
                {
                    var order = orders.FirstOrDefault(o => o.Id == group.Key);
                    if (order is not null) order.SupplierPurchases = group.ToList();
                }
            }

            var shopIds = orders
                .Where(o => o.ShopId.HasValue)
                .Select(o => o.ShopId!.Value)
                .Distinct()
                .ToList();

            var shops = shopIds.Count == 0
                ? new Dictionary<Guid, Shop>()
                : await _db.Shops
                    .AsNoTracking()
                    .Where(s => shopIds.Contains(s.Id))
                    .ToDictionaryAsync(s => s.Id, cancellationToken);

            var managerUserIds = shops.Values
                .Select(s => s.OwnerId)
                .Concat(orders.Select(o => o.OwnerId))
                .Distinct()
                .ToList();

            var managerNames = managerUserIds.Count == 0
                ? new Dictionary<Guid, string>()
                : await _db.Users
                    .AsNoTracking()
                    .Where(u => managerUserIds.Contains(u.Id))
                    .Select(u => new { u.Id, u.FullName, u.UserName })
                    .ToDictionaryAsync(
                        u => u.Id,
                        u => string.IsNullOrWhiteSpace(u.FullName) ? (u.UserName ?? u.Id.ToString()) : u.FullName,
                        cancellationToken);

            var now = DateTime.UtcNow;
            foreach (var order in orders)
            {
                var includeUsps = notifyUspsEnabled
                    && order.TelegramUspsNotifiedAt is null
                    && OrderFulfilHandlingSignals.HasUspsUnprocessed(order);
                var includeWarehouse = notifyWarehouseEnabled
                    && order.TelegramWarehouseNotifiedAt is null
                    && OrderFulfilHandlingSignals.NeedsThankCard(order);

                if (!includeUsps && !includeWarehouse)
                {
                    continue;
                }

                if (!HasCompleteProductInfo(order))
                {
                    _logger.LogInformation(
                        "Skip Telegram fulfil-handling notify for order {OrderId}: product info incomplete",
                        order.Id);
                    continue;
                }

                shops.TryGetValue(order.ShopId ?? Guid.Empty, out var shop);
                string? fallbackName = null;
                shopNameFallbacks?.TryGetValue(order.Id, out fallbackName);

                var managerUserId = shop?.OwnerId ?? order.OwnerId;
                managerNames.TryGetValue(managerUserId, out var managerFullName);

                var text = BuildFulfilHandlingMessage(
                    order,
                    shop,
                    fallbackName,
                    managerFullName,
                    includeUsps,
                    includeWarehouse);

                var photoUrl = order.Items
                    .Select(i => i.ImgUrl)
                    .FirstOrDefault(u => !string.IsNullOrWhiteSpace(u));

                await _api.SendHtmlMessageAsync(
                    config.BotToken,
                    config.CancelRefundChatId,
                    text,
                    previewUrl: string.IsNullOrWhiteSpace(photoUrl) ? null : photoUrl!.Trim(),
                    cancellationToken);

                if (includeUsps) order.TelegramUspsNotifiedAt = now;
                if (includeWarehouse) order.TelegramWarehouseNotifiedAt = now;
                order.UpdatedAt = now;
                sent++;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to send Telegram fulfil-handling notifications for {Count} orders",
                orders.Count);
        }

        return sent;
    }

    /// <inheritdoc />
    public async Task<int> NotifySheinDetailAddTrackAsync(
        IReadOnlyList<SheinDetailAddTrackNotifyItem> items,
        CancellationToken cancellationToken = default)
    {
        if (items.Count == 0)
        {
            return 0;
        }

        var sent = 0;
        var now = DateTime.UtcNow;
        try
        {
            var botToken = _telegramOptions.Value.BotToken?.Trim();
            var hasBotToken = !string.IsNullOrWhiteSpace(botToken);

            var values = await _settings.GetValuesAsync(
                [
                    SheinDetailSyncSettingKeys.NotifyCarrierTracking,
                    SheinDetailSyncSettingKeys.TelegramChatId
                ],
                cancellationToken);

            // Toggle mặc định tắt: thiếu key (chưa seed) phải off, không mượn quy ước
            // "rỗng = bật" của các cờ Telegram cũ.
            var enabled = string.Equals(
                values.GetValueOrDefault(SheinDetailSyncSettingKeys.NotifyCarrierTracking)?.Trim(),
                "true",
                StringComparison.OrdinalIgnoreCase);
            var chatId = FirstNonEmpty(values.GetValueOrDefault(SheinDetailSyncSettingKeys.TelegramChatId));

            string? skipReason = null;
            if (!hasBotToken) skipReason = "missing_bot_token";
            else if (!enabled) skipReason = "notify_off";
            else if (chatId is null) skipReason = "missing_chat_id";

            var reportable = new List<SheinDetailAddTrackNotifyItem>();
            var pendingSkipped = 0;
            var alreadyNotified = 0;
            var configDeferred = 0;

            foreach (var item in items)
            {
                if (item.Order.TelegramAddTrackNotifiedAt is not null)
                {
                    alreadyNotified++;
                    continue;
                }

                // Mark Ship bị hoãn (hết ngân sách lượt) → stamp pending, lượt detail sau gửi lại.
                if (ResolveMarkShipOutcome(item.Link) is null)
                {
                    item.Order.TelegramAddTrackNotifyPendingAt ??= now;
                    item.Order.UpdatedAt = now;
                    pendingSkipped++;
                    continue;
                }

                if (skipReason is not null)
                {
                    // Config chưa sẵn sàng: giữ pending để bật toggle / bổ sung Chat·Bot vẫn gửi được.
                    item.Order.TelegramAddTrackNotifyPendingAt ??= now;
                    item.Order.UpdatedAt = now;
                    configDeferred++;
                    continue;
                }

                reportable.Add(item);
            }

            if (skipReason is not null)
            {
                _logger.LogInformation(
                    "Telegram SHEIN add-track skipped: reason={Reason} candidates={Candidates} pendingStamped={Pending} configDeferred={ConfigDeferred} alreadyNotified={Already}",
                    skipReason,
                    items.Count,
                    pendingSkipped,
                    configDeferred,
                    alreadyNotified);
                return 0;
            }

            if (reportable.Count == 0)
            {
                _logger.LogInformation(
                    "Telegram SHEIN add-track skipped: reason=all_pending_mark_ship candidates={Candidates} pendingStamped={Pending} alreadyNotified={Already}",
                    items.Count,
                    pendingSkipped,
                    alreadyNotified);
                return 0;
            }

            // Đường sweep đã Include Shop; chỉ truy vấn cho đơn nào thiếu navigation.
            var missingShopIds = reportable
                .Where(i => i.Order.Shop is null && i.Order.ShopId.HasValue)
                .Select(i => i.Order.ShopId!.Value)
                .Distinct()
                .ToList();
            var shops = missingShopIds.Count == 0
                ? new Dictionary<Guid, Shop>()
                : await _db.Shops
                    .AsNoTracking()
                    .Where(s => missingShopIds.Contains(s.Id))
                    .ToDictionaryAsync(s => s.Id, cancellationToken);

            Shop? ResolveShop(Order order) => order.Shop
                ?? (order.ShopId.HasValue ? shops.GetValueOrDefault(order.ShopId.Value) : null);

            var managerUserIds = reportable
                .Select(i => ResolveShop(i.Order)?.OwnerId ?? i.Order.OwnerId)
                .Distinct()
                .ToList();
            var managerNames = await _db.Users
                .AsNoTracking()
                .Where(u => managerUserIds.Contains(u.Id))
                .Select(u => new { u.Id, u.FullName, u.UserName })
                .ToDictionaryAsync(
                    u => u.Id,
                    u => string.IsNullOrWhiteSpace(u.FullName) ? (u.UserName ?? u.Id.ToString()) : u.FullName,
                    cancellationToken);

            foreach (var item in reportable)
            {
                var shop = ResolveShop(item.Order);
                var managerUserId = shop?.OwnerId ?? item.Order.OwnerId;
                managerNames.TryGetValue(managerUserId, out var managerFullName);

                var text = BuildSheinDetailAddTrackMessage(item, shop?.ProfileName, managerFullName);
                await _api.SendHtmlMessageAsync(botToken!, chatId!, text, previewUrl: null, cancellationToken);

                item.Order.TelegramAddTrackNotifiedAt = now;
                item.Order.TelegramAddTrackNotifyPendingAt = null;
                item.Order.UpdatedAt = now;
                sent++;

                _logger.LogInformation(
                    "Telegram SHEIN add-track sent: orderId={OrderId} platformOrderId={PlatformOrderId} billno={Billno} chatId={ChatId}",
                    item.Order.Id,
                    item.Order.PlatformOrderId,
                    item.Billno,
                    chatId);
            }

            if (pendingSkipped > 0 || alreadyNotified > 0)
            {
                _logger.LogInformation(
                    "Telegram SHEIN add-track summary: candidates={Candidates} reportable={Reportable} sent={Sent} pendingSkipped={Pending} alreadyNotified={Already}",
                    items.Count,
                    reportable.Count,
                    sent,
                    pendingSkipped,
                    alreadyNotified);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to send Telegram SHEIN add-track notifications for {Count} orders",
                items.Count);
        }

        return sent;
    }

    /// <inheritdoc />
    public async Task NotifyOrderMessageAsync(
        Order order,
        string? shopNameFallback = null,
        CancellationToken cancellationToken = default)
    {
        if (order is null || string.IsNullOrWhiteSpace(order.Message))
        {
            return;
        }

        try
        {
            var config = await LoadConfigAsync(cancellationToken);
            if (config is null
                || !IsTruthy(config.NotifyOrderMessage)
                || string.IsNullOrWhiteSpace(config.CancelRefundChatId))
            {
                return;
            }

            Shop? shop = null;
            if (order.ShopId.HasValue)
            {
                shop = order.Shop
                    ?? await _db.Shops
                        .AsNoTracking()
                        .FirstOrDefaultAsync(s => s.Id == order.ShopId.Value, cancellationToken);
            }

            var managerUserId = shop?.OwnerId ?? order.OwnerId;
            var managerFullName = await _db.Users
                .AsNoTracking()
                .Where(u => u.Id == managerUserId)
                .Select(u => string.IsNullOrWhiteSpace(u.FullName) ? (u.UserName ?? u.Id.ToString()) : u.FullName)
                .FirstOrDefaultAsync(cancellationToken);

            if (order.Items is null || order.Items.Count == 0)
            {
                var items = await _db.OrderItems
                    .AsNoTracking()
                    .Where(i => i.OrderId == order.Id)
                    .ToListAsync(cancellationToken);
                order.Items = items;
            }

            var text = BuildOrderMessageNotification(
                order,
                shop,
                shopNameFallback,
                managerFullName);

            var photoUrl = order.Items
                .Select(i => i.ImgUrl)
                .FirstOrDefault(u => !string.IsNullOrWhiteSpace(u));

            await _api.SendHtmlMessageAsync(
                config.BotToken,
                config.CancelRefundChatId,
                text,
                previewUrl: string.IsNullOrWhiteSpace(photoUrl) ? null : photoUrl!.Trim(),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to send Telegram order-message notification for order {OrderId}",
                order.Id);
        }
    }

    /// <inheritdoc />
    public async Task<int> TryNotifyStaleNoTrackingAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var config = await LoadConfigAsync(cancellationToken);
            if (config is null
                || !IsTruthy(config.NotifyStaleNoTracking)
                || string.IsNullOrWhiteSpace(config.CancelRefundChatId))
            {
                return 0;
            }

            var localNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, VietnamTz);
            TelegramSummarySchedule.TryParseLocalClock(config.FulfilSummaryHour, out var hour, out var minute);
            if (!TelegramSummarySchedule.TryResolveSummaryLocalDate(
                    localNow, hour, minute, config.NotifyStaleNoTrackingLastSentDate, out var summaryDate))
            {
                return 0;
            }

            return await SendStaleNoTrackingDigestAsync(config, summaryDate, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Telegram stale-no-tracking digest failed");
            return 0;
        }
    }

    /// <inheritdoc />
    public async Task<bool> NotifySyncHealthAlertAsync(
        string text,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        try
        {
            var config = await LoadConfigAsync(cancellationToken);
            if (config is null || string.IsNullOrWhiteSpace(config.CancelRefundChatId))
            {
                return false;
            }

            await _api.SendHtmlMessageAsync(
                config.BotToken, config.CancelRefundChatId, text, previewUrl: null, cancellationToken);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // The watchdog exists to report failures — it must never become one itself.
            _logger.LogWarning(ex, "Telegram sync health alert failed");
            return false;
        }
    }

    private async Task<int> SendStaleNoTrackingDigestAsync(
        TelegramConfig config,
        DateTime summaryDate,
        CancellationToken cancellationToken)
    {
        var hours = OrderStaleNoTracking.ResolveHours(config.NotifyStaleNoTrackingHours);
        var now = DateTime.UtcNow;
        var cutoff = now - TimeSpan.FromHours(hours);
        var summaryDateKey = summaryDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        var candidates = await OrderWorkflowTabFilter
            .WhereWorkflowTab(_db.Orders.AsNoTracking(), OrderWorkflowTabs.Purchased)
            .Include(o => o.Items)
            .Include(o => o.SupplierPurchases)
            .Where(o =>
                (o.FulfilPurchasedAt != null && o.FulfilPurchasedAt <= cutoff)
                || (o.FulfilPurchasedAt == null
                    && o.SupplierPurchases.Any(p =>
                        (p.OrderedAt != null && p.OrderedAt <= cutoff)
                        || (p.OrderedAt == null && p.CreatedAt <= cutoff))))
            .OrderBy(o => o.FulfilPurchasedAt ?? o.CreatedAt)
            .ToListAsync(cancellationToken);

        var eligible = candidates
            .Where(o => OrderStaleNoTracking.IsEligible(o, now, hours))
            .ToList();

        if (eligible.Count > 0)
        {
            var shopIds = eligible
                .Where(o => o.ShopId.HasValue)
                .Select(o => o.ShopId!.Value)
                .Distinct()
                .ToList();

            var shops = shopIds.Count == 0
                ? new Dictionary<Guid, Shop>()
                : await _db.Shops
                    .AsNoTracking()
                    .Where(s => shopIds.Contains(s.Id))
                    .ToDictionaryAsync(s => s.Id, cancellationToken);

            var managerUserIds = shops.Values
                .Select(s => s.OwnerId)
                .Concat(eligible.Select(o => o.OwnerId))
                .Distinct()
                .ToList();

            var managerNames = managerUserIds.Count == 0
                ? new Dictionary<Guid, string>()
                : await _db.Users
                    .AsNoTracking()
                    .Where(u => managerUserIds.Contains(u.Id))
                    .Select(u => new { u.Id, u.FullName, u.UserName })
                    .ToDictionaryAsync(
                        u => u.Id,
                        u => string.IsNullOrWhiteSpace(u.FullName) ? (u.UserName ?? u.Id.ToString()) : u.FullName,
                        cancellationToken);

            var lines = new List<string>(eligible.Count);
            foreach (var order in eligible)
            {
                shops.TryGetValue(order.ShopId ?? Guid.Empty, out var shop);
                var managerUserId = shop?.OwnerId ?? order.OwnerId;
                managerNames.TryGetValue(managerUserId, out var managerFullName);
                var purchasedAt = OrderStaleNoTracking.ResolvePurchasedAt(order) ?? cutoff;
                var elapsedHours = Math.Max(hours, (int)Math.Floor((now - purchasedAt).TotalHours));
                lines.Add(BuildStaleNoTrackingDigestLine(order, shop, managerFullName, elapsedHours));
            }

            var dateText = summaryDate.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
            foreach (var chunk in ChunkStaleNoTrackingDigest(hours, dateText, eligible.Count, lines))
            {
                await _api.SendHtmlMessageAsync(
                    config.BotToken,
                    config.CancelRefundChatId,
                    chunk,
                    previewUrl: null,
                    cancellationToken);
            }
        }

        await _settings.SetValueAsync(
            TelegramSettingKeys.NotifyStaleNoTrackingLastSentDate,
            summaryDateKey,
            cancellationToken);
        _logger.LogInformation(
            "Sent Telegram stale-no-tracking digest for {Date}: {Count} orders (threshold {Hours}h)",
            summaryDateKey,
            eligible.Count,
            hours);
        return eligible.Count;
    }

    /// <inheritdoc />
    public async Task<int> TrySendPendingNewOrdersAsync(CancellationToken cancellationToken = default)
    {
        const int batchSize = 20;
        var window = TimeSpan.FromHours(24);
        var now = DateTime.UtcNow;
        var cutoff = now - window;
        var sent = 0;

        try
        {
            // Expire rows that stayed shop-less (or canceled) past the retry window so the
            // partial index stays small. Status filter is inlined for EF translation.
            var expired = await _db.Orders
                .Where(o =>
                    o.TelegramNewOrderNotifiedAt == null
                    && o.CreatedAt < cutoff
                    && (o.ShopId == null
                        || o.PlatformOrderStatus == PlatformOrderStatuses.Cancelled
                        || o.PlatformOrderStatus == "CANCELED"
                        || ((o.PlatformOrderStatus == null || o.PlatformOrderStatus == "")
                            && (o.OrderStatus == "canceled" || o.OrderStatus == "cancelled"))))
                .OrderBy(o => o.CreatedAt)
                .Take(batchSize)
                .ToListAsync(cancellationToken);

            if (expired.Count > 0)
            {
                foreach (var order in expired)
                {
                    order.TelegramNewOrderNotifiedAt = now;
                    order.UpdatedAt = now;
                    _logger.LogInformation(
                        "Expire pending Telegram new-order for order {OrderId}: shop unresolved or canceled past 24h (fourSellerShopId={FourSellerShopId})",
                        order.Id,
                        order.FourSellerShopId);
                }

                await _db.SaveChangesAsync(cancellationToken);
            }

            var pending = await _db.Orders
                .Include(o => o.Items)
                .Where(o =>
                    o.TelegramNewOrderNotifiedAt == null
                    && o.ShopId != null
                    && o.CreatedAt >= cutoff
                    && o.PlatformOrderStatus != PlatformOrderStatuses.Cancelled
                    && o.PlatformOrderStatus != "CANCELED"
                    && !((o.PlatformOrderStatus == null || o.PlatformOrderStatus == "")
                         && (o.OrderStatus == "canceled" || o.OrderStatus == "cancelled")))
                .OrderBy(o => o.CreatedAt)
                .Take(batchSize)
                .ToListAsync(cancellationToken);

            if (pending.Count == 0)
            {
                return 0;
            }

            sent = await SendOrderNotificationsAsync(
                pending,
                shopNameFallbacks: null,
                isCancelRefund: false,
                BuildNewOrderMessage,
                "new-order-sweep",
                cancellationToken);

            if (sent > 0 || pending.Any(o => o.TelegramNewOrderNotifiedAt is not null))
            {
                await _db.SaveChangesAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Telegram pending new-order sweep failed");
            return 0;
        }

        return sent;
    }

    private static string BuildFulfilHandlingMessage(
        Order order,
        Shop? shop,
        string? shopNameFallback,
        string? managerFullName,
        bool includeUsps,
        bool includeWarehouse)
    {
        var sellerName = EscapeHtml(FirstNonEmpty(managerFullName, "Seller")!);
        var emoji = includeUsps && includeWarehouse ? "📬" : includeUsps ? "📮" : "🎁";

        var sb = new StringBuilder();
        sb.Append(emoji).Append(" <b>").Append(sellerName).Append("</b> — Cần xử lý fulfil!");
        sb.Append("\n————————————————————");

        AppendOrderItems(sb, order);
        AppendNewOrderShopProfile(sb, shop, shopNameFallback);
        AppendOrderAmountAndCode(sb, order);

        if (includeUsps)
        {
            var uspsNo = OrderFulfilHandlingSignals.ResolveUspsTrackingNo(order);
            sb.Append("\n• 📮 USPS • <code>")
                .Append(EscapeHtml(NullDash(uspsNo)))
                .Append("</code>");
        }

        if (includeWarehouse)
        {
            var khoNo = OrderFulfilHandlingSignals.ResolveWarehouseTrackingNo(order);
            sb.Append("\n• 🎁 Kho • <code>")
                .Append(EscapeHtml(NullDash(khoNo)))
                .Append("</code>");
        }

        if (includeUsps && includeWarehouse)
        {
            sb.Append("\n💪 Xử lý BuyTrack + ThankCard cho đơn này.");
        }
        else if (includeUsps)
        {
            sb.Append("\n💪 Quét BuyTrack rồi đánh dấu đã xử lý.");
        }
        else
        {
            sb.Append("\n💪 Kiểm tra ThankCard / xử lý đơn kho.");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Mark Ship outcome worth reporting: <c>true</c> = đã lên sàn, <c>false</c> = không lên được.
    /// <c>null</c> khi lượt sync chưa thử Mark Ship (còn <c>pending</c>) — chưa có gì để báo.
    /// </summary>
    internal static bool? ResolveMarkShipOutcome(SupplierPurchase link) =>
        link.AutoMarkShipStatus switch
        {
            AutoMarkShipStatuses.Succeeded => true,
            AutoMarkShipStatuses.Failed
                or AutoMarkShipStatuses.Blocked
                or AutoMarkShipStatuses.Unconfirmed => false,
            _ => null
        };

    internal static string BuildSheinDetailAddTrackMessage(
        SheinDetailAddTrackNotifyItem item,
        string? profileName,
        string? managerFullName)
    {
        var link = item.Link;
        var order = item.Order;
        var sellerName = EscapeHtml(FirstNonEmpty(managerFullName, "Seller")!);
        var orderCode = FirstNonEmpty(
            order.PlatformOrderId,
            order.FourSellerOrderId.ToString(CultureInfo.InvariantCulture))!;
        var tracking = NullDash(string.Join(
            ' ',
            new[] { link.Carrier, link.TrackingNo }
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s!.Trim())));
        var orderedAt = order.OrderCreateTime ?? order.OrderPaidTime ?? order.CreatedAt;
        var purchasedAt = OrderStaleNoTracking.ResolvePurchasedAt(order);
        var succeeded = ResolveMarkShipOutcome(link) == true;

        var sb = new StringBuilder();
        sb.Append(succeeded
            ? "📦 <b>Add track</b> · ✅ Thành công"
            : "📦 <b>Add track</b> · ❌ Thất bại");

        if (!succeeded)
        {
            var error = FirstNonEmpty(link.AutoMarkShipError);
            if (error is not null)
            {
                sb.Append('\n').Append(EscapeHtml(TruncateOneLine(error, 160)));
            }
        }

        sb.Append("\n• ").Append(sellerName).Append(" • ").Append(EscapeHtml(NullDash(profileName)));
        sb.Append("\n• Đơn: <code>").Append(EscapeHtml(orderCode)).Append("</code>");
        sb.Append("\n• Billno: <code>").Append(EscapeHtml(NullDash(item.Billno))).Append("</code>");
        sb.Append("\n• Tracking: <code>").Append(EscapeHtml(tracking)).Append("</code>");
        sb.Append("\n• Đặt hàng: ").Append(FormatVnDateTime(orderedAt, "HH:mm dd/MM/yyyy"));
        sb.Append("\n• Mua hàng: ").Append(
            purchasedAt is { } bought
                ? FormatVnDateTime(bought, "HH:mm dd/MM/yyyy")
                : "—");
        sb.Append("\n• Mua sau đặt: <b>")
            .Append(purchasedAt is { } buyAfterOrder
                ? FormatTrackingLatency(orderedAt, buyAfterOrder)
                : "—")
            .Append("</b>");
        sb.Append("\n• Tracking sau mua: <b>")
            .Append(purchasedAt is { } fromBuy
                ? FormatTrackingLatency(fromBuy, item.DetectedAtUtc)
                : "—")
            .Append("</b>");
        sb.Append("\n• Tracking sau đặt: <b>")
            .Append(FormatTrackingLatency(orderedAt, item.DetectedAtUtc))
            .Append("</b>");
        sb.Append("\n• Time: ").Append(FormatVnDateTime(item.DetectedAtUtc, "HH:mm:ss dd/MM/yyyy"));

        return sb.ToString();
    }

    /// <summary>
    /// Khoảng chờ từ mốc tham chiếu tới lúc đích (mua / thấy mã carrier).
    /// Luôn tổng giờ + phút (vd. 50 giờ 15 phút), không tách ngày.
    /// </summary>
    private static string FormatTrackingLatency(DateTime fromUtc, DateTime toUtc)
    {
        if (fromUtc == default)
        {
            return "—";
        }

        var elapsed = toUtc - fromUtc;
        if (elapsed < TimeSpan.Zero)
        {
            elapsed = TimeSpan.Zero;
        }

        if (elapsed.TotalHours >= 1)
        {
            return $"{(int)elapsed.TotalHours} giờ {elapsed.Minutes} phút";
        }

        return elapsed.TotalMinutes >= 1 ? $"{(int)elapsed.TotalMinutes} phút" : "<1 phút";
    }

    private static string BuildOrderMessageNotification(
        Order order,
        Shop? shop,
        string? shopNameFallback,
        string? managerFullName)
    {
        var sellerName = EscapeHtml(FirstNonEmpty(managerFullName, "Seller")!);
        var message = EscapeHtml(TruncateOneLine(order.Message!.Trim(), 500));

        var sb = new StringBuilder();
        sb.Append("⚠️ <b>").Append(sellerName).Append("</b> — Thông báo xử lý đơn");
        sb.Append("\n————————————————————");

        AppendOrderItems(sb, order);
        AppendNewOrderShopProfile(sb, shop, shopNameFallback);
        AppendOrderAmountAndCode(sb, order);

        sb.Append("\n• 📢 ").Append(message);
        sb.Append("\n💪 Cần xử lý / xác nhận với seller.");

        return sb.ToString();
    }

    private static string BuildStaleNoTrackingDigestLine(
        Order order,
        Shop? shop,
        string? managerFullName,
        int elapsedHours)
    {
        var sellerName = EscapeHtml(FirstNonEmpty(managerFullName, "Seller")!);
        var profileName = EscapeHtml(NullDash(FirstNonEmpty(shop?.ProfileName)));
        var title = order.Items?.Select(i => i.Title).FirstOrDefault(t => !string.IsNullOrWhiteSpace(t));
        var titleHtml = EscapeHtml(TruncateOneLine(NullDash(title), ProductTitleMaxChars));
        var orderCode = FirstNonEmpty(
            order.PlatformOrderId,
            order.FourSellerOrderId.ToString(CultureInfo.InvariantCulture))!;
        var createdAt = order.OrderCreateTime ?? order.OrderPaidTime ?? order.CreatedAt;

        return $"• {sellerName} • {profileName} • {titleHtml}\n"
            + $"• Order : <code>{EscapeHtml(orderCode)}</code> • {elapsedHours.ToString(CultureInfo.InvariantCulture)}h\n"
            + $"• Created : {EscapeHtml(FormatVnDateTimeCompact(createdAt))}";
    }

    private static IEnumerable<string> ChunkStaleNoTrackingDigest(
        int thresholdHours,
        string dateText,
        int totalCount,
        IReadOnlyList<string> lines)
    {
        var header =
            $"⏰ Đơn {thresholdHours.ToString(CultureInfo.InvariantCulture)}h chưa có tracking thật ({dateText})"
            + $"\nTổng {totalCount.ToString(CultureInfo.InvariantCulture)} đơn"
            + "\n————————————————————";

        if (lines.Count == 0)
        {
            yield return header;
            yield break;
        }

        var sb = new StringBuilder(header);
        foreach (var line in lines)
        {
            var candidateLength = sb.Length + 1 + line.Length;
            if (sb.Length > header.Length && candidateLength > TelegramMessageMaxChars)
            {
                yield return sb.ToString();
                sb.Clear();
                sb.Append(header);
            }

            sb.Append('\n').Append(line);
        }

        if (sb.Length > 0)
        {
            yield return sb.ToString();
        }
    }

    private async Task<bool> SendSellerSummaryForLocalDateAsync(
        TelegramConfig config,
        DateTime summaryDate,
        CancellationToken cancellationToken)
    {
        var summaryDateKey = summaryDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        var sellers = await GetSellersAsync(cancellationToken);
        if (sellers.Count == 0)
        {
            var emptyText = "🏆 SELLER SUMMARY🏆\n(Không có seller)";
            await _api.SendHtmlMessageAsync(
                config.BotToken, config.OrdersChatId, emptyText, previewUrl: null, cancellationToken);
            await _settings.SetValueAsync(TelegramSettingKeys.SellerSummaryLastSentDate, summaryDateKey, cancellationToken);
            _logger.LogInformation("Sent Telegram seller summary (no sellers) for {Date}", summaryDateKey);
            return true;
        }

        var sellerIds = sellers.Select(s => s.Id).ToList();

        var dayStartLocal = summaryDate.Date;
        var dayEndLocal = dayStartLocal.AddDays(1);
        var dayStartUtc = TimeZoneInfo.ConvertTimeToUtc(dayStartLocal, VietnamTz);
        var dayEndUtc = TimeZoneInfo.ConvertTimeToUtc(dayEndLocal, VietnamTz);

        var listedCounts = await _db.Listings
            .AsNoTracking()
            .Where(l =>
                sellerIds.Contains(l.OwnerId) &&
                l.PageType == ListingPageTypes.Published &&
                (l.PlatformCreateTime ?? l.PlatformUpdateTime ?? l.CreatedAt) >= dayStartUtc &&
                (l.PlatformCreateTime ?? l.PlatformUpdateTime ?? l.CreatedAt) < dayEndUtc)
            .GroupBy(l => l.OwnerId)
            .Select(g => new { OwnerId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.OwnerId, x => x.Count, cancellationToken);

        var dayOrders = await _db.Orders
            .AsNoTracking()
            .Where(o =>
                sellerIds.Contains(o.OwnerId) &&
                (o.OrderCreateTime ?? o.CreatedAt) >= dayStartUtc &&
                (o.OrderCreateTime ?? o.CreatedAt) < dayEndUtc &&
                o.PlatformOrderStatus != "CANCELLED" &&
                o.PlatformOrderStatus != "CANCELED" &&
                !((o.PlatformOrderStatus == null || o.PlatformOrderStatus == "") &&
                  (o.OrderStatus == "canceled" || o.OrderStatus == "cancelled")))
            .Select(o => new
            {
                o.OwnerId,
                Revenue = o.SellerNetAmount ?? (o.TotalPaidAmount != 0 ? o.TotalPaidAmount : o.Amount)
            })
            .ToListAsync(cancellationToken);

        var orderStats = dayOrders
            .GroupBy(o => o.OwnerId)
            .ToDictionary(
                g => g.Key,
                g => (OrderCount: g.Count(), Revenue: g.Sum(x => x.Revenue)));

        var ranked = sellers
            .Select(seller =>
            {
                listedCounts.TryGetValue(seller.Id, out var listed);
                orderStats.TryGetValue(seller.Id, out var stats);
                return new
                {
                    seller.DisplayName,
                    Listed = listed,
                    stats.OrderCount,
                    stats.Revenue
                };
            })
            .OrderByDescending(s => s.Revenue)
            .ThenBy(s => s.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var medalRank = 0;
        var lines = new List<string>(ranked.Count + 1);
        foreach (var row in ranked)
        {
            string icon;
            if (row.Revenue > 0 && medalRank < 3)
            {
                icon = medalRank switch
                {
                    0 => "🥇",
                    1 => "🥈",
                    _ => "🥉"
                };
                medalRank++;
            }
            else
            {
                icon = "⚡️";
            }

            var revenueText = row.Revenue.ToString("0.##", CultureInfo.InvariantCulture);
            lines.Add(
                $"{icon} {EscapeHtml(row.DisplayName)} : L<b>{row.Listed}</b> • O<b>{row.OrderCount}</b> • R<b>${revenueText}</b>");
        }

        var totalListed = ranked.Sum(r => r.Listed);
        var totalOrders = ranked.Sum(r => r.OrderCount);
        var totalRevenue = ranked.Sum(r => r.Revenue);
        var totalRevenueText = totalRevenue.ToString("0.##", CultureInfo.InvariantCulture);
        lines.Add(
            $"📊 Total : L<b>{totalListed}</b> • O<b>{totalOrders}</b> • R<b>${totalRevenueText}</b>");

        foreach (var chunk in ChunkSellerSummaryMessages(lines))
        {
            await _api.SendHtmlMessageAsync(
                config.BotToken, config.OrdersChatId, chunk, previewUrl: null, cancellationToken);
        }

        await _settings.SetValueAsync(TelegramSettingKeys.SellerSummaryLastSentDate, summaryDateKey, cancellationToken);
        _logger.LogInformation(
            "Sent Telegram seller summary for {Date} ({SellerCount} sellers)",
            summaryDateKey,
            sellers.Count);
        return true;
    }

    private async Task<IReadOnlyList<SellerLine>> GetSellersAsync(CancellationToken cancellationToken)
    {
        var inSellerRole = await _userManager.GetUsersInRoleAsync(RoleNames.Seller);
        var result = new List<SellerLine>();
        foreach (var user in inSellerRole.Where(u => u.IsActive))
        {
            var displayName = string.IsNullOrWhiteSpace(user.FullName)
                ? (user.UserName ?? user.Id.ToString())
                : user.FullName.Trim();
            result.Add(new SellerLine(user.Id, displayName));
        }

        return result;
    }

    private static IEnumerable<string> ChunkSellerSummaryMessages(IReadOnlyList<string> lines)
    {
        const string header = "🏆 SELLER SUMMARY🏆";
        if (lines.Count == 0)
        {
            yield return header;
            yield break;
        }

        var sb = new StringBuilder(header);
        foreach (var line in lines)
        {
            var candidateLength = sb.Length + 1 + line.Length;
            if (sb.Length > header.Length && candidateLength > TelegramMessageMaxChars)
            {
                yield return sb.ToString();
                sb.Clear();
                sb.Append(header);
            }

            sb.Append('\n').Append(line);
        }

        if (sb.Length > 0)
        {
            yield return sb.ToString();
        }
    }

    private async Task<TelegramConfig?> LoadConfigAsync(CancellationToken cancellationToken)
    {
        var opts = _telegramOptions.Value;
        var botToken = opts.BotToken?.Trim();
        if (string.IsNullOrWhiteSpace(botToken))
        {
            return null;
        }

        var values = await _settings.GetValuesAsync(
            [
                TelegramSettingKeys.NotifyNewOrder,
                TelegramSettingKeys.NotifyCancelRefund,
                TelegramSettingKeys.OrdersChatId,
                TelegramSettingKeys.CancelRefundChatId,
                TelegramSettingKeys.DailySummaryEnabled,
                TelegramSettingKeys.DailySummaryHour,
                TelegramSettingKeys.DailySummaryLastSentDate,
                TelegramSettingKeys.SellerSummaryEnabled,
                TelegramSettingKeys.SellerSummaryHour,
                TelegramSettingKeys.SellerSummaryLastSentDate,
                TelegramSettingKeys.FulfilSummaryEnabled,
                TelegramSettingKeys.FulfilSummaryHour,
                TelegramSettingKeys.FulfilSummaryLastSentDate,
                TelegramSettingKeys.NotifyUsps,
                TelegramSettingKeys.NotifyWarehouseTrack,
                TelegramSettingKeys.NotifyOrderMessage,
                TelegramSettingKeys.NotifyStaleNoTracking,
                TelegramSettingKeys.NotifyStaleNoTrackingHours,
                TelegramSettingKeys.NotifyStaleNoTrackingLastSentDate
            ],
            cancellationToken);

        values.TryGetValue(TelegramSettingKeys.NotifyNewOrder, out var notifyNew);
        values.TryGetValue(TelegramSettingKeys.NotifyCancelRefund, out var notifyCancelRefund);
        values.TryGetValue(TelegramSettingKeys.OrdersChatId, out var ordersChatSetting);
        values.TryGetValue(TelegramSettingKeys.CancelRefundChatId, out var cancelChatSetting);
        values.TryGetValue(TelegramSettingKeys.DailySummaryEnabled, out var dailyEnabled);
        values.TryGetValue(TelegramSettingKeys.DailySummaryHour, out var hour);
        values.TryGetValue(TelegramSettingKeys.DailySummaryLastSentDate, out var lastSent);
        values.TryGetValue(TelegramSettingKeys.SellerSummaryEnabled, out var sellerEnabled);
        values.TryGetValue(TelegramSettingKeys.SellerSummaryHour, out var sellerHour);
        values.TryGetValue(TelegramSettingKeys.SellerSummaryLastSentDate, out var sellerLastSent);
        values.TryGetValue(TelegramSettingKeys.FulfilSummaryEnabled, out var fulfilEnabled);
        values.TryGetValue(TelegramSettingKeys.FulfilSummaryHour, out var fulfilHour);
        values.TryGetValue(TelegramSettingKeys.FulfilSummaryLastSentDate, out var fulfilLastSent);
        values.TryGetValue(TelegramSettingKeys.NotifyUsps, out var notifyUsps);
        values.TryGetValue(TelegramSettingKeys.NotifyWarehouseTrack, out var notifyWarehouse);
        values.TryGetValue(TelegramSettingKeys.NotifyOrderMessage, out var notifyOrderMessage);
        values.TryGetValue(TelegramSettingKeys.NotifyStaleNoTracking, out var notifyStaleNoTracking);
        values.TryGetValue(TelegramSettingKeys.NotifyStaleNoTrackingHours, out var staleNoTrackingHours);
        values.TryGetValue(TelegramSettingKeys.NotifyStaleNoTrackingLastSentDate, out var staleNoTrackingLastSent);

        var ordersChatId = FirstNonEmpty(ordersChatSetting, opts.ChatId) ?? string.Empty;
        var cancelRefundChatId = (cancelChatSetting ?? string.Empty).Trim();

        return new TelegramConfig(
            botToken,
            ordersChatId,
            cancelRefundChatId,
            notifyNew,
            notifyCancelRefund,
            dailyEnabled,
            hour,
            lastSent,
            sellerEnabled,
            sellerHour,
            sellerLastSent,
            fulfilEnabled,
            fulfilHour,
            fulfilLastSent,
            notifyUsps,
            notifyWarehouse,
            notifyOrderMessage,
            notifyStaleNoTracking,
            staleNoTrackingHours,
            staleNoTrackingLastSent);
    }

    internal static string BuildNewOrderMessage(
        Order order,
        Shop? shop,
        string? shopNameFallback,
        string? managerFullName,
        bool _)
    {
        // Compact layout for mobile preview (first 2 lines): seller + $amount • product.
        var sellerName = EscapeHtml(FirstNonEmpty(managerFullName, "Seller")!);
        var amount = OrderRevenue.Resolve(order);
        var amountHtml = "<b>" + EscapeHtml(FormatDollarAmount(amount)) + "</b>";
        var shopName = FirstNonEmpty(shop?.Name, shopNameFallback);
        var profileName = FirstNonEmpty(shop?.ProfileName);
        var orderAt = order.OrderCreateTime ?? order.OrderPaidTime ?? order.CreatedAt;
        var orderCode = FirstNonEmpty(
            order.PlatformOrderId,
            order.FourSellerOrderId.ToString(CultureInfo.InvariantCulture))!;

        var sb = new StringBuilder();
        sb.Append("🚀 <b>").Append(sellerName).Append("</b> vừa có đơn hàng mới!");

        var amountPlain = FormatDollarAmount(amount);
        // "• " + amount + " • " — keep $amount • title on one mobile line.
        var amountLinePrefixLen = 2 + amountPlain.Length + 3;
        var firstItemTitleMax = Math.Max(12, NewOrderAmountProductLineMaxChars - amountLinePrefixLen);

        var items = order.Items?.Count > 0 ? order.Items : [];
        if (items.Count == 0)
        {
            sb.Append("\n• ").Append(amountHtml).Append(" • —");
        }
        else
        {
            var first = true;
            foreach (var item in items)
            {
                var titleMax = first ? firstItemTitleMax : ProductTitleMaxChars;
                var title = EscapeHtml(TruncateOneLine(NullDash(item.Title), titleMax));
                if (first)
                {
                    sb.Append("\n• ").Append(amountHtml).Append(" • ").Append(title);
                    first = false;
                }
                else
                {
                    sb.Append("\n• ").Append(title);
                }
            }
        }

        sb.Append("\n• ")
            .Append(EscapeHtml(NullDash(profileName)))
            .Append(" • ")
            .Append(EscapeHtml(NullDash(shopName)));

        sb.Append("\n• ")
            .Append(EscapeHtml(FormatVnDateTimeCompact(orderAt)))
            .Append(" • <code>")
            .Append(EscapeHtml(orderCode))
            .Append("</code>");

        sb.Append('\n').Append(PickNewOrderMotivation());

        return sb.ToString();
    }

    private static string PickNewOrderMotivation() =>
        NewOrderMotivations[Random.Shared.Next(NewOrderMotivations.Length)];

    private static string BuildCanceledOrderMessage(
        Order order,
        Shop? shop,
        string? shopNameFallback,
        string? managerFullName,
        bool sheinPurchased)
    {
        var sellerName = EscapeHtml(FirstNonEmpty(managerFullName, "Seller")!);

        var sb = new StringBuilder();
        sb.Append("🚫 <b>").Append(sellerName).Append("</b> vừa bị hủy đơn!");
        sb.Append("\n————————————————————");

        AppendOrderItems(sb, order, strikethrough: true);
        AppendNewOrderShopProfile(sb, shop, shopNameFallback);
        AppendOrderAmountAndCode(sb, order, strikethrough: true);
        AppendCancelRefundTimeline(sb, order, eventLabel: "Hủy", eventAt: order.CanceledAt);
        AppendSheinPurchasedLine(sb, sheinPurchased);

        sb.Append("\n💪 Đừng nản, đơn mới sẽ sớm về!");

        return sb.ToString();
    }

    private static string BuildAfterSalesOrderMessage(
        Order order,
        Shop? shop,
        string? shopNameFallback,
        string? managerFullName,
        bool sheinPurchased,
        OrderAfterSalesNotice notice)
    {
        var sellerName = EscapeHtml(FirstNonEmpty(managerFullName, "Seller")!);
        var refunded = notice.Outcome == AfterSalesOutcome.Refunded;

        var sb = new StringBuilder();
        sb.Append(notice.Outcome switch
        {
            AfterSalesOutcome.Refunded => "💸 <b>" + sellerName + "</b> vừa có đơn bị refund!",
            AfterSalesOutcome.Closed => "↩️ <b>" + sellerName + "</b> có yêu cầu hoàn đã được đóng.",
            _ => "🛎️ <b>" + sellerName + "</b> có đơn cần xử lý hoàn/trả!"
        });
        sb.Append("\n————————————————————");

        AppendOrderItems(sb, order, strikethrough: refunded);
        AppendNewOrderShopProfile(sb, shop, shopNameFallback);
        AppendOrderAmountAndCode(sb, order, strikethrough: refunded);
        AppendCancelRefundTimeline(
            sb,
            order,
            eventLabel: refunded ? "Refund" : "Yêu cầu",
            eventAt: refunded ? order.RefundedAt ?? order.CanceledAt : null);
        AppendSheinPurchasedLine(sb, sheinPurchased);

        sb.Append("\n• Sau bán: ").Append(EscapeHtml(NullDash(notice.ReturnStatusText)));
        if (!string.IsNullOrWhiteSpace(notice.ReturnReason))
        {
            sb.Append("\n• Lý do: ").Append(EscapeHtml(TruncateOneLine(notice.ReturnReason, 120)));
        }

        if (notice.RefundTotal > 0)
        {
            sb.Append("\n• Tiền hoàn: ").Append(EscapeHtml(FormatDollarAmount(notice.RefundTotal)));
        }

        if (!string.IsNullOrWhiteSpace(notice.ReturnTrackingNumber))
        {
            sb.Append("\n• Mã trả hàng: <code>")
                .Append(EscapeHtml(notice.ReturnTrackingNumber.Trim()))
                .Append("</code>");
        }

        sb.Append(notice.Outcome switch
        {
            AfterSalesOutcome.Refunded => "\n💪 Kiểm tra lý do và xử lý kịp thời.",
            AfterSalesOutcome.Closed => "\n✅ Đơn quay lại trạng thái bán bình thường.",
            _ => "\n⏳ Theo dõi và phản hồi khách trước hạn."
        });

        return sb.ToString();
    }

    private static void AppendCancelRefundTimeline(
        StringBuilder sb,
        Order order,
        string eventLabel,
        DateTime? eventAt)
    {
        var orderAt = order.OrderCreateTime ?? order.OrderPaidTime ?? order.CreatedAt;
        var resolvedEventAt = eventAt ?? DateTime.UtcNow;

        var orderDateText = FormatVnDate(orderAt);
        var eventDateText = eventAt.HasValue ? FormatVnDate(resolvedEventAt) : "—";

        string daysText;
        if (!eventAt.HasValue && orderAt == default)
        {
            daysText = "—";
        }
        else
        {
            var orderLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(orderAt, DateTimeKind.Utc), VietnamTz).Date;
            var eventLocal = TimeZoneInfo.ConvertTimeFromUtc(
                DateTime.SpecifyKind(resolvedEventAt, DateTimeKind.Utc), VietnamTz).Date;
            var days = Math.Max(0, (eventLocal - orderLocal).Days);
            daysText = $"{days} ngày";
        }

        sb.Append("\n• Đặt: ")
            .Append(EscapeHtml(orderDateText))
            .Append(" • ")
            .Append(EscapeHtml(eventLabel))
            .Append(": ")
            .Append(EscapeHtml(eventDateText))
            .Append(" • <b>")
            .Append(EscapeHtml(daysText))
            .Append("</b>");
    }

    private static void AppendSheinPurchasedLine(StringBuilder sb, bool sheinPurchased)
    {
        sb.Append("\n• Shein: ")
            .Append(sheinPurchased ? "✅ Đã mua" : "❌ Chưa mua");
    }

    private static string FormatVnDate(DateTime utc)
    {
        if (utc == default)
        {
            return "—";
        }

        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), VietnamTz);
        return local.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
    }

    /// <summary>VN local time in an explicit format; <c>—</c> when the timestamp was never set.</summary>
    private static string FormatVnDateTime(DateTime utc, string format)
    {
        if (utc == default)
        {
            return "—";
        }

        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), VietnamTz);
        return local.ToString(format, CultureInfo.InvariantCulture);
    }

    /// <summary>VN local compact: <c>22/3 09:16</c> (no leading zeros on day/month).</summary>
    private static string FormatVnDateTimeCompact(DateTime utc)
    {
        if (utc == default)
        {
            return "—";
        }

        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), VietnamTz);
        return local.ToString("d/M HH:mm", CultureInfo.InvariantCulture);
    }

    private static void AppendOrderItems(StringBuilder sb, Order order, bool strikethrough = false)
    {
        var items = order.Items?.Count > 0 ? order.Items : [];
        if (items.Count == 0)
        {
            sb.Append("\n• —");
            return;
        }

        foreach (var item in items)
        {
            var title = EscapeHtml(TruncateOneLine(NullDash(item.Title), ProductTitleMaxChars));
            sb.Append("\n• ");
            if (strikethrough)
            {
                sb.Append("<s>").Append(title).Append("</s>");
            }
            else
            {
                sb.Append(title);
            }
        }
    }

    private static void AppendNewOrderShopProfile(
        StringBuilder sb,
        Shop? shop,
        string? shopNameFallback)
    {
        var shopName = FirstNonEmpty(shop?.Name, shopNameFallback);
        var profileName = FirstNonEmpty(shop?.ProfileName);

        sb.Append("\n• ")
            .Append(EscapeHtml(NullDash(shopName)))
            .Append(" • ")
            .Append(EscapeHtml(NullDash(profileName)));
    }

    private static void AppendOrderAmountAndCode(StringBuilder sb, Order order, bool strikethrough = false)
    {
        var amount = OrderRevenue.Resolve(order);
        var orderCode = FirstNonEmpty(order.PlatformOrderId, order.FourSellerOrderId.ToString(CultureInfo.InvariantCulture))!;
        var amountHtml = "<b>" + EscapeHtml(FormatDollarAmount(amount)) + "</b>";

        sb.Append("\n• ");
        if (strikethrough)
        {
            sb.Append("<s>").Append(amountHtml).Append("</s>");
        }
        else
        {
            sb.Append(amountHtml);
        }

        sb.Append(" • <code>")
            .Append(EscapeHtml(orderCode))
            .Append("</code>");
    }

    /// <summary>
    /// Whether a new-order Telegram should be attempted (shop resolved, not yet stamped, product complete).
    /// </summary>
    internal static bool ShouldAttemptNewOrderNotify(Order order, Shop? shop) =>
        order.TelegramNewOrderNotifiedAt is null
        && shop is not null
        && HasCompleteProductInfo(order);

    /// <summary>
    /// Applies an atomic claim result onto the tracked entity. <paramref name="rowsAffected"/> is
    /// the return of <c>ExecuteUpdate</c> that set <c>TelegramNewOrderNotifiedAt</c> only when null.
    /// </summary>
    internal static bool ApplyNewOrderNotifyClaim(Order order, int rowsAffected, DateTime claimedAtUtc)
    {
        if (rowsAffected != 1)
        {
            return false;
        }

        order.TelegramNewOrderNotifiedAt = claimedAtUtc;
        order.UpdatedAt = claimedAtUtc;
        return true;
    }

    /// <summary>Clears the in-memory claim after a failed Telegram send (DB cleared separately).</summary>
    internal static void ClearNewOrderNotifyClaim(Order order)
    {
        order.TelegramNewOrderNotifiedAt = null;
    }

    /// <summary>
    /// Atomically stamps <c>TelegramNewOrderNotifiedAt</c> when still null so sync and the
    /// pending sweep cannot both send. Returns false when another path already claimed.
    /// </summary>
    private async Task<bool> TryClaimNewOrderNotifyAsync(
        Order order,
        DateTime claimedAtUtc,
        CancellationToken cancellationToken)
    {
        if (order.Id == Guid.Empty)
        {
            return false;
        }

        if (order.TelegramNewOrderNotifiedAt is not null)
        {
            return false;
        }

        var rows = await _db.Orders
            .Where(o => o.Id == order.Id && o.TelegramNewOrderNotifiedAt == null)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(o => o.TelegramNewOrderNotifiedAt, claimedAtUtc)
                    .SetProperty(o => o.UpdatedAt, claimedAtUtc),
                cancellationToken);

        if (!ApplyNewOrderNotifyClaim(order, rows, claimedAtUtc))
        {
            _logger.LogInformation(
                "Skip Telegram new-order notify for order {OrderId}: already claimed by another path",
                order.Id);
            return false;
        }

        return true;
    }

    /// <summary>Releases a claim after send failure so sync / sweep can retry.</summary>
    private async Task ReleaseNewOrderNotifyClaimAsync(
        Order order,
        CancellationToken cancellationToken)
    {
        ClearNewOrderNotifyClaim(order);
        if (order.Id == Guid.Empty)
        {
            return;
        }

        await _db.Orders
            .Where(o => o.Id == order.Id)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(o => o.TelegramNewOrderNotifiedAt, (DateTime?)null)
                    .SetProperty(o => o.UpdatedAt, DateTime.UtcNow),
                cancellationToken);
    }

    private static bool HasCompleteProductInfo(Order order)
    {
        var items = order.Items;
        if (items is null || items.Count == 0)
        {
            return false;
        }

        var hasProduct = items.Any(i => !string.IsNullOrWhiteSpace(i.Title));
        if (!hasProduct)
        {
            return false;
        }

        var amount = OrderRevenue.Resolve(order);
        return amount > 0 || items.Any(i => i.ItemPrice > 0);
    }

    private static int CountStatus(IEnumerable<string?> statuses, string status) =>
        statuses.Count(s => string.Equals(s, status, StringComparison.OrdinalIgnoreCase));

    private static string FormatDollarAmount(decimal amount) =>
        $"${amount.ToString("0.##", CultureInfo.InvariantCulture)}";

    private static string NullDash(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "—" : value.Trim();

    private static string TruncateOneLine(string value, int maxChars)
    {
        var oneLine = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        if (oneLine.Length <= maxChars)
        {
            return oneLine;
        }

        return oneLine[..(maxChars - 1)].TrimEnd() + "…";
    }

    private static string EscapeHtml(string value) =>
        WebUtility.HtmlEncode(value);

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }

    private static bool IsTruthy(string? value) =>
        string.IsNullOrWhiteSpace(value)
        || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
        || value == "1";

    private static TimeZoneInfo ResolveVietnamTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");
        }
    }

    private sealed record TelegramConfig(
        string BotToken,
        string OrdersChatId,
        string CancelRefundChatId,
        string? NotifyNewOrder,
        string? NotifyCancelRefund,
        string? DailySummaryEnabled,
        string? DailySummaryHour,
        string? DailySummaryLastSentDate,
        string? SellerSummaryEnabled,
        string? SellerSummaryHour,
        string? SellerSummaryLastSentDate,
        string? FulfilSummaryEnabled,
        string? FulfilSummaryHour,
        string? FulfilSummaryLastSentDate,
        string? NotifyUsps,
        string? NotifyWarehouseTrack,
        string? NotifyOrderMessage,
        string? NotifyStaleNoTracking,
        string? NotifyStaleNoTrackingHours,
        string? NotifyStaleNoTrackingLastSentDate);

    private sealed record SellerLine(Guid Id, string DisplayName);
}
