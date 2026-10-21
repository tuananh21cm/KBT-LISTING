using Application.DTOs.FourSeller;
using Application.DTOs.Shop;
using Application.Interfaces;
using Domain.Entities;
using Domain.Exceptions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shared.Constants;
using Shared.Helpers;
using System.Text.Json;

namespace Application.Services;

/// <summary>
/// Shop management service implementation.
/// </summary>
public class ShopService : IShopService
{
    /// <summary>
    /// Prevents overlapping admin multi-cookie shop syncs from racing on upserts/rematch.
    /// </summary>
    private static readonly SemaphoreSlim SyncGate = new(1, 1);
    private const int MaxShopCatalogCategories = 20;
    private const string CatalogNameJoin = " · ";

    private readonly IApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IFourSellerApiService _fourSellerApiService;
    private readonly IOrderSyncService _orderSyncService;
    private readonly IShopOperationalPeriodService _operationalPeriods;
    private readonly IShopOwnershipTransferService _ownershipTransfer;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ISettingProvider _settings;
    private readonly ITelegramNotifier _telegram;
    private readonly ITikTokShopSyncLogWriter _tikTokSyncLog;
    private readonly IListingAutoPublishCancellationCoordinator _autoPublishCancellation;
    private readonly IShopAutoPublishSchedulerStatusResolver _shopAutoPublishSchedulerStatus;
    private readonly ILogger<ShopService> _logger;

    /// <summary>
    /// Initializes the shop service.
    /// </summary>
    public ShopService(
        IApplicationDbContext context,
        UserManager<ApplicationUser> userManager,
        IFourSellerApiService fourSellerApiService,
        IOrderSyncService orderSyncService,
        IShopOperationalPeriodService operationalPeriods,
        IShopOwnershipTransferService ownershipTransfer,
        IServiceScopeFactory scopeFactory,
        ISettingProvider settings,
        ITelegramNotifier telegram,
        ITikTokShopSyncLogWriter tikTokSyncLog,
        IListingAutoPublishCancellationCoordinator autoPublishCancellation,
        IShopAutoPublishSchedulerStatusResolver shopAutoPublishSchedulerStatus,
        ILogger<ShopService> logger)
    {
        _context = context;
        _userManager = userManager;
        _fourSellerApiService = fourSellerApiService;
        _orderSyncService = orderSyncService;
        _operationalPeriods = operationalPeriods;
        _ownershipTransfer = ownershipTransfer;
        _scopeFactory = scopeFactory;
        _settings = settings;
        _telegram = telegram;
        _tikTokSyncLog = tikTokSyncLog;
        _autoPublishCancellation = autoPublishCancellation;
        _shopAutoPublishSchedulerStatus = shopAutoPublishSchedulerStatus;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<List<ShopDto>> GetAllAsync(Guid ownerId, CancellationToken cancellationToken = default)
    {
        var shops = await _context.Shops
            .AsNoTracking()
            .Include(s => s.Platform)
            .Include(s => s.ReceivingBankAccount!)
                .ThenInclude(a => a.Source)
            .Include(s => s.ReceivingBankAccount!)
                .ThenInclude(a => a.SourceAccount)
            .Where(s => s.OwnerId == ownerId)
            .OrderBy(s => s.CreatedAt)
            .ThenBy(s => s.Id)
            .ToListAsync(cancellationToken);

        var dtos = shops.Select(MapToDto).ToList();
        await EnrichAutoPublishSchedulerStatusAsync(dtos, cancellationToken);
        await EnrichTodayOnlineSecondsAsync(dtos, cancellationToken);
        return dtos;
    }

    /// <inheritdoc />
    public async Task<List<ScraperShopOptionDto>> GetScraperOptionsAsync(
        Guid ownerId,
        CancellationToken cancellationToken = default)
    {
        var shops = await _context.Shops
            .AsNoTracking()
            .Include(s => s.Platform)
            .Where(s =>
                s.OwnerId == ownerId &&
                s.Status == ShopStatuses.Active &&
                s.FourSellerShopId != null &&
                s.FourSellerShopId != "")
            .OrderBy(s => s.SortOrder)
            .ThenBy(s => s.Name)
            .ToListAsync(cancellationToken);

        if (shops.Count == 0)
        {
            return [];
        }

        var shopIds = shops.Select(s => s.Id).ToList();
        var listingStats = await _context.Listings
            .AsNoTracking()
            .Where(l =>
                l.OwnerId == ownerId &&
                l.ShopId != null &&
                shopIds.Contains(l.ShopId.Value) &&
                l.SiteStatus != null &&
                l.SiteStatus.ToLower() == ListingSiteStatuses.Active)
            .GroupBy(l => l.ShopId!.Value)
            .Select(g => new { ShopId = g.Key, ActiveCount = g.Count() })
            .ToDictionaryAsync(x => x.ShopId, x => x.ActiveCount, cancellationToken);

        var result = new List<ScraperShopOptionDto>();
        foreach (var shop in shops)
        {
            if (!long.TryParse(shop.FourSellerShopId, out var fsId) || fsId <= 0)
            {
                continue;
            }

            result.Add(new ScraperShopOptionDto
            {
                Id = shop.Id,
                FourSellerShopId = fsId,
                Name = shop.Name,
                ProfileName = shop.ProfileName,
                PlatformCode = shop.Platform?.Code ?? string.Empty,
                PlatformAvatarUrl = shop.Platform?.AvatarUrl,
                Region = string.IsNullOrWhiteSpace(shop.Region) ? null : shop.Region.Trim(),
                ProductCategories = ParseJsonList<ShopProductCategoryDto>(shop.ProductCategories),
                Categories = ReadCatalogCategories(shop.CatalogCategories, shop.CategoryId, shop.CategoryName),
                CategoryId = shop.CategoryId,
                CategoryName = shop.CategoryName,
                AgeMin = shop.AgeMin,
                AgeMax = shop.AgeMax,
                PriceMinUsd = shop.PriceMinUsd,
                PriceMaxUsd = shop.PriceMaxUsd,
                PreferQuickShip = shop.PreferQuickShip,
                PreferLocal = shop.PreferLocal,
                PreferUsWarehouse = shop.PreferUsWarehouse,
                PreferSheinClub = shop.PreferSheinClub,
                MinRating = shop.MinRating,
                MinSoldCount = shop.MinSoldCount,
                MaxVariantCount = shop.MaxVariantCount,
                BodySizeCode = shop.BodySizeCode,
                StyleCodes = ReadStyleCodes(shop.StyleCodes),
                ActiveCount = listingStats.GetValueOrDefault(shop.Id)
            });
        }

        return result;
    }

    /// <inheritdoc />
    public async Task<ShopDto> GetByIdAsync(Guid ownerId, Guid shopId, CancellationToken cancellationToken = default)
    {
        var shop = await FindOwnedShopAsync(ownerId, shopId, cancellationToken);
        return MapToDto(shop);
    }

    /// <inheritdoc />
    public async Task<ShopDto> UpdateAsync(Guid ownerId, Guid shopId, UpdateShopDto dto, CancellationToken cancellationToken = default)
    {
        var shop = await FindOwnedShopAsync(ownerId, shopId, cancellationToken);
        await EnsureUniqueExternalShopIdAsync(shop.OwnerId, dto.ExternalShopId, shopId, cancellationToken);

        var previousStatus = shop.Status;
        var previousOwnerId = shop.OwnerId;

        shop.Name = dto.Name;
        shop.ExternalShopId = NormalizeOptionalString(dto.ExternalShopId);
        var fourSellerIdChanged = ApplyFourSellerConnection(shop, dto.FourSellerShopId);
        shop.AvatarUrl = NormalizeOptionalString(dto.AvatarUrl);
        shop.Status = dto.Status;
        shop.ShopUrl = dto.ShopUrl;
        shop.Metadata = dto.Metadata;
        ApplyExtendedFields(shop, dto);
        shop.ShopUrl = NormalizeOptionalString(shop.ShopUrl)
            ?? BuildTikTokShopStoreUrl(shop.Name, shop.ExternalShopId)
            ?? shop.ShopUrl;
        shop.UpdatedAt = DateTime.UtcNow;

        await _operationalPeriods.OnShopUpdatedAsync(
            shop,
            previousStatus,
            previousOwnerId,
            cancellationToken);
        MaybeCancelAutoPublishOnShopDeactivate(shop, previousStatus);
        await SaveChangesAllowingOpenPeriodRaceAsync(shop.Id, cancellationToken);
        await TryNotifyShopStatusChangeAsync(shop, previousStatus, "seller", cancellationToken);
        await TryRelinkOrdersAfterFourSellerIdChangeAsync(shop, fourSellerIdChanged, cancellationToken);
        return MapToDto(shop);
    }

    /// <inheritdoc />
    public async Task<ShopCatalogProfileDto> UpdateCatalogProfileAsync(
        Guid ownerId,
        Guid shopId,
        UpdateShopCatalogProfileDto dto,
        CancellationToken cancellationToken = default)
    {
        var shop = await _context.Shops
            .FirstOrDefaultAsync(s => s.Id == shopId && s.OwnerId == ownerId, cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy shop.");

        await ApplyCatalogProfileAsync(shop, dto, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
        return MapToCatalogProfileDto(shop);
    }

    /// <inheritdoc />
    public async Task<ShopAutoPublishDto> UpdateAutoPublishAsync(
        Guid ownerId,
        Guid shopId,
        UpdateShopAutoPublishDto dto,
        CancellationToken cancellationToken = default)
    {
        var shop = await _context.Shops
            .FirstOrDefaultAsync(s => s.Id == shopId && s.OwnerId == ownerId, cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy shop.");

        await ApplyAutoPublishAsync(shop, dto, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
        return MapToAutoPublishDto(shop);
    }

    /// <inheritdoc />
    public async Task<ShopDto> SyncTikTokProfileAsync(
        Guid ownerId,
        SyncTikTokShopProfileDto dto,
        CancellationToken cancellationToken = default)
    {
        var sellerId = dto.SellerId.Trim();
        Guid? shopId = null;
        try
        {
        var shop = await _context.Shops
            .Include(s => s.Platform)
            .Include(s => s.ReceivingBankAccount!)
                .ThenInclude(a => a.Source)
            .Include(s => s.ReceivingBankAccount!)
                .ThenInclude(a => a.SourceAccount)
            .FirstOrDefaultAsync(
                s => s.OwnerId == ownerId && s.ExternalShopId == sellerId,
                cancellationToken);

        if (shop is null)
        {
            var other = await (
                from s in _context.Shops.AsNoTracking()
                join u in _context.Users.AsNoTracking() on s.OwnerId equals u.Id into owners
                from u in owners.DefaultIfEmpty()
                where s.ExternalShopId == sellerId
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
                    $"Shop «{other.Name}» (externalShopId «{sellerId}») thuộc seller «{ownerLabel}» — " +
                    "đăng nhập đúng tài khoản owner trên extension rồi sync lại.");
            }

            throw new NotFoundException(
                $"Chưa có shop với externalShopId «{sellerId}» — tạo/gán shop trên admin trước.");
        }

        shopId = shop.Id;

        var oldEmail = shop.Email;
        var oldMobile = shop.NewPhoneNumber;
        var oldBank = shop.SettlementBankAccountNumber;
        var previousStatus = shop.Status;
        var previousOwnerId = shop.OwnerId;

        ApplyTikTokProfile(shop, dto);
        shop.TikTokProfileSyncedAt = DateTime.UtcNow;
        shop.UpdatedAt = DateTime.UtcNow;

        var statusChanged = !string.Equals(previousStatus, shop.Status, StringComparison.OrdinalIgnoreCase);
        var ownerChanged = previousOwnerId != shop.OwnerId;
        if (statusChanged || ownerChanged)
        {
            await _operationalPeriods.OnShopUpdatedAsync(
                shop,
                previousStatus,
                previousOwnerId,
                cancellationToken);
        }

        await SaveChangesAllowingOpenPeriodRaceAsync(shop.Id, cancellationToken);

        await TryNotifyTikTokProfileAlertsAsync(
            shop,
            oldEmail,
            oldMobile,
            oldBank,
            cancellationToken);

        await TryNotifyShopStatusChangeAsync(shop, previousStatus, "tiktok_sync", cancellationToken);

        // Note nguồn publish_limit đi kèm cả log Completed: sync xong mà thiếu limit là chuyện
        // của gateway product bên TikTok, không phải lỗi của lượt sync này.
        await _tikTokSyncLog.WriteAsync(
            ownerId,
            dto.RunId,
            sellerId,
            shop.Id,
            TikTokShopSyncTypes.Profile,
            TikTokShopSyncStatuses.Completed,
            NormalizeOptionalString(dto.SyncNote),
            cancellationToken: cancellationToken);

        return MapToDto(shop);
        }
        catch (Exception ex)
        {
            await _tikTokSyncLog.WriteAsync(
                ownerId,
                dto.RunId,
                sellerId,
                shopId,
                TikTokShopSyncTypes.Profile,
                TikTokShopSyncStatuses.Failed,
                ex.Message,
                cancellationToken: cancellationToken);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<TikTokShopSyncConfigDto> GetTikTokShopSyncConfigAsync(
        CancellationToken cancellationToken = default)
    {
        var values = await _settings.GetValuesAsync(
            [
                ExtensionTikTokShopSyncSettingKeys.Enabled,
                ExtensionTikTokShopSyncSettingKeys.IntervalValue,
                ExtensionTikTokShopSyncSettingKeys.IntervalUnit
            ],
            cancellationToken);

        var enabled = values.TryGetValue(ExtensionTikTokShopSyncSettingKeys.Enabled, out var e)
            && string.Equals(e?.Trim(), "true", StringComparison.OrdinalIgnoreCase);

        var intervalValue = ExtensionTikTokShopSyncSettingKeys.DefaultIntervalValue;
        if (values.TryGetValue(ExtensionTikTokShopSyncSettingKeys.IntervalValue, out var v)
            && int.TryParse(v, out var parsed)
            && parsed >= 1)
        {
            intervalValue = parsed;
        }

        var unit = values.TryGetValue(ExtensionTikTokShopSyncSettingKeys.IntervalUnit, out var u)
            ? (u ?? string.Empty).Trim()
            : ExtensionTikTokShopSyncSettingKeys.UnitSeconds;

        var seconds = string.Equals(unit, ExtensionTikTokShopSyncSettingKeys.UnitMinutes, StringComparison.OrdinalIgnoreCase)
            ? intervalValue * 60
            : intervalValue;

        return new TikTokShopSyncConfigDto
        {
            Enabled = enabled,
            IntervalSeconds = Math.Max(1, seconds)
        };
    }

    /// <inheritdoc />
    public async Task<TikTokUnifiedSyncConfigDto> GetTikTokUnifiedSyncConfigAsync(
        CancellationToken cancellationToken = default)
    {
        var values = await _settings.GetValuesAsync(
            [
                ExtensionTikTokUnifiedSyncSettingKeys.StepInfo,
                ExtensionTikTokUnifiedSyncSettingKeys.StepFinance,
                ExtensionTikTokUnifiedSyncSettingKeys.StepKeywords,
                ExtensionTikTokUnifiedSyncSettingKeys.StepAnalytics,
                ExtensionTikTokUnifiedSyncSettingKeys.StepHealthCenter,
                ExtensionTikTokUnifiedSyncSettingKeys.StepShopScore
            ],
            cancellationToken);

        static bool StepEnabled(Dictionary<string, string> map, string key)
        {
            if (!map.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw))
                return ExtensionTikTokUnifiedSyncSettingKeys.DefaultStepEnabled;
            return string.Equals(raw.Trim(), "true", StringComparison.OrdinalIgnoreCase);
        }

        return new TikTokUnifiedSyncConfigDto
        {
            Info = StepEnabled(values, ExtensionTikTokUnifiedSyncSettingKeys.StepInfo),
            Finance = StepEnabled(values, ExtensionTikTokUnifiedSyncSettingKeys.StepFinance),
            Keywords = StepEnabled(values, ExtensionTikTokUnifiedSyncSettingKeys.StepKeywords),
            Analytics = StepEnabled(values, ExtensionTikTokUnifiedSyncSettingKeys.StepAnalytics),
            HealthCenter = StepEnabled(values, ExtensionTikTokUnifiedSyncSettingKeys.StepHealthCenter),
            ShopScore = StepEnabled(values, ExtensionTikTokUnifiedSyncSettingKeys.StepShopScore),
        };
    }

    private async Task TryNotifyTikTokProfileAlertsAsync(
        Shop shop,
        string? oldEmail,
        string? oldMobile,
        string? oldBank,
        CancellationToken cancellationToken)
    {
        try
        {
            var values = await _settings.GetValuesAsync(
                [
                    ExtensionTikTokShopSyncSettingKeys.NotifyEmailChange,
                    ExtensionTikTokShopSyncSettingKeys.NotifyMobileChange,
                    ExtensionTikTokShopSyncSettingKeys.NotifyBankChange,
                    ExtensionTikTokShopSyncSettingKeys.NotifyBankLast4Mismatch
                ],
                cancellationToken);

            static bool IsOn(Dictionary<string, string> map, string key) =>
                map.TryGetValue(key, out var raw)
                && string.Equals(raw?.Trim(), "true", StringComparison.OrdinalIgnoreCase);

            var owner = await _userManager.FindByIdAsync(shop.OwnerId.ToString());
            var sellerName = string.IsNullOrWhiteSpace(owner?.FullName)
                ? (owner?.UserName ?? "Seller")
                : owner!.FullName.Trim();

            if (IsOn(values, ExtensionTikTokShopSyncSettingKeys.NotifyEmailChange)
                && IsMeaningfulChange(oldEmail, shop.Email))
            {
                await _telegram.NotifyShopProfileAlertAsync(
                    "email",
                    sellerName,
                    shop.ProfileName,
                    shop.Name,
                    oldEmail,
                    shop.Email,
                    cancellationToken);
            }

            if (IsOn(values, ExtensionTikTokShopSyncSettingKeys.NotifyMobileChange)
                && IsMeaningfulChange(oldMobile, shop.NewPhoneNumber))
            {
                await _telegram.NotifyShopProfileAlertAsync(
                    "mobile",
                    sellerName,
                    shop.ProfileName,
                    shop.Name,
                    oldMobile,
                    shop.NewPhoneNumber,
                    cancellationToken);
            }

            if (IsOn(values, ExtensionTikTokShopSyncSettingKeys.NotifyBankChange)
                && IsMeaningfulBankChange(oldBank, shop.SettlementBankAccountNumber))
            {
                await _telegram.NotifyShopProfileAlertAsync(
                    "bank",
                    sellerName,
                    shop.ProfileName,
                    shop.Name,
                    oldBank,
                    shop.SettlementBankAccountNumber,
                    cancellationToken);
            }

            if (IsOn(values, ExtensionTikTokShopSyncSettingKeys.NotifyBankLast4Mismatch)
                && !string.IsNullOrWhiteSpace(shop.SettlementBankAccountNumber))
            {
                // Alert once on first fill or when bank string changes — not every identical auto-sync.
                var bankAppearedOrChanged =
                    NormalizeOptionalString(oldBank) is null
                    || IsMeaningfulBankChange(oldBank, shop.SettlementBankAccountNumber);
                if (bankAppearedOrChanged)
                {
                    var last4 = ExtractLast4Digits(shop.SettlementBankAccountNumber);
                    if (last4 is not null)
                    {
                        var systemLast4 = await _context.ReceivingBankAccounts
                            .AsNoTracking()
                            .Where(a => a.IsActive)
                            .Select(a => a.AccountNumber)
                            .ToListAsync(cancellationToken);

                        var matched = systemLast4
                            .Select(ExtractLast4Digits)
                            .Any(x => x is not null && x == last4);

                        if (!matched)
                        {
                            await _telegram.NotifyShopProfileAlertAsync(
                                "bank_last4",
                                sellerName,
                                shop.ProfileName,
                                shop.Name,
                                null,
                                shop.SettlementBankAccountNumber,
                                cancellationToken);
                        }
                    }
                }
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
                "Failed TikTok shop profile Telegram alerts for shop {ShopId}",
                shop.Id);
        }
    }

    /// <summary>
    /// Telegram alert when the ops <c>Shop.Status</c> changed. Call after a successful save so we never
    /// announce a change that was rolled back. Cheap no-op when the status is unchanged: the string
    /// compare short-circuits before any settings / user lookup.
    /// </summary>
    /// <param name="shop">Shop after the change.</param>
    /// <param name="previousStatus">Ops status before the change.</param>
    /// <param name="changeSource">Trigger: <c>tiktok_sync</c>, <c>admin</c> or <c>seller</c>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    private async Task TryNotifyShopStatusChangeAsync(
        Shop shop,
        string? previousStatus,
        string changeSource,
        CancellationToken cancellationToken)
    {
        var previous = NormalizeOptionalString(previousStatus);
        var current = NormalizeOptionalString(shop.Status);
        if (previous is null
            || current is null
            || string.Equals(previous, current, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            var enabled = await _settings.GetValueAsync(
                ExtensionTikTokShopSyncSettingKeys.NotifyStatusChange,
                cancellationToken);
            if (!string.Equals(enabled?.Trim(), "true", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var owner = await _userManager.FindByIdAsync(shop.OwnerId.ToString());
            var sellerName = string.IsNullOrWhiteSpace(owner?.FullName)
                ? (owner?.UserName ?? "Seller")
                : owner!.FullName.Trim();

            await _telegram.NotifyShopStatusChangeAsync(
                shop,
                previous,
                sellerName,
                changeSource,
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
                "Failed shop status change Telegram alert for shop {ShopId} ({Previous} -> {Current})",
                shop.Id,
                previous,
                current);
        }
    }

    private static bool IsMeaningfulChange(string? oldValue, string? newValue)
    {
        var oldNorm = NormalizeOptionalString(oldValue);
        var newNorm = NormalizeOptionalString(newValue);
        if (oldNorm is null || newNorm is null)
        {
            return false;
        }

        return !string.Equals(oldNorm, newNorm, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsMeaningfulBankChange(string? oldValue, string? newValue)
    {
        var oldNorm = NormalizeBank(oldValue);
        var newNorm = NormalizeBank(newValue);
        if (oldNorm is null || newNorm is null)
        {
            return false;
        }

        return !string.Equals(oldNorm, newNorm, StringComparison.OrdinalIgnoreCase);
    }

    private static string? NormalizeBank(string? value)
    {
        var trimmed = NormalizeOptionalString(value);
        if (trimmed is null)
        {
            return null;
        }

        return string.Concat(trimmed.Where(c => !char.IsWhiteSpace(c)));
    }

    private static string? ExtractLast4Digits(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var digits = string.Concat(value.Where(char.IsDigit));
        return digits.Length >= 4 ? digits[^4..] : null;
    }

    /// <inheritdoc />
    public async Task<List<AdminShopOptionDto>> GetOptionsForAdminAsync(CancellationToken cancellationToken = default)
    {
        return await (
            from shop in _context.Shops.AsNoTracking()
            join platform in _context.Platforms.AsNoTracking() on shop.PlatformId equals platform.Id
            orderby shop.SortOrder, shop.Name
            select new AdminShopOptionDto
            {
                Id = shop.Id,
                Name = shop.Name,
                ProfileName = shop.ProfileName,
                OwnerId = shop.OwnerId,
                BodySizeCode = shop.BodySizeCode,
                PlatformCode = platform.Code,
                Status = shop.Status
            })
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ShopMappingHealthDto> GetMappingHealthForAdminAsync(
        CancellationToken cancellationToken = default)
    {
        var unlinkedOrderCount = await _context.Orders
            .AsNoTracking()
            .CountAsync(o => o.ShopId == null, cancellationToken);

        var groups = await _context.Orders
            .AsNoTracking()
            .Where(o => o.ShopId == null)
            .GroupBy(o => new { o.FourSellerShopId, o.OwnerId })
            .Select(g => new
            {
                g.Key.FourSellerShopId,
                g.Key.OwnerId,
                OrderCount = g.Count(),
                FirstOrderAt = g.Min(o => o.OrderCreateTime ?? o.CreatedAt),
                LastOrderAt = g.Max(o => o.OrderCreateTime ?? o.CreatedAt)
            })
            .OrderByDescending(g => g.OrderCount)
            .Take(50)
            .ToListAsync(cancellationToken);

        var ownerIds = groups.Select(g => g.OwnerId).Distinct().ToList();
        var ownerNames = await _context.Users
            .AsNoTracking()
            .Where(u => ownerIds.Contains(u.Id))
            .Select(u => new
            {
                u.Id,
                Name = string.IsNullOrWhiteSpace(u.FullName) ? u.UserName : u.FullName
            })
            .ToDictionaryAsync(u => u.Id, u => u.Name, cancellationToken);

        var fsIds = groups
            .Where(g => g.FourSellerShopId is > 0)
            .Select(g => g.FourSellerShopId!.Value.ToString())
            .Distinct()
            .ToList();

        var knownFsIds = new HashSet<string>(StringComparer.Ordinal);
        if (fsIds.Count > 0)
        {
            var linked = await _context.Shops
                .AsNoTracking()
                .Where(s =>
                    (s.FourSellerShopId != null && fsIds.Contains(s.FourSellerShopId))
                    || (s.PreviousFourSellerShopId != null
                        && fsIds.Contains(s.PreviousFourSellerShopId)))
                .Select(s => new { s.FourSellerShopId, s.PreviousFourSellerShopId })
                .ToListAsync(cancellationToken);

            foreach (var row in linked)
            {
                if (!string.IsNullOrWhiteSpace(row.FourSellerShopId))
                {
                    knownFsIds.Add(row.FourSellerShopId);
                }

                if (!string.IsNullOrWhiteSpace(row.PreviousFourSellerShopId))
                {
                    knownFsIds.Add(row.PreviousFourSellerShopId);
                }
            }
        }

        return new ShopMappingHealthDto
        {
            UnlinkedOrderCount = unlinkedOrderCount,
            Groups = groups.Select(g => new ShopMappingHealthGroupDto
            {
                FourSellerShopId = g.FourSellerShopId,
                OrderCount = g.OrderCount,
                OwnerId = g.OwnerId,
                OwnerName = ownerNames.GetValueOrDefault(g.OwnerId),
                FirstOrderAt = g.FirstOrderAt,
                LastOrderAt = g.LastOrderAt,
                HasLocalShop = g.FourSellerShopId is > 0
                    && knownFsIds.Contains(g.FourSellerShopId.Value.ToString())
            }).ToList()
        };
    }

    /// <inheritdoc />
    public async Task<List<AdminShopListItemDto>> GetAllForAdminAsync(CancellationToken cancellationToken = default)
    {
        var rows = await (
            from shop in _context.Shops.AsNoTracking()
            join platform in _context.Platforms.AsNoTracking() on shop.PlatformId equals platform.Id
            join user in _context.Users.AsNoTracking() on shop.OwnerId equals user.Id
            orderby shop.CreatedAt, shop.Id
            select new
            {
                shop.Id,
                shop.Name,
                shop.PlatformId,
                PlatformCode = platform.Code,
                PlatformName = platform.Name,
                shop.ExternalShopId,
                shop.AvatarUrl,
                shop.FourSellerShopId,
                shop.FourSellerConnectedAt,
                shop.Region,
                shop.Status,
                shop.ShopUrl,
                shop.Proxy,
                shop.ProfileName,
                shop.Email,
                shop.TiktokPassword,
                shop.TwoFactorAuth,
                shop.LlcName,
                shop.DayLive,
                shop.Deli,
                shop.NewPhoneNumber,
                shop.PaymentName,
                shop.ReceivingBankAccountId,
                ReceivingAccountNumber = shop.ReceivingBankAccount!.AccountNumber,
                shop.SettlementBankAccountNumber,
                shop.ShopType,
                shop.SortOrder,
                shop.CreatedAt,
                shop.OwnerId,
                OwnerFullName = user.FullName,
                OwnerUserName = user.UserName ?? string.Empty,
                shop.ShopCode,
                shop.PublishLimitPublished,
                shop.PublishLimitTotal,
                shop.Timezone,
                shop.SellerType,
                shop.TikTokShopStatus,
                shop.CategoryId,
                shop.CategoryName,
                shop.CatalogCategories,
                shop.AgeMin,
                shop.AgeMax,
                shop.PriceMinUsd,
                shop.PriceMaxUsd,
                shop.PreferQuickShip,
                shop.PreferLocal,
                shop.PreferUsWarehouse,
                shop.PreferSheinClub,
                shop.MinRating,
                shop.MinSoldCount,
                shop.MaxVariantCount,
                shop.BodySizeCode,
                shop.StyleCodes,
                shop.AutoPublishEnabled,
                shop.AutoPublishIntervalSeconds,
                shop.AutoPublishMinDraftAgeMinutes
            }).ToListAsync(cancellationToken);

        var shops = rows.Select(r => new AdminShopListItemDto
        {
            Id = r.Id,
            Name = r.Name,
            PlatformId = r.PlatformId,
            PlatformCode = r.PlatformCode,
            PlatformName = r.PlatformName,
            ExternalShopId = r.ExternalShopId,
            AvatarUrl = r.AvatarUrl,
            FourSellerShopId = r.FourSellerShopId,
            FourSellerConnectionStatus = r.FourSellerShopId != null && r.FourSellerShopId != ""
                ? FourSellerConnectionStatuses.Connected
                : FourSellerConnectionStatuses.NotConnected,
            FourSellerConnectedAt = r.FourSellerConnectedAt,
            Region = r.Region,
            Status = r.Status,
            ShopUrl = r.ShopUrl,
            Proxy = r.Proxy,
            ProfileName = r.ProfileName,
            Email = r.Email,
            TiktokPassword = r.TiktokPassword,
            TwoFactorAuth = r.TwoFactorAuth,
            LlcName = r.LlcName,
            DayLive = r.DayLive,
            Deli = r.Deli,
            NewPhoneNumber = r.NewPhoneNumber,
            PaymentName = r.PaymentName,
            ReceivingBankAccountId = r.ReceivingBankAccountId,
            SettlementBankLast4 = ReceivingBankCardMask.ExtractLast4Digits(r.SettlementBankAccountNumber),
            ReceivingBankLast4 = ReceivingBankCardMask.ExtractLast4Digits(r.ReceivingAccountNumber),
            BankMatchStatus = ReceivingBankCardMask.ResolveShopBankMatchStatus(
                r.ReceivingBankAccountId,
                r.ReceivingAccountNumber,
                r.SettlementBankAccountNumber),
            ShopType = r.ShopType,
            Categories = ReadCatalogCategories(r.CatalogCategories, r.CategoryId, r.CategoryName),
            CategoryId = r.CategoryId,
            CategoryName = r.CategoryName,
            AgeMin = r.AgeMin,
            AgeMax = r.AgeMax,
            PriceMinUsd = r.PriceMinUsd,
            PriceMaxUsd = r.PriceMaxUsd,
            PreferQuickShip = r.PreferQuickShip,
            PreferLocal = r.PreferLocal,
            PreferUsWarehouse = r.PreferUsWarehouse,
            PreferSheinClub = r.PreferSheinClub,
            MinRating = r.MinRating,
            MinSoldCount = r.MinSoldCount,
            MaxVariantCount = r.MaxVariantCount,
            BodySizeCode = r.BodySizeCode,
            StyleCodes = ReadStyleCodes(r.StyleCodes),
            SortOrder = r.SortOrder,
            CreatedAt = r.CreatedAt,
            OwnerId = r.OwnerId,
            OwnerFullName = r.OwnerFullName,
            OwnerUserName = r.OwnerUserName,
            ShopCode = r.ShopCode,
            ActiveListingCount = r.PublishLimitPublished ?? 0,
            PublishLimitPublished = r.PublishLimitPublished,
            PublishLimitTotal = r.PublishLimitTotal,
            Timezone = r.Timezone,
            SellerType = r.SellerType,
            TikTokShopStatus = r.TikTokShopStatus,
            AutoPublishEnabled = r.AutoPublishEnabled,
            AutoPublishIntervalSeconds = r.AutoPublishIntervalSeconds,
            AutoPublishMinDraftAgeMinutes = r.AutoPublishMinDraftAgeMinutes
        }).ToList();

        await EnrichAutoPublishSchedulerStatusAsync(shops, cancellationToken);
        await EnrichTodayOnlineSecondsAsync(shops, cancellationToken);
        return shops;
    }

    /// <inheritdoc />
    public async Task<AdminShopDto> GetByIdForAdminAsync(Guid shopId, CancellationToken cancellationToken = default)
    {
        var shop = await FindShopWithOwnerAsync(shopId, cancellationToken);
        var dto = MapToAdminDto(shop.shop, shop.owner);
        dto.AcquisitionCost = await _context.CompanyLedgerEntries.AsNoTracking()
            .Where(e => e.ShopId == shopId && e.Source == CompanyLedgerEntry.SourceShopAcquisition)
            .Select(e => (decimal?)e.Amount)
            .FirstOrDefaultAsync(cancellationToken);
        return dto;
    }

    /// <inheritdoc />
    public async Task<AdminShopDto> CreateForOwnerAsync(CreateAdminShopDto dto, CancellationToken cancellationToken = default)
    {
        var owner = await ValidateSellerOwnerAsync(dto.OwnerId, cancellationToken);
        await ValidatePlatformAsync(dto.PlatformId, cancellationToken);
        await EnsureUniqueExternalShopIdAsync(owner.Id, dto.ExternalShopId, null, cancellationToken);

        var nextSortOrder = await _context.Shops
            .Select(s => (int?)s.SortOrder)
            .MaxAsync(cancellationToken) ?? -1;

        var shop = new Shop
        {
            OwnerId = owner.Id,
            Name = dto.Name,
            PlatformId = dto.PlatformId,
            ExternalShopId = NormalizeOptionalString(dto.ExternalShopId),
            AvatarUrl = NormalizeOptionalString(dto.AvatarUrl),
            Region = ResolveRegion(dto.Region),
            Currency = ResolveCurrency(dto.Region),
            Status = dto.Status,
            ShopUrl = NormalizeOptionalString(dto.ShopUrl)
                ?? BuildTikTokShopStoreUrl(dto.Name, dto.ExternalShopId),
            Metadata = dto.Metadata,
            SortOrder = nextSortOrder + 1
        };
        var fourSellerIdChanged = ApplyFourSellerConnection(shop, dto.FourSellerShopId);
        ApplyExtendedFields(shop, dto);
        await ApplyReceivingBankAccountAsync(shop, dto.ReceivingBankAccountId, cancellationToken);

        _context.Shops.Add(shop);
        await _operationalPeriods.OnShopCreatedAsync(shop, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);

        // Orphaned orders already synced under this 4Seller id (shop created after the fact).
        await TryRelinkOrdersAfterFourSellerIdChangeAsync(shop, fourSellerIdChanged, cancellationToken);

        return await GetByIdForAdminAsync(shop.Id, cancellationToken);
    }

    /// <inheritdoc />
    public async Task TransferOwnerAsync(Guid shopId, Guid newOwnerId, CancellationToken cancellationToken = default)
    {
        var shop = await _context.Shops.FirstOrDefaultAsync(s => s.Id == shopId, cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy shop.");
        var owner = await ValidateSellerOwnerAsync(newOwnerId, cancellationToken);
        var previousOwnerId = shop.OwnerId;
        if (previousOwnerId == owner.Id)
        {
            return;
        }

        var previousStatus = shop.Status;
        shop.OwnerId = owner.Id;
        shop.UpdatedAt = DateTime.UtcNow;

        // Same invariant as UpdateForAdminAsync: cascade + period + shop row commit together or not at all.
        await using var transaction = await BeginTransactionAsync(cancellationToken);
        await _ownershipTransfer.TransferAsync(shop.Id, previousOwnerId, owner.Id, cancellationToken);
        await _operationalPeriods.OnShopUpdatedAsync(shop, previousStatus, previousOwnerId, cancellationToken);
        await SaveChangesAllowingOpenPeriodRaceAsync(shop.Id, cancellationToken, transaction);
        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }
    }

    /// <inheritdoc />
    public async Task<AdminShopDto> UpdateForAdminAsync(Guid shopId, UpdateAdminShopDto dto, CancellationToken cancellationToken = default)
    {
        var shop = await _context.Shops
            .Include(s => s.Platform)
            .FirstOrDefaultAsync(s => s.Id == shopId, cancellationToken);
        if (shop is null)
        {
            throw new NotFoundException("Không tìm thấy shop.");
        }

        var ownerId = dto.OwnerId ?? shop.OwnerId;
        var owner = await ValidateSellerOwnerAsync(ownerId, cancellationToken);
        await ValidatePlatformAsync(dto.PlatformId, cancellationToken);
        await EnsureUniqueExternalShopIdAsync(ownerId, dto.ExternalShopId, shopId, cancellationToken);

        var previousStatus = shop.Status;
        var previousOwnerId = shop.OwnerId;
        var ownerChanged = previousOwnerId != owner.Id;

        shop.OwnerId = owner.Id;
        shop.Name = dto.Name;
        shop.PlatformId = dto.PlatformId;
        shop.ExternalShopId = NormalizeOptionalString(dto.ExternalShopId);
        shop.AvatarUrl = NormalizeOptionalString(dto.AvatarUrl);
        var fourSellerIdChanged = ApplyFourSellerConnection(shop, dto.FourSellerShopId);
        if (!string.IsNullOrWhiteSpace(dto.Region))
        {
            AssignShopRegion(shop, dto.Region);
        }
        shop.Status = dto.Status;
        shop.ShopUrl = NormalizeOptionalString(dto.ShopUrl)
            ?? BuildTikTokShopStoreUrl(dto.Name, dto.ExternalShopId)
            ?? shop.ShopUrl;
        shop.Metadata = dto.Metadata;
        ApplyExtendedFields(shop, dto);
        await ApplyReceivingBankAccountAsync(shop, dto.ReceivingBankAccountId, cancellationToken);
        shop.UpdatedAt = DateTime.UtcNow;

        // The cascade below flushes mid-way (rematch, listing collapse) on this same DbContext, and
        // `shop.OwnerId` is already staged, so a failure after one of those flushes would leave the
        // shop handed over with no `OwnerChanged` period behind it. One transaction or nothing.
        await using var transaction = ownerChanged
            ? await BeginTransactionAsync(cancellationToken)
            : null;

        // Cascade cookie-owner denorm before SaveChanges so Mark Ship / seller lists follow the new seller.
        if (ownerChanged)
        {
            await _ownershipTransfer.TransferAsync(
                shop.Id,
                previousOwnerId,
                owner.Id,
                cancellationToken);
        }

        await _operationalPeriods.OnShopUpdatedAsync(
            shop,
            previousStatus,
            previousOwnerId,
            cancellationToken);
        MaybeCancelAutoPublishOnShopDeactivate(shop, previousStatus);
        await SaveChangesAllowingOpenPeriodRaceAsync(shop.Id, cancellationToken, transaction);

        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        await TryNotifyShopStatusChangeAsync(shop, previousStatus, "admin", cancellationToken);
        await TryRelinkOrdersAfterFourSellerIdChangeAsync(shop, fourSellerIdChanged, cancellationToken);
        return await GetByIdForAdminAsync(shop.Id, cancellationToken);
    }

    /// <inheritdoc />
    public Task<ShopOwnershipTransferResultDto> ReassignOwnedDataForAdminAsync(
        Guid shopId,
        CancellationToken cancellationToken = default)
        => _ownershipTransfer.ReassignMismatchedOwnedDataAsync(shopId, cancellationToken);

    /// <inheritdoc />
    public Task<ShopOwnershipTransferResultDto> ReassignAllOwnedDataForAdminAsync(
        CancellationToken cancellationToken = default)
        => _ownershipTransfer.ReassignAllMismatchedOwnedDataAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<ShopCatalogProfileDto> UpdateCatalogProfileForAdminAsync(
        Guid shopId,
        UpdateShopCatalogProfileDto dto,
        CancellationToken cancellationToken = default)
    {
        var shop = await _context.Shops
            .FirstOrDefaultAsync(s => s.Id == shopId, cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy shop.");

        await ApplyCatalogProfileAsync(shop, dto, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
        return MapToCatalogProfileDto(shop);
    }

    /// <inheritdoc />
    public async Task<ShopAutoPublishDto> UpdateAutoPublishForAdminAsync(
        Guid shopId,
        UpdateShopAutoPublishDto dto,
        CancellationToken cancellationToken = default)
    {
        var shop = await _context.Shops
            .FirstOrDefaultAsync(s => s.Id == shopId, cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy shop.");

        await ApplyAutoPublishAsync(shop, dto, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
        return MapToAutoPublishDto(shop);
    }

    /// <inheritdoc />
    public async Task DeleteForAdminAsync(Guid shopId, CancellationToken cancellationToken = default)
    {
        var shop = await _context.Shops.FirstOrDefaultAsync(s => s.Id == shopId, cancellationToken);
        if (shop is null)
        {
            throw new NotFoundException("Không tìm thấy shop.");
        }

        await _operationalPeriods.OnShopDeletingAsync(shop, cancellationToken);
        _context.Shops.Remove(shop);
        await _context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task ReorderAsync(ReorderShopsDto dto, CancellationToken cancellationToken = default)
    {
        if (dto.OrderedIds.Count == 0)
        {
            throw new BusinessException("Danh sách sắp xếp không được để trống.");
        }

        if (dto.OrderedIds.Distinct().Count() != dto.OrderedIds.Count)
        {
            throw new BusinessException("Danh sách sắp xếp không được chứa ID trùng.");
        }

        var shops = await _context.Shops.ToListAsync(cancellationToken);
        if (shops.Count != dto.OrderedIds.Count || shops.Any(s => !dto.OrderedIds.Contains(s.Id)))
        {
            throw new BusinessException("Danh sách sắp xếp không khớp với shop hiện có.");
        }

        var now = DateTime.UtcNow;
        for (var i = 0; i < dto.OrderedIds.Count; i++)
        {
            var shop = shops.First(s => s.Id == dto.OrderedIds[i]);
            shop.SortOrder = i;
            shop.UpdatedAt = now;
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<AdminShopDto> SyncFourSellerConnectionAsync(
        Guid shopId,
        FourSellerShopRecordDto matchedShop,
        CancellationToken cancellationToken = default)
    {
        var shop = await _context.Shops
            .Include(s => s.Platform)
            .FirstOrDefaultAsync(s => s.Id == shopId, cancellationToken);
        if (shop is null)
        {
            throw new NotFoundException("Không tìm thấy shop.");
        }

        var owner = await _context.Users.FirstOrDefaultAsync(u => u.Id == shop.OwnerId, cancellationToken);
        if (owner is null)
        {
            throw new NotFoundException("Không tìm thấy seller của shop.");
        }

        var fourSellerIdChanged = ApplyFourSellerRecordFields(shop, matchedShop);
        shop.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);
        await TryRelinkOrdersAfterFourSellerIdChangeAsync(shop, fourSellerIdChanged, cancellationToken);
        return MapToAdminDto(shop, owner);
    }

    /// <summary>
    /// After a manual 4Seller id change, restore <c>Order.ShopId</c> for rows already synced under
    /// that id (or under the id the shop carried before). Without this the orders keep showing an
    /// empty shop / profile name until someone runs a full 4Seller shop sync.
    /// A relink failure must not fail the shop write, so it is logged and swallowed.
    /// </summary>
    private async Task TryRelinkOrdersAfterFourSellerIdChangeAsync(
        Shop shop,
        bool fourSellerIdChanged,
        CancellationToken cancellationToken)
    {
        if (!fourSellerIdChanged || string.IsNullOrWhiteSpace(shop.FourSellerShopId))
        {
            return;
        }

        try
        {
            var relinked = await _orderSyncService.RematchOrderShopLinksAsync(
                shopId: shop.Id,
                cancellationToken: cancellationToken);
            if (relinked > 0)
            {
                _logger.LogInformation(
                    "Relinked {Count} orders to shop {ShopId} after FourSellerShopId changed to {FourSellerShopId}",
                    relinked,
                    shop.Id,
                    shop.FourSellerShopId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to relink orders for shop {ShopId} after FourSellerShopId changed to {FourSellerShopId}",
                shop.Id,
                shop.FourSellerShopId);
        }
    }

    /// <inheritdoc />
    public async Task<FourSellerSyncResponse> UpsertFromFourSellerListAsync(
        Guid ownerIdForCreates,
        IReadOnlyList<FourSellerShopRecordDto> fourSellerShops,
        bool scopeToOwnerOnly,
        CancellationToken cancellationToken = default)
    {
        // One load for both same-owner match and cross-owner ExternalShopId relink so EF tracks
        // a single instance per shop (loading twice would duplicate tracked entities).
        var allShopsForRelink = await _context.Shops
            .Include(s => s.Platform)
            .ToListAsync(cancellationToken);
        var localShops = scopeToOwnerOnly
            ? allShopsForRelink.Where(s => s.OwnerId == ownerIdForCreates).ToList()
            : allShopsForRelink;

        var response = new FourSellerSyncResponse
        {
            TotalLocalShops = localShops.Count,
            FourSellerShopCount = fourSellerShops.Count
        };

        var cookieOwnerName = await _context.Users
            .AsNoTracking()
            .Where(u => u.Id == ownerIdForCreates)
            .Select(u => string.IsNullOrWhiteSpace(u.FullName) ? u.UserName : u.FullName)
            .FirstOrDefaultAsync(cancellationToken);

        var matchedLocalIds = new HashSet<Guid>();

        foreach (var record in fourSellerShops)
        {
            if (!string.Equals(record.StatusDesc, "Connected", StringComparison.OrdinalIgnoreCase))
            {
                response.SkippedCount++;
                response.UnmatchedShops.Add(ToUnmatchedShop(
                    record, cookieOwnerName, FourSellerUnmatchedReasons.NotConnected));
                continue;
            }

            var localShop = FindLocalMatch(record, localShops, matchedLocalIds);
            if (localShop is not null)
            {
                ApplyFourSellerRecordFields(localShop, record);
                localShop.UpdatedAt = DateTime.UtcNow;
                matchedLocalIds.Add(localShop.Id);
                response.UpdatedCount++;
                continue;
            }

            // Same TikTok shop reconnected under another 4Seller account → refresh FourSellerShopId
            // only (keep OwnerId / Name / Region). Never create a new local shop.
            var relink = TryRelinkByExternalShopId(
                record,
                allShopsForRelink,
                matchedLocalIds,
                ownerIdForCreates,
                cookieOwnerName,
                response);
            if (relink)
            {
                continue;
            }

            response.SkippedCount++;
        }

        if (response.UpdatedCount + response.RelinkedCount > 0)
        {
            await _context.SaveChangesAsync(cancellationToken);
        }

        // Relink local orders so OrderCard can show shopName / shopProfileName.
        // After cross-owner FourSellerShopId refresh, rematch must see every shop — not only
        // the cookie owner's rows — so pass null when any RelinkedCount happened.
        response.OrdersRelinkedCount = await _orderSyncService.RematchOrderShopLinksAsync(
            scopeToOwnerOnly && response.RelinkedCount == 0 ? ownerIdForCreates : null,
            cancellationToken: cancellationToken);

        response.SyncedCount = response.UpdatedCount + response.RelinkedCount;
        response.UnmatchedCount = response.SkippedCount;
        return response;
    }

    /// <summary>
    /// When the cookie owner's scoped shops miss a Connected 4Seller row, try matching by TikTok
    /// <c>ExternalShopId</c> (= <c>sellingPartnerId</c>) across all local shops. On a unique hit,
    /// only stamp the new 4Seller internal id (PreviousFourSellerShopId keeps the old one).
    /// </summary>
    private bool TryRelinkByExternalShopId(
        FourSellerShopRecordDto record,
        IReadOnlyList<Shop> allShops,
        HashSet<Guid> matchedLocalIds,
        Guid cookieOwnerId,
        string? cookieOwnerName,
        FourSellerSyncResponse response)
    {
        var sellingPartnerId = NormalizeOptionalString(record.SellingPartnerId);
        if (sellingPartnerId is null)
        {
            response.UnmatchedShops.Add(ToUnmatchedShop(
                record, cookieOwnerName, FourSellerUnmatchedReasons.NoLocalShop));
            return false;
        }

        var newFsId = record.Id.ToString();
        var takenByOtherShop = allShops.Any(s =>
            string.Equals(
                NormalizeOptionalString(s.FourSellerShopId),
                newFsId,
                StringComparison.OrdinalIgnoreCase)
            && !string.Equals(
                NormalizeOptionalString(s.ExternalShopId),
                sellingPartnerId,
                StringComparison.OrdinalIgnoreCase));
        if (takenByOtherShop)
        {
            // Another shop already owns this 4Seller id with a different TikTok id.
            response.UnmatchedShops.Add(ToUnmatchedShop(
                record, cookieOwnerName, FourSellerUnmatchedReasons.FourSellerIdTaken));
            return false;
        }

        var candidates = allShops
            .Where(s =>
                !matchedLocalIds.Contains(s.Id)
                && string.Equals(
                    NormalizeOptionalString(s.ExternalShopId),
                    sellingPartnerId,
                    StringComparison.OrdinalIgnoreCase)
                && PlatformCodeMatches(record.Platform, s.Platform?.Code))
            .ToList();

        if (candidates.Count == 0)
        {
            response.UnmatchedShops.Add(ToUnmatchedShop(
                record, cookieOwnerName, FourSellerUnmatchedReasons.NoLocalShop));
            return false;
        }

        if (candidates.Count > 1)
        {
            response.UnmatchedShops.Add(ToUnmatchedShop(
                record, cookieOwnerName, FourSellerUnmatchedReasons.AmbiguousExternalId));
            return false;
        }

        var shop = candidates[0];
        matchedLocalIds.Add(shop.Id);

        // The cookie owner sees a shop belonging to someone else on every sync. Without this the
        // shop would be rewritten (and a global order rematch triggered) each time for nothing.
        if (string.Equals(
                NormalizeOptionalString(shop.FourSellerShopId),
                newFsId,
                StringComparison.OrdinalIgnoreCase))
        {
            // Legacy rows may have FourSellerShopId without ConnectedAt — fill without a rewrite.
            if (shop.FourSellerConnectedAt is null)
            {
                shop.FourSellerConnectedAt =
                    ParseFourSellerCreateTime(record.CreateTime) ?? DateTime.UtcNow;
                shop.UpdatedAt = DateTime.UtcNow;
            }

            return true;
        }

        ApplyFourSellerConnection(shop, newFsId);
        shop.FourSellerConnectedAt = ParseFourSellerCreateTime(record.CreateTime) ?? DateTime.UtcNow;
        shop.UpdatedAt = DateTime.UtcNow;
        response.RelinkedCount++;

        if (shop.OwnerId != cookieOwnerId)
        {
            _logger.LogWarning(
                "Shop sync relinked FourSellerShopId {FourSellerShopId} onto local shop {ShopId} (owner {LocalOwnerId}) from cookie of {CookieOwnerId} ({CookieOwnerName}) via ExternalShopId {ExternalShopId} — OwnerId unchanged",
                newFsId,
                shop.Id,
                shop.OwnerId,
                cookieOwnerId,
                cookieOwnerName,
                sellingPartnerId);
        }

        return true;
    }

    private static FourSellerUnmatchedShopDto ToUnmatchedShop(
        FourSellerShopRecordDto record,
        string? cookieOwnerName,
        string reason) =>
        new()
        {
            FourSellerShopId = record.Id,
            ShopName = string.IsNullOrWhiteSpace(record.ShopName) ? null : record.ShopName.Trim(),
            PlatformShopName = string.IsNullOrWhiteSpace(record.PlatformShopName)
                ? null
                : record.PlatformShopName.Trim(),
            SellingPartnerId = string.IsNullOrWhiteSpace(record.SellingPartnerId)
                ? null
                : record.SellingPartnerId.Trim(),
            MarketplaceId = string.IsNullOrWhiteSpace(record.MarketplaceId)
                ? null
                : record.MarketplaceId.Trim(),
            CookieOwnerName = cookieOwnerName,
            Reason = reason
        };

    /// <inheritdoc />
    public async Task<FourSellerSyncResponse> SyncAllFromLoggedInCookiesAsync(
        CancellationToken cancellationToken = default)
    {
        var acquired = await SyncGate.WaitAsync(0, cancellationToken);
        if (!acquired)
        {
            _logger.LogInformation("Shop sync skipped — another multi-cookie sync is already running");
            return new FourSellerSyncResponse
            {
                FailedAccounts =
                [
                    new FourSellerSyncAccountFailureDto
                    {
                        OwnerUserId = Guid.Empty,
                        OwnerName = null,
                        Message = "Đồng bộ shop đang chạy. Thử lại sau vài giây."
                    }
                ]
            };
        }

        try
        {
            return await SyncAllFromLoggedInCookiesCoreAsync(cancellationToken);
        }
        finally
        {
            SyncGate.Release();
        }
    }

    private async Task<FourSellerSyncResponse> SyncAllFromLoggedInCookiesCoreAsync(
        CancellationToken cancellationToken)
    {
        var owners = await _context.Users
            .AsNoTracking()
            .Where(u =>
                u.FourSellerCookie != null &&
                u.FourSellerCookie.Trim() != string.Empty &&
                u.FourSellerCookieLoginStatus == FourSellerCookieLoginStatuses.LoggedIn)
            .Select(u => new { u.Id, u.UserName, u.FullName })
            .ToListAsync(cancellationToken);

        var aggregate = new FourSellerSyncResponse
        {
            TotalLocalShops = await _context.Shops.CountAsync(cancellationToken)
        };

        if (owners.Count == 0)
        {
            return aggregate;
        }

        // Fan-out: each cookie account gets its own DI scope (DbContext + FourSellerApiService + sticky proxy).
        var tasks = owners
            .Select(owner => SyncOwnerInScopeAsync(
                owner.Id,
                string.IsNullOrWhiteSpace(owner.FullName) ? owner.UserName : owner.FullName,
                cancellationToken))
            .ToArray();

        var partials = await Task.WhenAll(tasks);
        foreach (var partial in partials)
        {
            if (partial.Failure is not null)
            {
                aggregate.FailedAccounts.Add(partial.Failure);
                continue;
            }

            var response = partial.Response!;
            aggregate.SyncedAccounts++;
            aggregate.FourSellerShopCount += response.FourSellerShopCount;
            aggregate.UpdatedCount += response.UpdatedCount;
            aggregate.RelinkedCount += response.RelinkedCount;
            aggregate.SkippedCount += response.SkippedCount;
            aggregate.OrdersRelinkedCount += response.OrdersRelinkedCount;
            if (response.UnmatchedShops.Count > 0)
            {
                aggregate.UnmatchedShops.AddRange(response.UnmatchedShops);
            }
        }

        aggregate.SyncedCount = aggregate.UpdatedCount + aggregate.RelinkedCount;
        aggregate.UnmatchedCount = aggregate.SkippedCount;
        return aggregate;
    }

    /// <inheritdoc />
    public async Task<FourSellerSyncResponse> SyncOwnerFromCookieAsync(
        Guid ownerUserId,
        CancellationToken cancellationToken = default)
    {
        var fourSellerShops = await _fourSellerApiService.GetTidyShopListAsync(
            ownerUserId,
            cancellationToken);
        return await UpsertFromFourSellerListAsync(
            ownerUserId,
            fourSellerShops,
            scopeToOwnerOnly: true,
            cancellationToken);
    }

    private async Task<OwnerShopSyncPartial> SyncOwnerInScopeAsync(
        Guid ownerId,
        string? ownerName,
        CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var worker = scope.ServiceProvider.GetRequiredService<IShopService>();
            var response = await worker.SyncOwnerFromCookieAsync(ownerId, cancellationToken);
            return new OwnerShopSyncPartial(response, null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Shop sync failed for cookie account {OwnerId}", ownerId);
            return new OwnerShopSyncPartial(
                null,
                new FourSellerSyncAccountFailureDto
                {
                    OwnerUserId = ownerId,
                    OwnerName = ownerName,
                    Message = ex is BusinessException be ? be.Message : Truncate(ex.Message, 300)
                });
        }
    }

    private sealed record OwnerShopSyncPartial(
        FourSellerSyncResponse? Response,
        FourSellerSyncAccountFailureDto? Failure);

    private static string Truncate(string value, int maxLen) =>
        string.IsNullOrEmpty(value) || value.Length <= maxLen
            ? value
            : value[..maxLen] + "…";

    private static Shop? FindLocalMatch(
        FourSellerShopRecordDto record,
        IReadOnlyList<Shop> localShops,
        HashSet<Guid> alreadyMatched)
    {
        var candidates = localShops.Where(s => !alreadyMatched.Contains(s.Id)).ToList();
        var sellingPartnerId = NormalizeOptionalString(record.SellingPartnerId);
        var recordId = record.Id.ToString();
        var region = string.IsNullOrWhiteSpace(record.MarketplaceId)
            ? null
            : record.MarketplaceId.Trim();

        var byFourSellerNumericId = candidates.FirstOrDefault(s =>
            string.Equals(s.FourSellerShopId, recordId, StringComparison.OrdinalIgnoreCase));
        if (byFourSellerNumericId is not null)
        {
            return byFourSellerNumericId;
        }

        if (sellingPartnerId is not null)
        {
            var byFourSellerPartner = candidates.FirstOrDefault(s =>
                string.Equals(s.FourSellerShopId, sellingPartnerId, StringComparison.OrdinalIgnoreCase));
            if (byFourSellerPartner is not null)
            {
                return byFourSellerPartner;
            }

            var byExternal = candidates.FirstOrDefault(s =>
                string.Equals(s.ExternalShopId, sellingPartnerId, StringComparison.OrdinalIgnoreCase));
            if (byExternal is not null)
            {
                return byExternal;
            }
        }

        var platformName = NormalizeName(record.PlatformShopName);
        var shopName = NormalizeName(record.ShopName);

        return candidates.FirstOrDefault(s =>
        {
            if (!PlatformCodeMatches(record.Platform, s.Platform?.Code))
            {
                return false;
            }

            if (region is not null &&
                !string.Equals(s.Region, region, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var localName = NormalizeName(s.Name);
            if (string.IsNullOrEmpty(localName))
            {
                return false;
            }

            return localName == platformName ||
                   localName == shopName ||
                   (!string.IsNullOrEmpty(shopName) &&
                    shopName.StartsWith(localName, StringComparison.OrdinalIgnoreCase));
        });
    }

    /// <summary>
    /// Copies the 4Seller shop record onto a local shop.
    /// Returns true when <c>FourSellerShopId</c> changed (orders need a relink).
    /// </summary>
    private static bool ApplyFourSellerRecordFields(Shop shop, FourSellerShopRecordDto record)
    {
        var displayName = !string.IsNullOrWhiteSpace(record.PlatformShopName)
            ? record.PlatformShopName.Trim()
            : record.ShopName.Trim();

        if (!string.IsNullOrWhiteSpace(displayName))
        {
            shop.Name = displayName;
        }

        // A blank sellingPartnerId means 4Seller did not return the TikTok id on this record — keep
        // the stored one, it is the only key left for ExternalShopId matching and cross-owner relink.
        var sellingPartnerId = NormalizeOptionalString(record.SellingPartnerId);
        if (sellingPartnerId is not null)
        {
            shop.ExternalShopId = sellingPartnerId;
        }
        // Always store 4Seller internal Id — matches order.shopId / listings / category APIs.
        var fourSellerIdChanged = ApplyFourSellerConnection(shop, record.Id.ToString());
        shop.FourSellerConnectedAt = ParseFourSellerCreateTime(record.CreateTime) ?? DateTime.UtcNow;

        if (!string.IsNullOrWhiteSpace(record.MarketplaceId))
        {
            AssignShopRegion(shop, record.MarketplaceId);
        }

        if (!string.IsNullOrWhiteSpace(record.Currency))
        {
            shop.Currency = record.Currency.Trim().ToUpperInvariant();
        }
        else if (!string.IsNullOrWhiteSpace(record.MarketplaceId))
        {
            shop.Currency = ResolveCurrency(record.MarketplaceId);
        }

        return fourSellerIdChanged;
    }

    private static bool PlatformCodeMatches(string fourSellerPlatform, string? localPlatformCode)
    {
        if (string.IsNullOrWhiteSpace(localPlatformCode))
        {
            return false;
        }

        return string.Equals(
            fourSellerPlatform.Replace(" ", string.Empty),
            localPlatformCode.Replace(" ", string.Empty),
            StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeName(string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToLowerInvariant();

    private async Task<Shop> FindOwnedShopAsync(Guid ownerId, Guid shopId, CancellationToken cancellationToken)
    {
        var shop = await _context.Shops
            .Include(s => s.Platform)
            .Include(s => s.ReceivingBankAccount!)
                .ThenInclude(a => a.Source)
            .Include(s => s.ReceivingBankAccount!)
                .ThenInclude(a => a.SourceAccount)
            .FirstOrDefaultAsync(s => s.Id == shopId && s.OwnerId == ownerId, cancellationToken);

        if (shop is null)
        {
            throw new NotFoundException("Không tìm thấy shop.");
        }

        return shop;
    }

    private async Task<(Shop shop, ApplicationUser owner)> FindShopWithOwnerAsync(
        Guid shopId,
        CancellationToken cancellationToken)
    {
        var result = await (
            from shop in _context.Shops
                .Include(s => s.Platform)
                .Include(s => s.ReceivingBankAccount!)
                    .ThenInclude(a => a.Source)
                .Include(s => s.ReceivingBankAccount!)
                    .ThenInclude(a => a.SourceAccount)
            join user in _context.Users on shop.OwnerId equals user.Id
            where shop.Id == shopId
            select new { shop, user }).FirstOrDefaultAsync(cancellationToken);

        if (result is null)
        {
            throw new NotFoundException("Không tìm thấy shop.");
        }

        return (result.shop, result.user);
    }

    private async Task<ApplicationUser> ValidateSellerOwnerAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        var owner = await _context.Users.FirstOrDefaultAsync(u => u.Id == ownerId, cancellationToken);
        if (owner is null || !owner.IsActive)
        {
            throw new BusinessException("Seller không hợp lệ hoặc đã bị vô hiệu hóa.");
        }

        var roles = await _userManager.GetRolesAsync(owner);
        if (!roles.Contains(RoleNames.Seller))
        {
            throw new BusinessException("Người dùng được chọn phải có vai trò Seller.");
        }

        return owner;
    }

    private async Task ValidatePlatformAsync(Guid platformId, CancellationToken cancellationToken)
    {
        var exists = await _context.Platforms.AnyAsync(
            p => p.Id == platformId && p.IsActive,
            cancellationToken);

        if (!exists)
        {
            throw new BusinessException("Nền tảng không hợp lệ hoặc đã bị vô hiệu hóa.");
        }
    }

    private async Task EnsureUniqueExternalShopIdAsync(
        Guid ownerId,
        string? externalShopId,
        Guid? excludeShopId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(externalShopId))
        {
            return;
        }

        var normalized = externalShopId.Trim();

        // Cross-owner too: one TikTok shop must map to one row. A second row for the new seller
        // would strand the first seller's listings, orders and supplier links on the old row.
        var duplicate = await (
                from s in _context.Shops.AsNoTracking()
                join u in _context.Users.AsNoTracking() on s.OwnerId equals u.Id into owners
                from u in owners.DefaultIfEmpty()
                where s.ExternalShopId == normalized
                      && (!excludeShopId.HasValue || s.Id != excludeShopId.Value)
                select new
                {
                    s.OwnerId,
                    s.Name,
                    OwnerLabel = u != null ? (u.FullName ?? u.UserName) : null
                })
            .FirstOrDefaultAsync(cancellationToken);

        if (duplicate is null)
        {
            return;
        }

        if (duplicate.OwnerId == ownerId)
        {
            throw new BusinessException("Shop với External Shop ID này đã tồn tại cho seller.");
        }

        throw new BusinessException(
            $"External Shop ID «{normalized}» đã thuộc shop «{duplicate.Name}» của seller "
            + $"«{duplicate.OwnerLabel ?? "(không rõ)"}». Chuyển shop đó sang seller mới thay vì tạo shop thứ hai, "
            + "để listing / đơn / dữ liệu NCC đi theo.");
    }

    private static string? NormalizeOptionalString(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>Passport/pay TTS mask contact with <c>*</c> (same rule as extension).</summary>
    private static bool IsMaskedContact(string? value) =>
        !string.IsNullOrEmpty(value) && value.Contains('*');

    /// <summary>
    /// Extension contact: keep existing real value; fill empty or upgrade masked → unmasked.
    /// Never persist masked incoming values.
    /// </summary>
    private static string? ResolveSyncedContact(string? existing, string? incoming)
    {
        var next = NormalizeOptionalString(incoming);
        if (next is null || IsMaskedContact(next))
        {
            return existing;
        }

        var current = NormalizeOptionalString(existing);
        if (current is null || IsMaskedContact(current))
        {
            return next;
        }

        return existing;
    }

    private static string ResolveRegion(string? region) =>
        string.IsNullOrWhiteSpace(region) ? "US" : region.Trim().ToUpperInvariant();

    private static string ResolveCurrency(string? region) => ResolveRegion(region) switch
    {
        "UK" or "GB" => "GBP",
        "ID" => "IDR",
        "MY" => "MYR",
        "PH" => "PHP",
        "SG" => "SGD",
        "TH" => "THB",
        "VN" => "VND",
        _ => "USD"
    };

    private static ShopDto MapToDto(Shop shop) => new()
    {
        Id = shop.Id,
        Name = shop.Name,
        PlatformId = shop.PlatformId,
        PlatformCode = shop.Platform?.Code ?? string.Empty,
        PlatformName = shop.Platform?.Name ?? string.Empty,
        ExternalShopId = shop.ExternalShopId,
        AvatarUrl = shop.AvatarUrl,
        FourSellerShopId = shop.FourSellerShopId,
        FourSellerConnectionStatus = ResolveFourSellerConnectionStatus(shop.FourSellerShopId),
        FourSellerConnectedAt = shop.FourSellerConnectedAt,
        Region = shop.Region,
        Currency = shop.Currency,
        Status = shop.Status,
        ShopUrl = shop.ShopUrl,
        Metadata = shop.Metadata,
        Proxy = shop.Proxy,
        ProfileName = shop.ProfileName,
        Email = shop.Email,
        TiktokPassword = shop.TiktokPassword,
        DayLive = shop.DayLive,
        Deli = shop.Deli,
        NewPhoneNumber = shop.NewPhoneNumber,
        OldBankNumber = shop.OldBankNumber,
        NewBankNumber = shop.NewBankNumber,
        PaymentName = shop.PaymentName,
        ReceivingBankAccountId = shop.ReceivingBankAccountId,
        ReceivingBankAccountName = shop.ReceivingBankAccount?.Name,
        ReceivingBankAccountNumber = shop.ReceivingBankAccount?.AccountNumber,
        ReceivingBankAccountHolderName = shop.ReceivingBankAccount?.AccountHolderName,
        ReceivingBankSourceName = shop.ReceivingBankAccount?.Source?.Name,
        ReceivingBankSourceAccountName = shop.ReceivingBankAccount?.SourceAccount?.Name,
        ReceivingBankAccountCurrency = shop.ReceivingBankAccount?.Currency,
        ShopType = shop.ShopType,
        Categories = ReadCatalogCategories(shop.CatalogCategories, shop.CategoryId, shop.CategoryName),
        CategoryId = shop.CategoryId,
        CategoryName = shop.CategoryName,
        AgeMin = shop.AgeMin,
        AgeMax = shop.AgeMax,
        PriceMinUsd = shop.PriceMinUsd,
        PriceMaxUsd = shop.PriceMaxUsd,
        PreferQuickShip = shop.PreferQuickShip,
        PreferLocal = shop.PreferLocal,
        PreferUsWarehouse = shop.PreferUsWarehouse,
        PreferSheinClub = shop.PreferSheinClub,
        MinRating = shop.MinRating,
        MinSoldCount = shop.MinSoldCount,
        MaxVariantCount = shop.MaxVariantCount,
        BodySizeCode = shop.BodySizeCode,
        StyleCodes = ReadStyleCodes(shop.StyleCodes),
        LlcName = shop.LlcName,
        TwoFactorAuth = shop.TwoFactorAuth,
        SortOrder = shop.SortOrder,
        ActiveListingCount = shop.PublishLimitPublished ?? 0,
        ShopCode = shop.ShopCode,
        TikTokShopStatus = shop.TikTokShopStatus,
        Timezone = shop.Timezone,
        TikTokCreatedAt = shop.TikTokCreatedAt,
        VerifiedDocuments = shop.VerifiedDocuments,
        EmailVerified = shop.EmailVerified,
        PhoneVerified = shop.PhoneVerified,
        SellerType = shop.SellerType,
        BusinessType = shop.BusinessType,
        PayEntityType = shop.PayEntityType,
        CompanyCertNum = shop.CompanyCertNum,
        CompanyCertType = shop.CompanyCertType,
        AccountName = shop.AccountName,
        IsBlocked = shop.IsBlocked,
        IsBlocking = shop.IsBlocking,
        PublishLimitPublished = shop.PublishLimitPublished,
        PublishLimitTotal = shop.PublishLimitTotal,
        TikTokProfileSyncedAt = shop.TikTokProfileSyncedAt,
        AhrScore = shop.AhrScore,
        ShopPerformanceScore = shop.ShopPerformanceScore,
        ProductSatisfactionScore = shop.ProductSatisfactionScore,
        FulfillmentScore = shop.FulfillmentScore,
        CustomerServiceScore = shop.CustomerServiceScore,
        TikTokShopScoreSyncedAt = shop.TikTokShopScoreSyncedAt,
        CompanyAddress = ParseJson<ShopAddressDto>(shop.CompanyAddress),
        OwnerPerson = ParseJson<ShopOwnerPersonDto>(shop.OwnerPerson),
        LogisticsServices = ParseJsonList<ShopLogisticsServiceDto>(shop.LogisticsServices),
        ProductCategories = ParseJsonList<ShopProductCategoryDto>(shop.ProductCategories),
        SettlementBankAccountNumber = shop.SettlementBankAccountNumber,
        SettlementPaymentMethod = shop.SettlementPaymentMethod,
        SettlementBeneficiaryName = shop.SettlementBeneficiaryName,
        SettlementAccountType = shop.SettlementAccountType,
        SettlementAccountStatus = shop.SettlementAccountStatus,
        AutoPublishEnabled = shop.AutoPublishEnabled,
        AutoPublishIntervalSeconds = shop.AutoPublishIntervalSeconds,
        AutoPublishMinDraftAgeMinutes = shop.AutoPublishMinDraftAgeMinutes,
        CreatedAt = shop.CreatedAt,
        UpdatedAt = shop.UpdatedAt
    };

    private static AdminShopDto MapToAdminDto(Shop shop, ApplicationUser owner)
    {
        var dto = MapToDto(shop);
        return new AdminShopDto
        {
            Id = dto.Id,
            Name = dto.Name,
            PlatformId = dto.PlatformId,
            PlatformCode = dto.PlatformCode,
            PlatformName = dto.PlatformName,
            ExternalShopId = dto.ExternalShopId,
            AvatarUrl = dto.AvatarUrl,
            FourSellerShopId = dto.FourSellerShopId,
            FourSellerConnectionStatus = dto.FourSellerConnectionStatus,
            FourSellerConnectedAt = dto.FourSellerConnectedAt,
            Region = dto.Region,
            Currency = dto.Currency,
            Status = dto.Status,
            ShopUrl = dto.ShopUrl,
            Metadata = dto.Metadata,
            Proxy = dto.Proxy,
            ProfileName = dto.ProfileName,
            Email = dto.Email,
            TiktokPassword = dto.TiktokPassword,
            DayLive = dto.DayLive,
            Deli = dto.Deli,
            NewPhoneNumber = dto.NewPhoneNumber,
            OldBankNumber = dto.OldBankNumber,
            NewBankNumber = dto.NewBankNumber,
            PaymentName = dto.PaymentName,
            ReceivingBankAccountId = dto.ReceivingBankAccountId,
            ReceivingBankAccountName = dto.ReceivingBankAccountName,
            ReceivingBankAccountNumber = dto.ReceivingBankAccountNumber,
            ReceivingBankAccountHolderName = dto.ReceivingBankAccountHolderName,
            ReceivingBankSourceName = dto.ReceivingBankSourceName,
            ReceivingBankSourceAccountName = dto.ReceivingBankSourceAccountName,
            ReceivingBankAccountCurrency = dto.ReceivingBankAccountCurrency,
            ShopType = dto.ShopType,
            Categories = dto.Categories,
            CategoryId = dto.CategoryId,
            CategoryName = dto.CategoryName,
            AgeMin = dto.AgeMin,
            AgeMax = dto.AgeMax,
            PriceMinUsd = dto.PriceMinUsd,
            PriceMaxUsd = dto.PriceMaxUsd,
            PreferQuickShip = dto.PreferQuickShip,
            PreferLocal = dto.PreferLocal,
            PreferUsWarehouse = dto.PreferUsWarehouse,
            PreferSheinClub = dto.PreferSheinClub,
            MinRating = dto.MinRating,
            MinSoldCount = dto.MinSoldCount,
            MaxVariantCount = dto.MaxVariantCount,
            BodySizeCode = dto.BodySizeCode,
            StyleCodes = dto.StyleCodes,
            LlcName = dto.LlcName,
            TwoFactorAuth = dto.TwoFactorAuth,
            SortOrder = dto.SortOrder,
            ActiveListingCount = dto.ActiveListingCount,
            ShopCode = dto.ShopCode,
            TikTokShopStatus = dto.TikTokShopStatus,
            Timezone = dto.Timezone,
            TikTokCreatedAt = dto.TikTokCreatedAt,
            VerifiedDocuments = dto.VerifiedDocuments,
            EmailVerified = dto.EmailVerified,
            PhoneVerified = dto.PhoneVerified,
            SellerType = dto.SellerType,
            BusinessType = dto.BusinessType,
            PayEntityType = dto.PayEntityType,
            CompanyCertNum = dto.CompanyCertNum,
            CompanyCertType = dto.CompanyCertType,
            AccountName = dto.AccountName,
            IsBlocked = dto.IsBlocked,
            IsBlocking = dto.IsBlocking,
            PublishLimitPublished = dto.PublishLimitPublished,
            PublishLimitTotal = dto.PublishLimitTotal,
            TikTokProfileSyncedAt = dto.TikTokProfileSyncedAt,
            AhrScore = dto.AhrScore,
            ShopPerformanceScore = dto.ShopPerformanceScore,
            ProductSatisfactionScore = dto.ProductSatisfactionScore,
            FulfillmentScore = dto.FulfillmentScore,
            CustomerServiceScore = dto.CustomerServiceScore,
            TikTokShopScoreSyncedAt = dto.TikTokShopScoreSyncedAt,
            CompanyAddress = dto.CompanyAddress,
            OwnerPerson = dto.OwnerPerson,
            LogisticsServices = dto.LogisticsServices,
            ProductCategories = dto.ProductCategories,
            SettlementBankAccountNumber = dto.SettlementBankAccountNumber,
            SettlementPaymentMethod = dto.SettlementPaymentMethod,
            SettlementBeneficiaryName = dto.SettlementBeneficiaryName,
            SettlementAccountType = dto.SettlementAccountType,
            SettlementAccountStatus = dto.SettlementAccountStatus,
            AutoPublishEnabled = dto.AutoPublishEnabled,
            AutoPublishIntervalSeconds = dto.AutoPublishIntervalSeconds,
            AutoPublishMinDraftAgeMinutes = dto.AutoPublishMinDraftAgeMinutes,
            CreatedAt = dto.CreatedAt,
            UpdatedAt = dto.UpdatedAt,
            OwnerId = shop.OwnerId,
            OwnerFullName = owner.FullName,
            OwnerUserName = owner.UserName ?? string.Empty
        };
    }

    private static readonly JsonSerializerOptions TikTokProfileJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private static void ApplyTikTokProfile(Shop shop, SyncTikTokShopProfileDto dto)
    {
        var shopName = NormalizeOptionalString(dto.ShopName);
        if (shopName is not null)
        {
            shop.Name = shopName;
        }

        var logo = NormalizeOptionalString(dto.LogoUrl);
        if (logo is not null)
        {
            shop.AvatarUrl = logo;
        }

        var region = NormalizeOptionalString(dto.Region);
        if (region is not null)
        {
            AssignShopRegion(shop, region);
        }

        var shopType = NormalizeTikTokShopType(dto.ShopType);
        if (shopType is not null)
        {
            shop.ShopType = shopType;
        }

        var companyName = NormalizeOptionalString(dto.CompanyName);
        if (companyName is not null)
        {
            shop.LlcName = companyName;
        }

        // Email/phone từ TTS thường mask (i***o@…, +1****4409) — không ghi đè contact thật;
        // chỉ fill khi trống hoặc nâng cấp từ giá trị mask cũ. Không đụng TiktokPassword.
        shop.Email = ResolveSyncedContact(shop.Email, dto.AccountEmail);
        shop.NewPhoneNumber = ResolveSyncedContact(shop.NewPhoneNumber, dto.AccountMobile);

        var shopCode = NormalizeOptionalString(dto.ShopCode);
        if (shopCode is not null)
        {
            shop.ShopCode = shopCode;
        }

        if (dto.ShopStatus.HasValue)
        {
            shop.TikTokShopStatus = dto.ShopStatus;
        }

        var opsStatus = TikTokAccountStatusMapper.ResolveOpsStatus(
            dto.SellerStatus,
            dto.ShopStatus,
            shop.Status,
            dto.IdentityVideoVerificationLock == true);
        if (opsStatus is not null)
        {
            shop.Status = opsStatus;
        }

        var timezone = NormalizeOptionalString(dto.Timezone);
        if (timezone is not null)
        {
            shop.Timezone = timezone;
        }

        if (dto.CreateTimeMs is > 0)
        {
            shop.TikTokCreatedAt = DateTimeOffset.FromUnixTimeMilliseconds(dto.CreateTimeMs.Value).UtcDateTime;
            var days = Math.Max(0, (int)(DateTime.UtcNow - shop.TikTokCreatedAt.Value).TotalDays);
            shop.DayLive = $"{days} days";
        }

        if (dto.VerifiedDocuments.HasValue)
        {
            shop.VerifiedDocuments = dto.VerifiedDocuments;
        }

        if (dto.EmailVerified.HasValue)
        {
            shop.EmailVerified = dto.EmailVerified;
        }

        if (dto.PhoneVerified.HasValue)
        {
            shop.PhoneVerified = dto.PhoneVerified;
        }

        if (dto.SellerType.HasValue)
        {
            shop.SellerType = dto.SellerType;
        }

        if (dto.BusinessType.HasValue)
        {
            shop.BusinessType = dto.BusinessType;
        }

        if (dto.PayEntityType.HasValue)
        {
            shop.PayEntityType = dto.PayEntityType;
        }

        var certNum = NormalizeOptionalString(dto.CompanyCertNum);
        if (certNum is not null)
        {
            shop.CompanyCertNum = certNum;
        }

        if (dto.CompanyCertType.HasValue)
        {
            shop.CompanyCertType = dto.CompanyCertType;
        }

        var accountName = NormalizeOptionalString(dto.AccountName);
        if (accountName is not null)
        {
            shop.AccountName = accountName;
        }

        if (dto.IsBlocked.HasValue)
        {
            shop.IsBlocked = dto.IsBlocked;
        }

        if (dto.IsBlocking.HasValue)
        {
            shop.IsBlocking = dto.IsBlocking;
        }

        var published = dto.PublishLimitPublished ?? dto.PublishLimit?.PublishedNum;
        var total = dto.PublishLimitTotal ?? dto.PublishLimit?.TotalLimit;
        if (published.HasValue)
        {
            shop.PublishLimitPublished = published;
        }

        if (total.HasValue)
        {
            shop.PublishLimitTotal = total;
        }

        if (dto.CompanyAddress is not null)
        {
            shop.CompanyAddress = SerializeJson(dto.CompanyAddress);
        }

        if (dto.Owner is not null)
        {
            shop.OwnerPerson = SerializeJson(dto.Owner);
        }

        if (dto.LogisticsServices is { Count: > 0 })
        {
            shop.LogisticsServices = SerializeJson(dto.LogisticsServices);
        }

        if (dto.ProductCategories is { Count: > 0 })
        {
            shop.ProductCategories = SerializeJson(dto.ProductCategories);
        }

        var bankNum = NormalizeOptionalString(dto.SettlementBankAccountNumber)
            ?? NormalizeOptionalString(dto.BankAccountNumber);
        if (bankNum is not null)
        {
            shop.SettlementBankAccountNumber = bankNum;
        }

        var payMethod = NormalizeOptionalString(dto.SettlementPaymentMethod);
        if (payMethod is not null)
        {
            shop.SettlementPaymentMethod = payMethod;
        }

        var beneficiary = NormalizeOptionalString(dto.SettlementBeneficiaryName);
        if (beneficiary is not null)
        {
            shop.SettlementBeneficiaryName = beneficiary;
        }

        var accountType = NormalizeOptionalString(dto.SettlementAccountType);
        if (accountType is not null)
        {
            shop.SettlementAccountType = accountType;
        }

        if (dto.SettlementAccountStatus.HasValue)
        {
            shop.SettlementAccountStatus = dto.SettlementAccountStatus;
        }

        if (dto.OnHoldAmount.HasValue)
        {
            shop.OnHoldAmount = dto.OnHoldAmount;
            shop.OnHoldInTransitAmount = dto.OnHoldInTransitAmount ?? 0;
            shop.OnHoldAwaitingSettlementAmount = dto.OnHoldAwaitingSettlementAmount ?? 0;
            shop.OnHoldReserveAmount = dto.OnHoldReserveAmount ?? 0;
        }

        if (dto.Deli.HasValue)
        {
            shop.Deli = dto.Deli;
        }

        // Auto storefront URL from sellerId + shop name (TikTok Shop US pattern).
        var sellerId = NormalizeOptionalString(dto.SellerId) ?? shop.ExternalShopId;
        var urlName = shopName ?? NormalizeOptionalString(shop.Name);
        var builtUrl = BuildTikTokShopStoreUrl(urlName, sellerId);
        if (builtUrl is not null)
        {
            shop.ShopUrl = builtUrl;
        }
    }

    /// <summary>
    /// Public TikTok Shop storefront: https://www.tiktok.com/shop/store/{slug}/{sellerId}
    /// </summary>
    private static string? BuildTikTokShopStoreUrl(string? shopName, string? sellerId)
    {
        var id = NormalizeOptionalString(sellerId);
        if (id is null)
        {
            return null;
        }

        var slug = SlugifyShopName(shopName);
        if (string.IsNullOrEmpty(slug))
        {
            slug = id;
        }

        return $"https://www.tiktok.com/shop/store/{slug}/{id}";
    }

    private static string SlugifyShopName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        var sb = new System.Text.StringBuilder(name.Length);
        var prevHyphen = false;
        foreach (var ch in name.Trim().ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(ch))
            {
                sb.Append(ch);
                prevHyphen = false;
            }
            else if (!prevHyphen && sb.Length > 0)
            {
                sb.Append('-');
                prevHyphen = true;
            }
        }

        while (sb.Length > 0 && sb[^1] == '-')
        {
            sb.Length--;
        }

        return sb.ToString();
    }

    private static string? NormalizeTikTokShopType(string? value)
    {
        var trimmed = NormalizeOptionalString(value);
        if (trimmed is null)
        {
            return null;
        }

        if (string.Equals(trimmed, ShopTypes.Per, StringComparison.OrdinalIgnoreCase))
        {
            return ShopTypes.Per;
        }

        if (string.Equals(trimmed, ShopTypes.Llc, StringComparison.OrdinalIgnoreCase))
        {
            return ShopTypes.Llc;
        }

        return null;
    }

    private static T? ParseJson<T>(string? json) where T : class
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(json, TikTokProfileJsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static List<T> ParseJsonList<T>(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<T>>(json, TikTokProfileJsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static List<ShopCatalogCategoryDto> NormalizeIncomingCategories(UpdateShopCatalogProfileDto dto)
    {
        if (dto.Categories is { Count: > 0 })
        {
            return dto.Categories
                .Where(c => !string.IsNullOrWhiteSpace(c.Id))
                .Select(c => new ShopCatalogCategoryDto
                {
                    Id = c.Id.Trim(),
                    Name = (c.Name ?? string.Empty).Trim()
                })
                .ToList();
        }

        if (!string.IsNullOrWhiteSpace(dto.CategoryId))
        {
            return
            [
                new ShopCatalogCategoryDto
                {
                    Id = dto.CategoryId.Trim(),
                    Name = (dto.CategoryName ?? string.Empty).Trim()
                }
            ];
        }

        return [];
    }

    private static List<ShopCatalogCategoryDto> ReadCatalogCategories(
        string? json,
        string? categoryId,
        string? categoryName)
    {
        var parsed = ParseJsonList<ShopCatalogCategoryDto>(json)
            .Where(c => !string.IsNullOrWhiteSpace(c.Id))
            .GroupBy(c => c.Id.Trim(), StringComparer.Ordinal)
            .Select(g =>
            {
                var first = g.First();
                first.Id = first.Id.Trim();
                first.Name = string.IsNullOrWhiteSpace(first.Name) ? first.Id : first.Name.Trim();
                return first;
            })
            .ToList();
        if (parsed.Count > 0)
        {
            return parsed;
        }

        if (!string.IsNullOrWhiteSpace(categoryId))
        {
            var id = categoryId.Trim();
            return
            [
                new ShopCatalogCategoryDto
                {
                    Id = id,
                    Name = string.IsNullOrWhiteSpace(categoryName) ? id : categoryName.Trim()
                }
            ];
        }

        return [];
    }

    private static void WriteCatalogCategories(Shop shop, List<ShopCatalogCategoryDto> categories)
    {
        if (categories.Count == 0)
        {
            shop.CatalogCategories = null;
            shop.CategoryId = null;
            shop.CategoryName = null;
            return;
        }

        shop.CatalogCategories = SerializeJson(categories);
        shop.CategoryId = categories[0].Id;
        var joined = string.Join(CatalogNameJoin, categories.Select(c => c.Name));
        shop.CategoryName = joined.Length > 2000 ? joined[..2000] : joined;
    }

    private static string? SerializeJson<T>(T value) =>
        JsonSerializer.Serialize(value, TikTokProfileJsonOptions);

    private static DateTime? ParseFourSellerCreateTime(string? createTime) =>
        AppTimeZones.ParseFourSellerNaiveAsUtc(createTime);

    private static string ResolveFourSellerConnectionStatus(string? fourSellerShopId) =>
        string.IsNullOrWhiteSpace(fourSellerShopId)
            ? FourSellerConnectionStatuses.NotConnected
            : FourSellerConnectionStatuses.Connected;

    /// <summary>
    /// Stamps the 4Seller internal id on a shop.
    /// Returns true when the id actually changed, so the caller can relink existing orders.
    /// </summary>
    private static bool ApplyFourSellerConnection(Shop shop, string? fourSellerShopId)
    {
        var normalized = NormalizeOptionalString(fourSellerShopId);
        var previous = NormalizeOptionalString(shop.FourSellerShopId);
        var wasConnected = previous is not null;

        // Existing Orders / Listings still carry the outgoing id — keep it so ownership rematch can
        // find them after the shop is reconnected under a different 4Seller account.
        if (previous is not null && !string.Equals(previous, normalized, StringComparison.OrdinalIgnoreCase))
        {
            shop.PreviousFourSellerShopId = previous;
        }

        shop.FourSellerShopId = normalized;

        if (normalized is not null)
        {
            // First link, or legacy row that already had FourSellerShopId before ConnectedAt existed.
            if (!wasConnected || shop.FourSellerConnectedAt is null)
            {
                shop.FourSellerConnectedAt = DateTime.UtcNow;
            }
        }
        else
        {
            shop.FourSellerConnectedAt = null;
        }

        return !string.Equals(previous, normalized, StringComparison.OrdinalIgnoreCase);
    }

    private static void ApplyExtendedFields(Shop shop, CreateShopDto dto)
    {
        shop.Proxy = dto.Proxy;
        shop.ProfileName = dto.ProfileName;
        shop.Email = dto.Email;
        shop.TiktokPassword = dto.TiktokPassword;
        shop.DayLive = dto.DayLive;
        shop.Deli = dto.Deli;
        shop.NewPhoneNumber = dto.NewPhoneNumber;
        shop.OldBankNumber = dto.OldBankNumber;
        shop.NewBankNumber = dto.NewBankNumber;
        shop.PaymentName = dto.PaymentName;
        shop.ShopType = NormalizeOptionalString(dto.ShopType);
        shop.LlcName = dto.LlcName;
        shop.TwoFactorAuth = dto.TwoFactorAuth;
    }

    private static void ApplyExtendedFields(Shop shop, UpdateShopDto dto)
    {
        shop.Proxy = dto.Proxy;
        shop.ProfileName = dto.ProfileName;
        shop.Email = dto.Email;
        shop.TiktokPassword = dto.TiktokPassword;
        shop.DayLive = dto.DayLive;
        shop.Deli = dto.Deli;
        shop.NewPhoneNumber = dto.NewPhoneNumber;
        shop.OldBankNumber = dto.OldBankNumber;
        shop.NewBankNumber = dto.NewBankNumber;
        shop.PaymentName = dto.PaymentName;
        shop.ShopType = NormalizeOptionalString(dto.ShopType);
        shop.LlcName = dto.LlcName;
        shop.TwoFactorAuth = dto.TwoFactorAuth;
    }

    private async Task ApplyCatalogProfileAsync(
        Shop shop,
        UpdateShopCatalogProfileDto dto,
        CancellationToken cancellationToken)
    {
        var incoming = NormalizeIncomingCategories(dto);
        if (incoming.Count == 0)
        {
            throw new BusinessException("Chọn ít nhất một category cho shop.");
        }

        if (incoming.Count > MaxShopCatalogCategories)
        {
            throw new BusinessException($"Tối đa {MaxShopCatalogCategories} category mỗi shop.");
        }

        var site = ResolveRegion(shop.Region);
        var resolved = new List<ShopCatalogCategoryDto>(incoming.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in incoming)
        {
            var categoryId = item.Id.Trim();
            if (!seen.Add(categoryId))
            {
                continue;
            }

            var category = await EnsureValidCatalogCategoryAsync(shop, categoryId, cancellationToken);
            var displayName = await ResolveCatalogDisplayNameAsync(
                category,
                site,
                item.Name,
                cancellationToken);
            resolved.Add(new ShopCatalogCategoryDto
            {
                Id = category.ExternalId,
                Name = displayName.Length > 500 ? displayName[..500] : displayName
            });
        }

        WriteCatalogCategories(shop, resolved);
        shop.AgeMin = dto.AgeMin;
        shop.AgeMax = dto.AgeMax;
        shop.PriceMinUsd = dto.PriceMinUsd;
        shop.PriceMaxUsd = dto.PriceMaxUsd;
        shop.PreferQuickShip = dto.PreferQuickShip;
        shop.PreferLocal = dto.PreferLocal;
        shop.PreferUsWarehouse = dto.PreferUsWarehouse;
        shop.PreferSheinClub = dto.PreferSheinClub;
        shop.MinRating = NormalizeMinRating(dto.MinRating);
        shop.MinSoldCount = NormalizeMinSoldCount(dto.MinSoldCount);
        shop.MaxVariantCount = NormalizeMaxVariantCount(dto.MaxVariantCount);
        shop.BodySizeCode = NormalizeBodySizeCode(dto.BodySizeCode);
        WriteStyleCodes(shop, dto.StyleCodes);

        if (dto.AutoPublishEnabled.HasValue)
        {
            await ApplyAutoPublishToggleAsync(
                shop,
                dto.AutoPublishEnabled.Value,
                cancellationToken);
        }

        if (dto.AutoPublishIntervalSeconds.HasValue)
        {
            shop.AutoPublishIntervalSeconds = Math.Max(1, dto.AutoPublishIntervalSeconds.Value);
        }

        if (dto.AutoPublishMinDraftAgeMinutes.HasValue)
        {
            shop.AutoPublishMinDraftAgeMinutes = Math.Max(
                ListingAutoPublishSettingKeys.MinDraftAgeMinutes,
                dto.AutoPublishMinDraftAgeMinutes.Value);
        }

        shop.UpdatedAt = DateTime.UtcNow;
    }

    private async Task ApplyAutoPublishAsync(
        Shop shop,
        UpdateShopAutoPublishDto dto,
        CancellationToken cancellationToken)
    {
        await ApplyAutoPublishToggleAsync(shop, dto.AutoPublishEnabled, cancellationToken);

        if (dto.AutoPublishIntervalSeconds.HasValue)
        {
            shop.AutoPublishIntervalSeconds = Math.Max(1, dto.AutoPublishIntervalSeconds.Value);
        }

        if (dto.AutoPublishMinDraftAgeMinutes.HasValue)
        {
            shop.AutoPublishMinDraftAgeMinutes = Math.Max(
                ListingAutoPublishSettingKeys.MinDraftAgeMinutes,
                dto.AutoPublishMinDraftAgeMinutes.Value);
        }

        shop.UpdatedAt = DateTime.UtcNow;
    }

    private async Task ApplyAutoPublishToggleAsync(
        Shop shop,
        bool enabled,
        CancellationToken cancellationToken)
    {
        if (enabled)
        {
            await EnsureSystemAutoPublishEnabledAsync(cancellationToken);
        }

        var wasEnabled = shop.AutoPublishEnabled;
        shop.AutoPublishEnabled = enabled;
        if (!wasEnabled && shop.AutoPublishEnabled)
        {
            _autoPublishCancellation.ResetShop(shop.Id);
        }

        if (wasEnabled && !shop.AutoPublishEnabled)
        {
            _autoPublishCancellation.CancelShop(shop.Id, "shop_auto_publish_disabled");
        }
    }

    private static decimal NormalizeMinRating(decimal value) =>
        Math.Clamp(Math.Round(value, 1, MidpointRounding.AwayFromZero), 0m, 5m);

    private static int NormalizeMinSoldCount(int value) => Math.Max(0, value);

    private static int NormalizeMaxVariantCount(int value) => Math.Clamp(value, 0, 500);

    private static string? NormalizeBodySizeCode(string? code)
    {
        var normalized = NormalizeOptionalString(code);
        if (normalized is null)
        {
            return null;
        }

        var match = ShopBodySizeCodes.All.FirstOrDefault(c =>
            string.Equals(c, normalized, StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            throw new BusinessException(
                "Body size không hợp lệ. Chọn regular, plus, midsize, curvy, petite hoặc tall.");
        }

        return match;
    }

    private static List<string> ReadStyleCodes(string? json) =>
        ParseJsonList<string>(json)
            .Select(ShopStyleCodes.Canonical)
            .Where(c => c is not null)
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static void WriteStyleCodes(Shop shop, List<string>? codes)
    {
        var normalized = NormalizeStyleCodes(codes);
        shop.StyleCodes = normalized.Count == 0 ? null : SerializeJson(normalized);
    }

    private static List<string> NormalizeStyleCodes(List<string>? codes)
    {
        if (codes is null || codes.Count == 0)
        {
            return [];
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        foreach (var raw in codes)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            var canonical = ShopStyleCodes.Canonical(raw);
            // Code cũ (`casual-woman`/`royal`) hoặc typo — bỏ im, không 400.
            if (canonical is null)
            {
                continue;
            }

            if (seen.Add(canonical))
            {
                result.Add(canonical);
            }
        }

        if (result.Count > ShopStyleCodes.MaxPerShop)
        {
            throw new BusinessException($"Tối đa {ShopStyleCodes.MaxPerShop} style mỗi shop.");
        }

        return result;
    }

    private async Task<TikTokCategory> EnsureValidCatalogCategoryAsync(
        Shop shop,
        string categoryId,
        CancellationToken cancellationToken)
    {
        var site = ResolveRegion(shop.Region);
        var category = await _context.TikTokCategories
            .AsNoTracking()
            .FirstOrDefaultAsync(
                c => c.Site.ToUpper() == site && c.ExternalId == categoryId,
                cancellationToken);

        if (category is null)
        {
            throw new BusinessException(
                $"Không tìm thấy category {categoryId} cho site {site}. Sync catalog trước.");
        }

        if (!category.IsActive)
        {
            throw new BusinessException("Category không còn active. Chọn lại từ catalog.");
        }

        return category;
    }

    private async Task<string> ResolveCatalogDisplayNameAsync(
        TikTokCategory category,
        string site,
        string? clientName,
        CancellationToken cancellationToken)
    {
        var pathIds = (category.Path ?? string.Empty)
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        if (pathIds.Count == 0)
        {
            pathIds.Add(category.ExternalId);
        }

        var names = await _context.TikTokCategories
            .AsNoTracking()
            .Where(c => c.Site.ToUpper() == site && pathIds.Contains(c.ExternalId))
            .Select(c => new { c.ExternalId, c.Name })
            .ToListAsync(cancellationToken);

        var byId = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var row in names)
        {
            byId.TryAdd(row.ExternalId, row.Name);
        }
        var labels = new List<string>(pathIds.Count);
        foreach (var id in pathIds)
        {
            if (byId.TryGetValue(id, out var name) && !string.IsNullOrWhiteSpace(name))
            {
                labels.Add(name.Trim());
            }
        }

        if (labels.Count == pathIds.Count && labels.Count > 0)
        {
            return string.Join(" / ", labels);
        }

        return NormalizeOptionalString(clientName) ?? category.Name;
    }

    private static ShopCatalogProfileDto MapToCatalogProfileDto(Shop shop)
    {
        var categories = ReadCatalogCategories(shop.CatalogCategories, shop.CategoryId, shop.CategoryName);
        return new ShopCatalogProfileDto
        {
            Id = shop.Id,
            Categories = categories,
            CategoryId = shop.CategoryId,
            CategoryName = shop.CategoryName,
            AgeMin = shop.AgeMin,
            AgeMax = shop.AgeMax,
            PriceMinUsd = shop.PriceMinUsd,
            PriceMaxUsd = shop.PriceMaxUsd,
            PreferQuickShip = shop.PreferQuickShip,
            PreferLocal = shop.PreferLocal,
            PreferUsWarehouse = shop.PreferUsWarehouse,
            PreferSheinClub = shop.PreferSheinClub,
            MinRating = shop.MinRating,
            MinSoldCount = shop.MinSoldCount,
            MaxVariantCount = shop.MaxVariantCount,
            BodySizeCode = shop.BodySizeCode,
            StyleCodes = ReadStyleCodes(shop.StyleCodes),
            AutoPublishEnabled = shop.AutoPublishEnabled,
            AutoPublishIntervalSeconds = shop.AutoPublishIntervalSeconds,
            AutoPublishMinDraftAgeMinutes = shop.AutoPublishMinDraftAgeMinutes
        };
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ShopAutoPublishSchedulerStatusItemDto>> GetAutoPublishSchedulerStatusAsync(
        IReadOnlyList<Guid> shopIds,
        Guid? ownerUserId = null,
        CancellationToken cancellationToken = default)
    {
        if (shopIds.Count == 0)
        {
            return [];
        }

        const int maxIds = 200;
        var ids = shopIds.Distinct().Take(maxIds).ToList();

        var shopsQuery = _context.Shops.AsNoTracking()
            .Where(s => ids.Contains(s.Id) && s.AutoPublishEnabled);
        if (ownerUserId is Guid owner)
        {
            shopsQuery = shopsQuery.Where(s => s.OwnerId == owner);
        }

        var enabledIds = await shopsQuery
            .Select(s => s.Id)
            .ToListAsync(cancellationToken);

        if (enabledIds.Count == 0)
        {
            return [];
        }

        var statusMap = await _shopAutoPublishSchedulerStatus.ResolveAsync(enabledIds, cancellationToken);

        return enabledIds
            .Where(statusMap.ContainsKey)
            .Select(id =>
            {
                var status = statusMap[id];
                return new ShopAutoPublishSchedulerStatusItemDto
                {
                    Id = id,
                    AutoPublishSchedulerStatus = status.SchedulerStatus,
                    AutoPublishNextPickAt = status.NextPickAt,
                    AutoPublishEligibleDraftCount = status.EligibleDraftCount,
                };
            })
            .ToList();
    }

    private async Task EnrichAutoPublishSchedulerStatusAsync(
        List<ShopDto> shops,
        CancellationToken cancellationToken)
    {
        var enabledIds = shops.Where(s => s.AutoPublishEnabled).Select(s => s.Id).ToList();
        if (enabledIds.Count == 0)
        {
            return;
        }

        var statusMap = await _shopAutoPublishSchedulerStatus.ResolveAsync(enabledIds, cancellationToken);
        foreach (var shop in shops)
        {
            ApplyAutoPublishSchedulerStatus(shop, statusMap);
        }
    }

    private async Task EnrichAutoPublishSchedulerStatusAsync(
        List<AdminShopListItemDto> shops,
        CancellationToken cancellationToken)
    {
        var enabledIds = shops.Where(s => s.AutoPublishEnabled).Select(s => s.Id).ToList();
        if (enabledIds.Count == 0)
        {
            return;
        }

        var statusMap = await _shopAutoPublishSchedulerStatus.ResolveAsync(enabledIds, cancellationToken);
        foreach (var shop in shops)
        {
            ApplyAutoPublishSchedulerStatus(shop, statusMap);
        }
    }

    private async Task EnrichTodayOnlineSecondsAsync(
        List<ShopDto> shops,
        CancellationToken cancellationToken)
    {
        var secondsByShop = await SumTodayOnlineSecondsByShopAsync(
            shops.Select(s => s.Id).ToList(),
            cancellationToken);
        foreach (var shop in shops)
        {
            shop.TodayOnlineSeconds = secondsByShop.GetValueOrDefault(shop.Id);
        }
    }

    private async Task EnrichTodayOnlineSecondsAsync(
        List<AdminShopListItemDto> shops,
        CancellationToken cancellationToken)
    {
        var secondsByShop = await SumTodayOnlineSecondsByShopAsync(
            shops.Select(s => s.Id).ToList(),
            cancellationToken);
        foreach (var shop in shops)
        {
            shop.TodayOnlineSeconds = secondsByShop.GetValueOrDefault(shop.Id);
        }
    }

    /// <summary>
    /// One round-trip: sessions whose <c>StartedAt</c> falls on today's Vietnam calendar day
    /// (DayBoundary already splits overnight sessions). Open rows use UTC now as end.
    /// </summary>
    private async Task<Dictionary<Guid, long>> SumTodayOnlineSecondsByShopAsync(
        IReadOnlyList<Guid> shopIds,
        CancellationToken cancellationToken)
    {
        if (shopIds.Count == 0)
        {
            return new Dictionary<Guid, long>();
        }

        var vnToday = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, AppTimeZones.Vietnam));
        var (dayStartUtc, dayEndUtc) = AppTimeZones.GetVietnamDayUtcRange(vnToday);
        var now = DateTime.UtcNow;

        var rows = await _context.ShopOnlineSessions.AsNoTracking()
            .Where(s => shopIds.Contains(s.ShopId)
                        && s.StartedAt >= dayStartUtc
                        && s.StartedAt <= dayEndUtc)
            .Select(s => new { s.ShopId, s.StartedAt, s.EndedAt })
            .ToListAsync(cancellationToken);

        var result = new Dictionary<Guid, long>();
        foreach (var row in rows)
        {
            var end = row.EndedAt ?? now;
            if (end < row.StartedAt)
            {
                end = row.StartedAt;
            }

            var seconds = (long)(end - row.StartedAt).TotalSeconds;
            if (seconds <= 0)
            {
                continue;
            }

            result[row.ShopId] = result.GetValueOrDefault(row.ShopId) + seconds;
        }

        return result;
    }

    private static void ApplyAutoPublishSchedulerStatus(
        ShopDto shop,
        IReadOnlyDictionary<Guid, ShopAutoPublishSchedulerStatusResult> statusMap)
    {
        if (!statusMap.TryGetValue(shop.Id, out var status))
        {
            return;
        }

        shop.AutoPublishSchedulerStatus = status.SchedulerStatus;
        shop.AutoPublishNextPickAt = status.NextPickAt;
        shop.AutoPublishEligibleDraftCount = status.EligibleDraftCount;
    }

    private static void ApplyAutoPublishSchedulerStatus(
        AdminShopListItemDto shop,
        IReadOnlyDictionary<Guid, ShopAutoPublishSchedulerStatusResult> statusMap)
    {
        if (!statusMap.TryGetValue(shop.Id, out var status))
        {
            return;
        }

        shop.AutoPublishSchedulerStatus = status.SchedulerStatus;
        shop.AutoPublishNextPickAt = status.NextPickAt;
        shop.AutoPublishEligibleDraftCount = status.EligibleDraftCount;
    }

    private static ShopAutoPublishDto MapToAutoPublishDto(Shop shop) =>
        new()
        {
            Id = shop.Id,
            AutoPublishEnabled = shop.AutoPublishEnabled,
            AutoPublishIntervalSeconds = shop.AutoPublishIntervalSeconds,
            AutoPublishMinDraftAgeMinutes = shop.AutoPublishMinDraftAgeMinutes
        };

    /// <summary>
    /// Category catalog is per marketplace site. Changing region invalidates the leaf id.
    /// Age/price/flags stay — they are not site-specific.
    /// </summary>
    private static void AssignShopRegion(Shop shop, string? region)
    {
        var next = ResolveRegion(region);
        if (string.Equals(shop.Region, next, StringComparison.OrdinalIgnoreCase))
        {
            shop.Region = next;
            return;
        }

        shop.Region = next;
        shop.Currency = ResolveCurrency(next);
        shop.CategoryId = null;
        shop.CategoryName = null;
        shop.CatalogCategories = null;
    }

    private async Task ApplyReceivingBankAccountAsync(
        Shop shop,
        Guid? receivingBankAccountId,
        CancellationToken cancellationToken)
    {
        if (!receivingBankAccountId.HasValue)
        {
            shop.ReceivingBankAccountId = null;
            return;
        }

        var account = await _context.ReceivingBankAccounts
            .FirstOrDefaultAsync(a => a.Id == receivingBankAccountId.Value, cancellationToken);
        if (account is null)
        {
            throw new NotFoundException("Không tìm thấy thẻ nhận tiền.");
        }

        if (!account.IsActive && shop.ReceivingBankAccountId != account.Id)
        {
            throw new BusinessException("Thẻ nhận tiền đang bị vô hiệu, không thể kết nối.");
        }

        shop.ReceivingBankAccountId = account.Id;
        shop.NewBankNumber = account.AccountNumber;
    }

    /// <summary>
    /// Owner handover spans several flushes, so it needs one unit of work. InMemory (tests) has no
    /// transaction support, and the caller then falls back to the plain save path.
    /// </summary>
    private async Task<IDbContextTransaction?> BeginTransactionAsync(CancellationToken cancellationToken)
    {
        if (_context is not DbContext ef)
        {
            return null;
        }

        var provider = ef.Database.ProviderName ?? "";
        if (provider.Contains("InMemory", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return await ef.Database.BeginTransactionAsync(cancellationToken);
    }

    /// <summary>
    /// Shop fields must persist even when the operational-period bookkeeping loses a race on
    /// <c>IX_ShopOperationalPeriods_ShopId_Open</c>. Periods are derived data rebuilt on the next
    /// status transition, so they are what gets dropped — never the shop row.
    /// </summary>
    private async Task SaveChangesAllowingOpenPeriodRaceAsync(
        Guid shopId,
        CancellationToken cancellationToken,
        IDbContextTransaction? transaction = null)
    {
        // Inside an ambient transaction the failed save leaves Postgres in an aborted state, so the
        // retry below needs a savepoint to roll back to. Without one it dies on "transaction is aborted".
        const string savepoint = "before_shop_save";
        var savepointTaken = false;
        if (transaction?.SupportsSavepoints == true)
        {
            await transaction.CreateSavepointAsync(savepoint, cancellationToken);
            savepointTaken = true;
        }

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ShopOperationalPeriodService.IsDuplicateOpenPeriod(ex))
        {
            if (savepointTaken)
            {
                await transaction!.RollbackToSavepointAsync(savepoint, cancellationToken);
            }

            // Materialize first: changing entry states mutates the change tracker.
            foreach (var entry in _context.ChangeTracker.Entries<ShopOperationalPeriod>().ToList())
            {
                switch (entry.State)
                {
                    case EntityState.Added:
                        entry.State = EntityState.Detached;
                        break;

                    case EntityState.Modified:
                        entry.CurrentValues.SetValues(entry.OriginalValues);
                        entry.State = EntityState.Unchanged;
                        break;
                }
            }

            _logger.LogWarning(
                ex,
                "Shop {ShopId}: bỏ qua cập nhật ShopOperationalPeriods vì đụng IX_ShopOperationalPeriods_ShopId_Open, dữ liệu shop vẫn được lưu.",
                shopId);

            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    private void MaybeCancelAutoPublishOnShopDeactivate(Shop shop, string previousStatus)
    {
        if (string.Equals(shop.Status, ShopStatuses.Active, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!string.Equals(previousStatus, ShopStatuses.Active, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _autoPublishCancellation.CancelShop(shop.Id, "shop_deactivated");
    }

    private async Task EnsureSystemAutoPublishEnabledAsync(CancellationToken cancellationToken)
    {
        var raw = await _settings.GetValueAsync(ListingAutoPublishSettingKeys.Enabled, cancellationToken);
        if (!string.Equals(raw?.Trim(), "true", StringComparison.OrdinalIgnoreCase))
        {
            throw new BusinessException("Auto publish hệ thống đã tắt — bật trong Settings trước.");
        }
    }
}
