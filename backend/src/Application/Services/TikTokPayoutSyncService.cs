using System.Globalization;
using System.Text.Json;
using Application.DTOs.Shop;
using Application.Interfaces;
using Domain.Entities;
using Domain.Exceptions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Shared.Constants;

namespace Application.Services;

/// <summary>
/// Upserts TikTok payouts into receiving-bank ledger and marks linked orders paid.
/// </summary>
public class TikTokPayoutSyncService : ITikTokPayoutSyncService
{
    private readonly IApplicationDbContext _db;
    private readonly ISettingProvider _settings;
    private readonly ITelegramNotifier _telegram;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ITikTokShopSyncLogWriter _tikTokSyncLog;
    private readonly ILogger<TikTokPayoutSyncService> _logger;

    /// <summary>Initializes the service.</summary>
    public TikTokPayoutSyncService(
        IApplicationDbContext db,
        ISettingProvider settings,
        ITelegramNotifier telegram,
        UserManager<ApplicationUser> userManager,
        ITikTokShopSyncLogWriter tikTokSyncLog,
        ILogger<TikTokPayoutSyncService> logger)
    {
        _db = db;
        _settings = settings;
        _telegram = telegram;
        _userManager = userManager;
        _tikTokSyncLog = tikTokSyncLog;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<TikTokPayoutSyncConfigDto> GetSyncConfigAsync(
        CancellationToken cancellationToken = default)
    {
        var values = await _settings.GetValuesAsync(
            [
                ExtensionTikTokPayoutSyncSettingKeys.Enabled,
                ExtensionTikTokPayoutSyncSettingKeys.IntervalValue,
                ExtensionTikTokPayoutSyncSettingKeys.IntervalUnit,
                ExtensionTikTokPayoutSyncSettingKeys.MaxDate,
                ExtensionTikTokPayoutSyncSettingKeys.NotifyNewPayment
            ],
            cancellationToken);

        var enabled = IsOn(values, ExtensionTikTokPayoutSyncSettingKeys.Enabled);

        var intervalValue = ExtensionTikTokPayoutSyncSettingKeys.DefaultIntervalValue;
        if (values.TryGetValue(ExtensionTikTokPayoutSyncSettingKeys.IntervalValue, out var v)
            && int.TryParse(v, out var parsed)
            && parsed >= 1)
        {
            intervalValue = parsed;
        }

        var unit = values.TryGetValue(ExtensionTikTokPayoutSyncSettingKeys.IntervalUnit, out var u)
            ? (u ?? string.Empty).Trim()
            : ExtensionTikTokPayoutSyncSettingKeys.UnitSeconds;

        var seconds = string.Equals(unit, ExtensionTikTokPayoutSyncSettingKeys.UnitMinutes, StringComparison.OrdinalIgnoreCase)
            ? intervalValue * 60
            : intervalValue;

        string? maxDate = null;
        if (values.TryGetValue(ExtensionTikTokPayoutSyncSettingKeys.MaxDate, out var md)
            && !string.IsNullOrWhiteSpace(md))
        {
            var trimmed = md.Trim();
            if (DateOnly.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            {
                maxDate = d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            }
            else if (DateTime.TryParse(
                         trimmed,
                         CultureInfo.InvariantCulture,
                         DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                         out var dt))
            {
                maxDate = DateOnly.FromDateTime(dt).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            }
        }

        return new TikTokPayoutSyncConfigDto
        {
            Enabled = enabled,
            IntervalSeconds = Math.Max(1, seconds),
            MaxDate = maxDate,
            NotifyNewPayment = IsOn(values, ExtensionTikTokPayoutSyncSettingKeys.NotifyNewPayment)
        };
    }

    /// <inheritdoc />
    public async Task<TikTokPayoutSyncCursorDto> GetCursorAsync(
        Guid ownerId,
        string externalShopId,
        CancellationToken cancellationToken = default)
    {
        var shopId = await ResolveShopIdAsync(ownerId, externalShopId, cancellationToken);
        if (shopId is null)
        {
            return new TikTokPayoutSyncCursorDto();
        }

        var latest = await _db.ReceivingBankTransactions
            .AsNoTracking()
            .Where(t =>
                t.ShopId == shopId
                && t.Source == ReceivingBankTransaction.SourceTikTokPayout
                && t.ExternalTransactionId != null)
            .OrderByDescending(t => t.OccurredAt)
            .ThenByDescending(t => t.CreatedAt)
            .Select(t => new { t.OccurredAt, t.ExternalTransactionId })
            .FirstOrDefaultAsync(cancellationToken);

        return new TikTokPayoutSyncCursorDto
        {
            LatestOccurredAt = latest?.OccurredAt,
            LatestExternalTransactionId = latest?.ExternalTransactionId
        };
    }

    /// <inheritdoc />
    public async Task<SyncTikTokPayoutsResultDto> SyncAsync(
        Guid ownerId,
        SyncTikTokPayoutsDto dto,
        CancellationToken cancellationToken = default)
    {
        var externalShopId = ResolveExternalShopId(dto);
        Guid? shopId = null;
        try
        {
        var shop = await _db.Shops
            .FirstOrDefaultAsync(
                s => s.OwnerId == ownerId && s.ExternalShopId == externalShopId,
                cancellationToken);

        if (shop is null)
        {
            var other = await (
                from s in _db.Shops.AsNoTracking()
                join u in _db.Users.AsNoTracking() on s.OwnerId equals u.Id into owners
                from u in owners.DefaultIfEmpty()
                where s.ExternalShopId == externalShopId
                select new
                {
                    s.Name,
                    OwnerUserName = u != null ? u.UserName : null
                })
                .FirstOrDefaultAsync(cancellationToken);

            if (other is not null)
            {
                var ownerLabel = string.IsNullOrWhiteSpace(other.OwnerUserName)
                    ? "(unknown)"
                    : other.OwnerUserName;
                throw new NotFoundException(
                    $"Shop «{other.Name}» (externalShopId «{externalShopId}») thuộc seller «{ownerLabel}» — " +
                    "đăng nhập đúng tài khoản owner trên extension rồi sync lại.");
            }

            throw new NotFoundException(
                $"Chưa có shop với externalShopId «{externalShopId}» — tạo/gán shop trên admin trước.");
        }

        shopId = shop.Id;

        if (!shop.ReceivingBankAccountId.HasValue)
        {
            throw new BusinessException("Shop chưa gắn thẻ nhận tiền — không thể sync payout.");
        }

        var cardActive = await _db.ReceivingBankAccounts.AnyAsync(
            a => a.Id == shop.ReceivingBankAccountId.Value && a.IsActive,
            cancellationToken);
        if (!cardActive)
        {
            throw new NotFoundException("Không tìm thấy thẻ nhận tiền hoặc thẻ đã vô hiệu.");
        }

        var receivingCard = await _db.ReceivingBankAccounts
            .AsNoTracking()
            .Where(a => a.Id == shop.ReceivingBankAccountId.Value)
            .Select(a => new
            {
                SourceName = a.Source != null ? a.Source.Name : null,
                a.AccountNumber
            })
            .FirstOrDefaultAsync(cancellationToken);
        var receivingCardSourceName = string.IsNullOrWhiteSpace(receivingCard?.SourceName)
            ? null
            : receivingCard!.SourceName.Trim();
        var receivingCardMaskedNumber = MaskReceivingAccountNumber(receivingCard?.AccountNumber);

        var result = new SyncTikTokPayoutsResultDto { ShopId = shop.Id };
        var config = await GetSyncConfigAsync(cancellationToken);
        var notify = config.NotifyNewPayment;

        var owner = await _userManager.FindByIdAsync(shop.OwnerId.ToString());
        var sellerName = string.IsNullOrWhiteSpace(owner?.FullName)
            ? (owner?.UserName ?? "Seller")
            : owner!.FullName.Trim();

        var paymentIds = dto.Payments
            .Select(p => (p.PaymentId ?? string.Empty).Trim())
            .Where(id => id.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        // Only consider TikTokPayout rows for this shop — never overwrite Manual/SettlementPaid
        // or another shop's ledger via shared ExternalTransactionId.
        var existingByExtId = new Dictionary<string, ReceivingBankTransaction>(StringComparer.Ordinal);
        if (paymentIds.Count > 0)
        {
            var existing = await _db.ReceivingBankTransactions
                .Where(t =>
                    t.ShopId == shop.Id
                    && t.Source == ReceivingBankTransaction.SourceTikTokPayout
                    && t.ExternalTransactionId != null
                    && paymentIds.Contains(t.ExternalTransactionId))
                .ToListAsync(cancellationToken);
            foreach (var row in existing)
            {
                if (!string.IsNullOrEmpty(row.ExternalTransactionId))
                {
                    existingByExtId[row.ExternalTransactionId] = row;
                }
            }

            var conflicting = await _db.ReceivingBankTransactions
                .AsNoTracking()
                .Where(t =>
                    t.ExternalTransactionId != null
                    && paymentIds.Contains(t.ExternalTransactionId)
                    && (t.ShopId != shop.Id
                        || t.Source != ReceivingBankTransaction.SourceTikTokPayout))
                .Select(t => new { t.ExternalTransactionId, t.ShopId, t.Source })
                .ToListAsync(cancellationToken);
            if (conflicting.Count > 0)
            {
                var sample = conflicting[0];
                throw new BusinessException(
                    $"ExternalTransactionId «{sample.ExternalTransactionId}» đã tồn tại " +
                    $"(source={sample.Source}, shopId={sample.ShopId}) — không ghi đè để bảo vệ sổ cái.");
            }
        }

        var newPayoutsForTelegram = new List<(
            decimal Amount,
            string Currency,
            DateTime OccurredAt,
            string PaymentId)>();

        foreach (var payment in dto.Payments)
        {
            var paymentId = (payment.PaymentId ?? string.Empty).Trim();
            if (paymentId.Length == 0)
            {
                result.Skipped++;
                continue;
            }

            var occurredAt = ParseOccurredAt(payment.SuccessTime);
            if (occurredAt is null)
            {
                _logger.LogWarning(
                    "Skip TikTok payout {PaymentId}: missing/invalid successTime",
                    paymentId);
                result.Skipped++;
                continue;
            }

            var amount = payment.Amount < 0 ? Math.Abs(payment.Amount) : payment.Amount;
            if (amount <= 0)
            {
                _logger.LogWarning(
                    "Skip TikTok payout {PaymentId}: non-positive amount {Amount}",
                    paymentId,
                    payment.Amount);
                result.Skipped++;
                continue;
            }

            var currency = string.IsNullOrWhiteSpace(payment.Currency)
                ? "USD"
                : payment.Currency.Trim().ToUpperInvariant();
            var note = BuildNote(payment);

            var isNew = !existingByExtId.TryGetValue(paymentId, out var txn);
            if (isNew)
            {
                txn = new ReceivingBankTransaction
                {
                    ReceivingBankAccountId = shop.ReceivingBankAccountId.Value,
                    ShopId = shop.Id,
                    OrderId = null,
                    Amount = amount,
                    Direction = ReceivingBankTransaction.DirectionCredit,
                    Currency = currency,
                    Note = note,
                    ExternalTransactionId = paymentId,
                    PayoutDetailsJson = SerializePayoutOrders(payment.Orders),
                    Source = ReceivingBankTransaction.SourceTikTokPayout,
                    ConfirmationStatus = ResolvePayoutConfirmationStatus(
                        payment.BankCardMask,
                        receivingCard?.AccountNumber,
                        existingStatus: null),
                    CreatedByUserId = ownerId,
                    OccurredAt = occurredAt.Value
                };
                _db.ReceivingBankTransactions.Add(txn);
                existingByExtId[paymentId] = txn;
                result.Inserted++;
                if (notify)
                {
                    newPayoutsForTelegram.Add((amount, currency, occurredAt.Value, paymentId));
                }
            }
            else
            {
                txn!.ReceivingBankAccountId = shop.ReceivingBankAccountId.Value;
                txn.ShopId = shop.Id;
                txn.Amount = amount;
                txn.Direction = ReceivingBankTransaction.DirectionCredit;
                txn.Currency = currency;
                txn.Note = note;
                txn.PayoutDetailsJson = SerializePayoutOrders(payment.Orders) ?? txn.PayoutDetailsJson;
                txn.Source = ReceivingBankTransaction.SourceTikTokPayout;
                txn.ConfirmationStatus = ResolvePayoutConfirmationStatus(
                    payment.BankCardMask,
                    receivingCard?.AccountNumber,
                    txn.ConfirmationStatus);
                txn.OccurredAt = occurredAt.Value;
                txn.UpdatedAt = DateTime.UtcNow;
                result.Updated++;
            }

            var marked = await MarkOrdersPaidAsync(
                shop.Id,
                paymentId,
                payment.Orders,
                cancellationToken);
            result.OrdersMarkedPaid += marked;
        }

        await _db.SaveChangesAsync(cancellationToken);

        foreach (var payout in newPayoutsForTelegram)
        {
            try
            {
                await _telegram.NotifyNewTikTokPayoutAsync(
                    sellerName,
                    shop.ProfileName,
                    shop.Name,
                    payout.Amount,
                    payout.Currency,
                    payout.OccurredAt,
                    payout.PaymentId,
                    receivingCardSourceName,
                    receivingCardMaskedNumber,
                    cancellationToken);
                result.TelegramSent++;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Telegram new TikTok payout notify failed for shop {ShopId}", shop.Id);
            }
        }

        await _tikTokSyncLog.WriteAsync(
            ownerId,
            dto.RunId,
            externalShopId,
            shop.Id,
            TikTokShopSyncTypes.Payout,
            TikTokShopSyncStatuses.Completed,
            cancellationToken: cancellationToken);

        return result;
        }
        catch (Exception ex)
        {
            await _tikTokSyncLog.WriteAsync(
                ownerId,
                dto.RunId,
                externalShopId,
                shopId,
                TikTokShopSyncTypes.Payout,
                TikTokShopSyncStatuses.Failed,
                ex.Message,
                cancellationToken: cancellationToken);
            throw;
        }
    }

    private async Task<int> MarkOrdersPaidAsync(
        Guid shopId,
        string paymentId,
        List<SyncTikTokPayoutOrderDto>? orders,
        CancellationToken cancellationToken)
    {
        if (orders is null || orders.Count == 0)
        {
            return 0;
        }

        var tradeIds = orders
            .Select(o => (o.TradeOrderId ?? string.Empty).Trim())
            .Where(id => id.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (tradeIds.Count == 0)
        {
            return 0;
        }

        var matched = await _db.Orders
            .Where(o =>
                o.ShopId == shopId
                && o.PlatformOrderId != null
                && tradeIds.Contains(o.PlatformOrderId))
            .ToListAsync(cancellationToken);

        if (matched.Count == 0)
        {
            return 0;
        }

        // TikTokPayout is the bank truth — drop per-order SettlementPaid rows to avoid double-count.
        var matchedIds = matched.Select(o => o.Id).ToList();
        var settlementPaidRows = await _db.ReceivingBankTransactions
            .Where(t =>
                t.OrderId != null
                && matchedIds.Contains(t.OrderId.Value)
                && t.Source == ReceivingBankTransaction.SourceSettlementPaid)
            .ToListAsync(cancellationToken);
        if (settlementPaidRows.Count > 0)
        {
            _db.ReceivingBankTransactions.RemoveRange(settlementPaidRows);
        }

        var marked = 0;
        foreach (var order in matched)
        {
            var changed = false;
            if (!order.IsSettlementPaid)
            {
                order.IsSettlementPaid = true;
                changed = true;
            }

            if (!string.Equals(order.TikTokPaymentId, paymentId, StringComparison.Ordinal))
            {
                order.TikTokPaymentId = paymentId;
                changed = true;
            }

            if (changed)
            {
                order.UpdatedAt = DateTime.UtcNow;
                marked++;
            }
        }

        return marked;
    }

    private async Task<Guid?> ResolveShopIdAsync(
        Guid ownerId,
        string externalShopId,
        CancellationToken cancellationToken)
    {
        var id = externalShopId.Trim();
        if (id.Length == 0)
        {
            return null;
        }

        return await _db.Shops
            .AsNoTracking()
            .Where(s => s.OwnerId == ownerId && s.ExternalShopId == id)
            .Select(s => (Guid?)s.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static string ResolveExternalShopId(SyncTikTokPayoutsDto dto)
    {
        if (!string.IsNullOrWhiteSpace(dto.ExternalShopId))
        {
            return dto.ExternalShopId.Trim();
        }

        return (dto.SellerId ?? string.Empty).Trim();
    }

    /// <summary>
    /// Auto-confirm when TikTok <c>bank_card_mask_code</c> last-4 matches the shop card.
    /// Keep admin Rejected / Confirmed; re-evaluate only new or still-Pending rows.
    /// </summary>
    private static string ResolvePayoutConfirmationStatus(
        string? bankCardMask,
        string? accountNumber,
        string? existingStatus)
    {
        if (string.Equals(
                existingStatus,
                ReceivingBankTransaction.ConfirmationRejected,
                StringComparison.Ordinal))
        {
            return ReceivingBankTransaction.ConfirmationRejected;
        }

        if (string.Equals(
                existingStatus,
                ReceivingBankTransaction.ConfirmationConfirmed,
                StringComparison.Ordinal))
        {
            return ReceivingBankTransaction.ConfirmationConfirmed;
        }

        return ReceivingBankCardMask.MatchesAccountLast4(bankCardMask, accountNumber)
            ? ReceivingBankTransaction.ConfirmationConfirmed
            : ReceivingBankTransaction.ConfirmationPending;
    }

    private static string? BuildNote(SyncTikTokPayoutItemDto payment)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(payment.Note))
        {
            parts.Add(payment.Note.Trim());
        }

        if (!string.IsNullOrWhiteSpace(payment.BankCardMask))
        {
            parts.Add($"card {payment.BankCardMask.Trim()}");
        }

        if (parts.Count == 0)
        {
            return $"TikTok payout {payment.PaymentId}";
        }

        var joined = string.Join(" · ", parts);
        return joined.Length <= 500 ? joined : joined[..500];
    }

    private static string? SerializePayoutOrders(List<SyncTikTokPayoutOrderDto>? orders)
    {
        if (orders is null || orders.Count == 0)
        {
            return null;
        }

        var rows = orders
            .Select(o => new
            {
                tradeOrderId = (o.TradeOrderId ?? string.Empty).Trim(),
                settlementAmount = o.SettlementAmount,
                earningAmount = o.EarningAmount,
                fees = o.Fees,
                settlementTime = string.IsNullOrWhiteSpace(o.SettlementTime) ? null : o.SettlementTime.Trim()
            })
            .Where(o => o.tradeOrderId.Length > 0)
            .GroupBy(o => o.tradeOrderId, StringComparer.Ordinal)
            .Select(g => g.First())
            .ToList();

        if (rows.Count == 0)
        {
            return null;
        }

        return JsonSerializer.Serialize(rows);
    }

    private static DateTime? ParseOccurredAt(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var trimmed = raw.Trim();
        if (long.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ms))
        {
            // TikTok uses ms; guard seconds-scale.
            if (ms > 1_000_000_000_000L)
            {
                return DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime;
            }

            if (ms > 1_000_000_000L)
            {
                return DateTimeOffset.FromUnixTimeSeconds(ms).UtcDateTime;
            }
        }

        if (DateTimeOffset.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dto))
        {
            return dto.UtcDateTime;
        }

        return null;
    }

    private static bool IsOn(IReadOnlyDictionary<string, string> values, string key) =>
        values.TryGetValue(key, out var v)
        && string.Equals(v?.Trim(), "true", StringComparison.OrdinalIgnoreCase);

    /// <summary>Same mask as receiving-bank ledger UI: <c>0039****9978</c>.</summary>
    private static string? MaskReceivingAccountNumber(string? number)
    {
        if (string.IsNullOrWhiteSpace(number))
        {
            return null;
        }

        var digits = new string(number.Where(char.IsDigit).ToArray());
        if (digits.Length == 0)
        {
            return null;
        }

        if (digits.Length <= 8)
        {
            return digits;
        }

        return $"{digits[..4]}****{digits[^4..]}";
    }
}
