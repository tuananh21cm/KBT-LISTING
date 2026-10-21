using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Application.DTOs.FourSeller;
using Application.Interfaces;
using Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Shared.Constants;
using Shared.Helpers;

namespace Infrastructure.Integrations.FourSeller;

/// <summary>
/// 4Seller external API client.
/// </summary>
public class FourSellerApiService : IFourSellerApiService
{
    private const string AuthRedirectPath = "/api/auth/redirect/url";
    private const string IsLoginPath = "/api/user/is-login";
    private const string ShopListPath = "/api/shop/get-list";
    private const string ShopTidyListPath = "/api/shop/get-tidy-list";
    private const string CategoryListPath = "/api/meta/tiktok/get-category-list";
    private const string CategoryAttributeListByIdPath = "/api/meta/tiktok/get-category-attribute-list-by-id";
    private const string BrandListByShopPath = "/api/meta/tiktok/get-brand-list-by-shop-id";
    private const string BrandSuggestByShopPath = "/api/meta/tiktok/get-brand-list-by-shop-id-brand-suggest";
    private const string WarehouseListByShopPath = "/api/meta/tiktok/get-warehouse-list-by-shop-id";
    private const string AiGenerateCategoryPath = "/api/ai/generate-category-tk";
    private const string RecentUseCategoryPath = "/api/ai/recent-use-category";
    private const string BatchGetMoveSourceListingPath = "/api/listing/tiktok/batch-get-move-source-listing";
    private const string BatchBingingCategoryPath = "/api/listing/tiktok/batch-binging-category";
    private const string CrawlUrlsPath = "/api/crawl/get-crawl-url-repeat";
    private const string PostHtmlFileShopPath = "/api/crawl/postHtml-file-shop";
    private const string PostHtmlVersion = "1.2.97";
    private const string MoveDraftPagePath = "/api/crawl/get-move-draft-page";
    private const string SaveToPlatformPath = "/api/crawl/save-to-platform";
    private const string GetSchedulePath = "/api/crawl/get-schedule";
    private const string DeleteDraftPath = "/api/crawl/delete-draft";
    private const string OrderPagePath = "/api/v2/order/page";
    private const string OrderDetailPath = "/api/v2/order/detail";
    private const string TikTokReturnSyncPath = "/api/returns/tiktok/sync";
    private const string TikTokReturnPagePath = "/api/returns/tiktok/page";
    private const string AfterSalesReferer =
        "https://www.4seller.com/web/order/after-sales-manage/index.html?type=afterSalesOrder";
    private const string ListPlatformShippingServicePath = "/api/v2/order/list-platform-shipping-service";
    private const string ProductDiscountActivityPagePath = "/api/promotion/tiktok/activity/get-page";
    private const string ProductDiscountDeactivatePath =
        "/api/promotion/tiktok/activity/deactivate-activity";
    private const string ProductDiscountReferer =
        "https://www.4seller.com/web/marketing/product-discount/index.html";
    private const string FlashDealReferer =
        "https://www.4seller.com/web/marketing/flash-deal/index.html";
    private const string FlashSaleDiscountType = "FLASHSALE";
    private const string ToShipMarkShipPath = "/api/v2/order/to-ship-mark-ship";
    private const string GetAsyncShipOrderResultPath = "/api/v2/order/get-async-ship-order-result";
    private const string UpdateOrderNotePath = "/api/v2/order/update-note";
    private const string OrderToShipReferer = "https://www.4seller.com/web/order2.html?type=toShip";
    private const string OrderShippedReferer = "https://www.4seller.com/web/order2.html?type=shipped";
    private const string OrderAllReferer = "https://www.4seller.com/web/order2.html?type=allOrder";
    private const string DefaultShopifyShop = "https://.myshopify.com";
    private const string ShopReferer = "https://www.4seller.com/web/setting/shop.html";
    private const string TidyListReferer =
        "https://www.4seller.com/web/listing/tiktok.html?type=tiktokDraft";
    private const string ListingReferer =
        "https://www.4seller.com/web/listing/tiktok/create.html?status=draft";
    private const string ListingEditReferer =
        "https://www.4seller.com/web/listing/tiktok/edit/";
    private const string ScrapeReferer =
        "https://www.4seller.com/web/tools-v2/scarape/history.html";
    private const string Origin = "https://www.4seller.com";
    private const string UserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/150.0.0.0 Safari/537.36";

    /// <summary>
    /// A proxy that cannot be reached fails inside its 12s ConnectTimeout. Anything that dies later
    /// already had a tunnel, so it is 4Seller being slow — rerouting it would triple the wait.
    /// </summary>
    private static readonly TimeSpan ProxyConnectFailureWindow = TimeSpan.FromSeconds(20);

    /// <summary>Above this, an outbound call is worth reporting at Information level.</summary>
    private static readonly TimeSpan SlowCallThreshold = TimeSpan.FromSeconds(3);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// 4Seller listing/page requires explicit nulls (e.g. <c>isGlobal: null</c>); omitting them → 业务异常参数错误.
    /// </summary>
    private static readonly JsonSerializerOptions ListingJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    /// <summary>Delay before a confirm probe when the first is-login looks expired.</summary>
    private static readonly TimeSpan ExpireConfirmDelay = TimeSpan.FromMilliseconds(1500);

    private readonly IFourSellerHttpClientProvider _clientProvider;
    private readonly IFourSellerProxyManager _proxyManager;
    private readonly IFourSellerSessionCache _sessionCache;
    private readonly IApplicationDbContext _dbContext;
    private readonly ILogger<FourSellerApiService> _logger;

    /// <summary>
    /// Per-request cache: skip repeated <c>is-login</c> for the same user within one DI scope (e.g. order sync pagination).
    /// </summary>
    private Guid? _sessionValidatedUserId;
    private string? _sessionValidatedCookie;

    /// <summary>
    /// User currently being served by this scoped instance (set on cookie/proxy resolve).
    /// Used by timing logs so probes are not attributed to a previous seller.
    /// </summary>
    private Guid? _servingUserId;

    /// <summary>
    /// Effective egress proxy for the seller currently being served (null = direct).
    /// Set when the seller's cookie is resolved; read by every outbound send. Safe because
    /// this scoped instance serves sellers sequentially (parallel sub-calls share one seller).
    /// </summary>
    private string? _activeProxyUrl;

    /// <summary>
    /// True when the active proxy came from the seller's personal sticky setting
    /// (never fall back to direct — jumping IP can kill the cookie).
    /// </summary>
    private bool _activeProxyIsSticky;

    /// <summary>
    /// Initializes the 4Seller API service.
    /// </summary>
    public FourSellerApiService(
        IFourSellerHttpClientProvider clientProvider,
        IFourSellerProxyManager proxyManager,
        IFourSellerSessionCache sessionCache,
        IApplicationDbContext dbContext,
        ILogger<FourSellerApiService> logger)
    {
        _clientProvider = clientProvider;
        _proxyManager = proxyManager;
        _sessionCache = sessionCache;
        _dbContext = dbContext;
        _logger = logger;
    }

    /// <summary>
    /// Sends a 4Seller request through the seller's resolved proxy client and detects EdgeOne
    /// IP blocks (HTTP 567). On a block, the proxy is put on cooldown and a
    /// <see cref="FourSellerBlockedException"/> is thrown so callers do NOT treat it as an
    /// expired cookie. Sticky personal proxies never fall back to direct egress (jumping IP
    /// can kill the session). Response content is buffered (default), so callers can still
    /// read the body.
    /// </summary>
    private async Task<HttpResponseMessage> SendFourSellerAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var proxy = _activeProxyUrl;
        HttpRequestMessage? clone = null;
        if (proxy is not null)
        {
            clone = await CloneHttpRequestAsync(request, cancellationToken);
        }

        var attemptClock = Stopwatch.StartNew();
        try
        {
            return await SendFourSellerOnceAsync(clone ?? request, proxy, cancellationToken);
        }
        catch (Exception ex) when (proxy is not null
            && !cancellationToken.IsCancellationRequested
            && IsProxyConnectFailure(ex, attemptClock.Elapsed))
        {
            var httpEx = ex as HttpRequestException ?? FindHttpRequestException(ex);
            var reason = httpEx is not null && IsProxyTunnelFailure(httpEx) ? "407" : "connect";
            _proxyManager.ReportBlocked(proxy, reason);

            // Same IP, different scheme — safe for sticky sessions.
            if (!EgressProxyParser.HasExplicitScheme(proxy))
            {
                var currentScheme = EgressProxyParser.GetSchemeOrDefault(proxy);
                var alternateScheme = string.Equals(currentScheme, "http", StringComparison.OrdinalIgnoreCase)
                    ? "https"
                    : "http";
                var alternateProxy = EgressProxyParser.WithScheme(proxy, alternateScheme);

                _logger.LogWarning(
                    ex,
                    "4Seller proxy {Proxy} connect failed ({Scheme}); retrying with {AlternateScheme}",
                    MaskProxy(proxy),
                    currentScheme,
                    alternateScheme);

                var alternateClock = Stopwatch.StartNew();
                try
                {
                    var response = await SendFourSellerOnceAsync(clone ?? request, alternateProxy, cancellationToken);
                    _activeProxyUrl = alternateProxy;
                    return response;
                }
                catch (Exception ex2) when (!cancellationToken.IsCancellationRequested
                    && IsProxyConnectFailure(ex2, alternateClock.Elapsed))
                {
                    _proxyManager.ReportBlocked(alternateProxy, "connect");
                }
            }

            // Sticky personal proxy: never fall back to direct — jumping IP can kill the cookie.
            if (_activeProxyIsSticky)
            {
                throw new FourSellerUnavailableException(
                    $"Proxy 4Seller sticky không kết nối được ({MaskProxy(proxy)}). " +
                    "Giữ nguyên proxy để tránh nhảy IP làm chết cookie — thử lại sau hoặc kiểm tra user:pass/scheme.",
                    proxy);
            }

            // Pool / no-personal-proxy sellers may fall back to direct.
            _logger.LogWarning(
                ex,
                "4Seller proxy {Proxy} connect failed; retrying without proxy",
                MaskProxy(proxy));
            _activeProxyUrl = null;
            return await SendFourSellerOnceAsync(request, proxyUrl: null, cancellationToken);
        }
        finally
        {
            clone?.Dispose();
        }
    }

    private static HttpRequestException? FindHttpRequestException(Exception ex)
    {
        for (Exception? cur = ex; cur is not null; cur = cur.InnerException)
        {
            if (cur is HttpRequestException httpEx)
            {
                return httpEx;
            }
        }

        return null;
    }

    private async Task<HttpResponseMessage> SendFourSellerOnceAsync(
        HttpRequestMessage request,
        string? proxyUrl,
        CancellationToken cancellationToken)
    {
        var client = _clientProvider.GetClient(proxyUrl);
        var clock = Stopwatch.StartNew();
        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, cancellationToken);
        }
        catch (Exception ex)
        {
            LogCallTiming(request, proxyUrl, clock.Elapsed, ex.GetType().Name, failed: true);
            throw;
        }

        LogCallTiming(
            request,
            proxyUrl,
            clock.Elapsed,
            ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture),
            failed: !response.IsSuccessStatusCode);

        if ((int)response.StatusCode == 567)
        {
            _proxyManager.ReportBlocked(proxyUrl, "567");
            string body;
            try
            {
                body = await response.Content.ReadAsStringAsync(cancellationToken);
            }
            catch
            {
                body = string.Empty;
            }
            finally
            {
                response.Dispose();
            }

            var via = proxyUrl is null ? "IP trực tiếp" : $"proxy {MaskProxy(proxyUrl)}";
            throw new FourSellerBlockedException(
                $"4Seller (EdgeOne) chặn truy cập theo IP — HTTP 567 Restricted Access qua {via}. " +
                "Đây KHÔNG phải cookie hết hạn. Đổi/thêm proxy residential trong pool hoặc chờ hết cooldown. " +
                $"{Truncate(body, 120)}".TrimEnd(),
                proxyUrl);
        }

        if (response.IsSuccessStatusCode)
        {
            _proxyManager.ReportSuccess(proxyUrl);
        }

        return response;
    }

    /// <summary>
    /// Host:port only. Pool entries use the credential-bearing <c>host:port:user:pass</c> form that
    /// <see cref="MaskProxy"/> (which expects <c>user:pass@host</c>) would pass through untouched.
    /// </summary>
    private static string ProxyLabel(string? proxyUrl)
    {
        if (string.IsNullOrWhiteSpace(proxyUrl))
        {
            return "direct";
        }

        var withoutScheme = proxyUrl;
        var schemeEnd = withoutScheme.IndexOf("://", StringComparison.Ordinal);
        if (schemeEnd >= 0)
        {
            withoutScheme = withoutScheme[(schemeEnd + 3)..];
        }

        var at = withoutScheme.LastIndexOf('@');
        if (at >= 0)
        {
            withoutScheme = withoutScheme[(at + 1)..];
        }

        var parts = withoutScheme.Split(':', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 ? $"{parts[0]}:{parts[1]}" : parts.FirstOrDefault() ?? "proxy";
    }

    /// <summary>
    /// Outbound latency is the main cost of every sync, so slow calls are surfaced at Information
    /// while healthy ones stay at Debug to keep the log readable.
    /// </summary>
    private void LogCallTiming(
        HttpRequestMessage request,
        string? proxyUrl,
        TimeSpan elapsed,
        string outcome,
        bool failed)
    {
        var slow = failed || elapsed >= SlowCallThreshold;
        if (!slow && !_logger.IsEnabled(LogLevel.Debug))
        {
            return;
        }

        var path = request.RequestUri?.AbsolutePath ?? "?";
        var via = ProxyLabel(proxyUrl);
        var elapsedMs = (long)elapsed.TotalMilliseconds;
        var ownerId = _servingUserId ?? _sessionValidatedUserId;

        if (slow)
        {
            _logger.LogInformation(
                "4Seller call {Path} (seller {OwnerId}, via {Via}) → {Outcome} in {ElapsedMs} ms",
                path,
                ownerId,
                via,
                outcome,
                elapsedMs);
            return;
        }

        _logger.LogDebug(
            "4Seller call {Path} (seller {OwnerId}, via {Via}) → {Outcome} in {ElapsedMs} ms",
            path,
            ownerId,
            via,
            outcome,
            elapsedMs);
    }

    /// <summary>407/403 / CONNECT tunnel fail — proxy từ chối, chưa tới 4Seller.</summary>
    private static bool IsProxyTunnelFailure(HttpRequestException ex)
    {
        if (ex.StatusCode == HttpStatusCode.ProxyAuthenticationRequired)
        {
            return true;
        }

        for (Exception? cur = ex; cur is not null; cur = cur.InnerException)
        {
            var msg = cur.Message ?? string.Empty;
            if (msg.Contains("407", StringComparison.Ordinal)
                || msg.Contains("403", StringComparison.Ordinal)
                || msg.Contains("proxy tunnel", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("Proxy Authentication", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Proxy unreachable: tunnel rejected, connect timeout, or TLS/handshake fail before 4Seller.
    /// </summary>
    /// <param name="elapsed">
    /// How long the attempt ran. A cancellation/timeout raised after <see cref="ProxyConnectFailureWindow"/>
    /// means the tunnel was established and 4Seller itself was slow, so rerouting would only multiply
    /// the wait. Callers that merely classify an error for a message pass the default (no window check).
    /// </param>
    private static bool IsProxyConnectFailure(Exception ex, TimeSpan elapsed = default)
    {
        if (ex is HttpRequestException httpEx && IsProxyTunnelFailure(httpEx))
        {
            return true;
        }

        if (elapsed >= ProxyConnectFailureWindow)
        {
            return false;
        }

        for (Exception? cur = ex; cur is not null; cur = cur.InnerException)
        {
            if (cur is OperationCanceledException or TimeoutException)
            {
                return true;
            }

            var msg = cur.Message ?? string.Empty;
            if (msg.Contains("timed out", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("connection attempt failed", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("proxy tunnel", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("407", StringComparison.Ordinal)
                || msg.Contains("403", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static async Task<HttpRequestMessage> CloneHttpRequestAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri);
        foreach (var header in request.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        if (request.Content is not null)
        {
            var bytes = await request.Content.ReadAsByteArrayAsync(cancellationToken);
            var content = new ByteArrayContent(bytes);
            foreach (var header in request.Content.Headers)
            {
                content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            clone.Content = content;
        }

        return clone;
    }

    /// <summary>Reads the seller's stored proxy and resolves the effective egress proxy into <see cref="_activeProxyUrl"/>.</summary>
    private async Task SetActiveProxyAsync(Guid userId, CancellationToken cancellationToken)
    {
        _servingUserId = userId;
        var userProxy = await _dbContext.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => u.FourSellerProxy)
            .FirstOrDefaultAsync(cancellationToken);
        _activeProxyIsSticky = !string.IsNullOrWhiteSpace(userProxy);
        _activeProxyUrl = await _proxyManager.ResolveAsync(userId, userProxy, cancellationToken);
    }

    /// <summary>Masks credentials in a proxy URL for logging/messages.</summary>
    private static string MaskProxy(string proxy)
    {
        var at = proxy.IndexOf('@');
        if (at < 0)
        {
            return proxy;
        }

        var schemeEnd = proxy.IndexOf("://", StringComparison.Ordinal);
        var start = schemeEnd >= 0 ? schemeEnd + 3 : 0;
        return string.Concat(proxy.AsSpan(0, start), "***", proxy.AsSpan(at));
    }

    /// <inheritdoc />
    public async Task<FourSellerLoginCheckResponse> CheckLoginAsync(
        Guid userId,
        string? fourSellerCookie = null,
        CancellationToken cancellationToken = default)
    {
        string cookie;
        try
        {
            if (!string.IsNullOrWhiteSpace(fourSellerCookie))
            {
                cookie = fourSellerCookie.Trim();
                if (!CookieHasUserToken(cookie))
                {
                    return new FourSellerLoginCheckResponse
                    {
                        IsLoggedIn = false,
                        Message =
                            "Cookie 4Seller thiếu userToken hoặc không hợp lệ. Đăng nhập 4Seller trên trình duyệt rồi copy cookie đầy đủ."
                    };
                }

                // Draft cookie: ResolveCookieAsync is skipped, so resolve the seller's proxy here.
                await SetActiveProxyAsync(userId, cancellationToken);
            }
            else
            {
                // Check-login may run when status is unchecked/expired — do not require logged_in.
                cookie = await ResolveCookieAsync(userId, cancellationToken, requireLoggedInStatus: false);
            }
        }
        catch (BusinessException ex)
        {
            return new FourSellerLoginCheckResponse
            {
                IsLoggedIn = false,
                Message = ex.Message
            };
        }

        try
        {
            var (state, detail) = await ProbeIsLoginAsync(cookie, cancellationToken);
            return state switch
            {
                FourSellerSessionState.LoggedIn => MarkAndRespondLoggedIn(userId, cookie),
                FourSellerSessionState.Expired => new FourSellerLoginCheckResponse
                {
                    IsLoggedIn = false,
                    Message = detail
                },
                _ => new FourSellerLoginCheckResponse
                {
                    IsLoggedIn = false,
                    Unverified = true,
                    Message = detail
                }
            };
        }
        catch (FourSellerBlockedException ex)
        {
            // IP blocked by EdgeOne (567): cannot verify the session — do NOT treat as expired.
            return new FourSellerLoginCheckResponse
            {
                IsLoggedIn = false,
                Blocked = true,
                Message = ex.Message
            };
        }
        catch (FourSellerUnavailableException ex)
        {
            return new FourSellerLoginCheckResponse
            {
                IsLoggedIn = false,
                Unverified = true,
                Message = ex.Message
            };
        }
        catch (HttpRequestException ex)
        {
            return new FourSellerLoginCheckResponse
            {
                IsLoggedIn = false,
                Unverified = true,
                Message = IsProxyConnectFailure(ex)
                    ? "Proxy 4Seller không kết nối được (timeout/407/403). Kiểm tra user:pass; thử prefix https:// hoặc socks5://. Không đổi trạng thái cookie."
                    : $"Không kết nối được 4Seller: {ex.Message}. Không đổi trạng thái cookie."
            };
        }
        catch (Exception ex) when (IsProxyConnectFailure(ex))
        {
            return new FourSellerLoginCheckResponse
            {
                IsLoggedIn = false,
                Unverified = true,
                Message =
                    "Proxy 4Seller không phản hồi (timeout). Kiểm tra IP/port/user/pass; thử https:// hoặc socks5://. Không đổi trạng thái cookie."
            };
        }
    }

    private FourSellerLoginCheckResponse MarkAndRespondLoggedIn(Guid userId, string cookie)
    {
        _sessionCache.MarkValid(userId, cookie);
        return new FourSellerLoginCheckResponse
        {
            IsLoggedIn = true,
            Message = "Cookie 4Seller hợp lệ — đã đăng nhập."
        };
    }

    /// <inheritdoc />
    public async Task<FourSellerAuthRedirectResponse> GetAuthRedirectUrlAsync(
        Guid userId,
        FourSellerAuthRedirectRequest request,
        CancellationToken cancellationToken = default)
    {
        var payload = new FourSellerApiAuthRedirectBody
        {
            PlatformCode = request.PlatformCode,
            ShopName = request.ShopName,
            SiteList = request.SiteList.ToList(),
            ShopifyShop = string.IsNullOrWhiteSpace(request.ShopifyShop)
                ? DefaultShopifyShop
                : request.ShopifyShop
        };

        var apiResponse = await PostAsync<FourSellerApiResponse<string>>(
            userId,
            AuthRedirectPath,
            payload,
            ShopReferer,
            cancellationToken);

        if (string.IsNullOrWhiteSpace(apiResponse.Data))
        {
            throw new BusinessException("4Seller API không trả về link xác thực.");
        }

        return new FourSellerAuthRedirectResponse
        {
            AuthorizeUrl = apiResponse.Data
        };
    }

    /// <inheritdoc />
    public async Task<FourSellerShopCheckResponse> CheckShopConnectionAsync(
        Guid userId,
        FourSellerShopCheckRequest request,
        CancellationToken cancellationToken = default)
    {
        var records = await GetShopListAsync(userId, cancellationToken);
        return MatchShopAgainstList(request, records);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<FourSellerShopRecordDto>> GetShopListAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var listResponse = await PostAsync<FourSellerApiResponse<FourSellerShopListData>>(
            userId,
            ShopListPath,
            new { pageCurrent = 1, pageSize = 999 },
            ShopReferer,
            cancellationToken);

        return (listResponse.Data?.Records ?? []).Select(MapRecord).ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<FourSellerShopRecordDto>> GetTidyShopListAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var listResponse = await PostAsync<FourSellerApiResponse<FourSellerShopListData>>(
            userId,
            ShopTidyListPath,
            new { pageCurrent = 1, pageSize = 999 },
            TidyListReferer,
            cancellationToken);

        return (listResponse.Data?.Records ?? []).Select(MapRecord).ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<FourSellerCategoryNodeDto>> GetCategoryListAsync(
        Guid userId,
        FourSellerCategoryListRequest request,
        CancellationToken cancellationToken = default)
    {
        var parentId = string.IsNullOrWhiteSpace(request.ParentId) ? "0" : request.ParentId.Trim();
        object parentIdValue = long.TryParse(parentId, out var parentNumeric) ? parentNumeric : parentId;

        var payload = new
        {
            site = string.IsNullOrWhiteSpace(request.Site) ? "US" : request.Site.Trim(),
            parentId = parentIdValue,
            shopId = request.ShopId
        };

        var body = await PostRawAsync(userId, CategoryListPath, payload, ListingReferer, cancellationToken);
        return ParseCategoryListResponse(body);
    }

    /// <inheritdoc />
    public async Task<FourSellerAiCategoryResultDto> GenerateCategoryAsync(
        Guid userId,
        FourSellerAiCategoryRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Title))
        {
            throw new BusinessException("Title là bắt buộc để AI gợi ý category.");
        }

        if (request.ShopId <= 0)
        {
            throw new BusinessException("ShopId 4Seller không hợp lệ.");
        }

        var site = string.IsNullOrWhiteSpace(request.Site) ? "US" : request.Site.Trim();
        var form = new Dictionary<string, string>
        {
            ["shopId"] = request.ShopId.ToString(),
            ["site"] = site,
            ["title"] = request.Title.Trim()
        };

        var body = await PostFormRawAsync(
            userId,
            AiGenerateCategoryPath,
            form,
            $"{ListingEditReferer}?status=draft",
            cancellationToken);

        return ParseAiCategoryResponse(body, site);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<FourSellerCategoryAttributeDto>> GetCategoryAttributeListByIdAsync(
        Guid userId,
        string categoryId,
        long shopId,
        string? site = null,
        CancellationToken cancellationToken = default)
    {
        var trimmedCategoryId = (categoryId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(trimmedCategoryId))
        {
            throw new BusinessException("CategoryId là bắt buộc để lấy schema Specifics.");
        }

        if (shopId <= 0)
        {
            throw new BusinessException("ShopId 4Seller không hợp lệ.");
        }

        var siteCode = string.IsNullOrWhiteSpace(site) ? "US" : site.Trim();
        var path =
            $"{CategoryAttributeListByIdPath}?categoryId={Uri.EscapeDataString(trimmedCategoryId)}" +
            $"&shopId={shopId}&site={Uri.EscapeDataString(siteCode)}";
        var body = await GetRawAsync(userId, path, ListingReferer, cancellationToken);
        return ParseCategoryAttributeListResponse(body);
    }

    /// <inheritdoc />
    public async Task<FourSellerCrawlSubmitResult> SubmitCrawlUrlsAsync(
        Guid userId,
        IReadOnlyList<string> urls,
        CancellationToken cancellationToken = default)
    {
        var cleaned = urls
            .Where(u => !string.IsNullOrWhiteSpace(u))
            .Select(u => SourceUrlKeyHelper.CanonicalizeForCrawl(u))
            .Where(u => !string.IsNullOrWhiteSpace(u))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (cleaned.Count == 0)
        {
            throw new BusinessException("Danh sách URL scrape không được để trống.");
        }

        // Only get-crawl-url-repeat exists (get-crawl-url = 404).
        // data:[] with code:0 = accepted to async pool (poll drafts); ≠ cookie expiry (4001).
        // Login is validated via GET /api/user/is-login inside outbound helpers.
        var body = await PostCrawlUrlsRawAsync(userId, CrawlUrlsPath, cleaned, cancellationToken);
        var parsed = ParseCrawlSubmitData(body);

        if (parsed.UrlsNeedingConfirm.Count > 0)
        {
            var toRepeat = parsed.UrlsNeedingConfirm
                .Where(u => !string.IsNullOrWhiteSpace(u))
                .Select(u => SourceUrlKeyHelper.CanonicalizeForCrawl(u))
                .Where(u => !string.IsNullOrWhiteSpace(u))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (toRepeat.Count == 0)
            {
                toRepeat = cleaned;
            }

            var confirmBody = await PostCrawlUrlsRawAsync(userId, CrawlUrlsPath, toRepeat, cancellationToken);
            var confirmParsed = ParseCrawlSubmitData(confirmBody);
            // Prefer confirm response when it returns drafts or another confirm list;
            // empty data:[] after confirm still means accepted into the async crawl pool.
            parsed = confirmParsed;
            body = confirmBody;
        }

        // data:[] (HTTP 200 + code:0) = accepted into crawl pool — NOT a failure and NOT cookie expiry
        // (expiry is code 4001). Drafts may take 30–120s; FE/BE poll get-move-draft-page to claim.
        var claimedKeys = parsed.ExistingDrafts
            .Select(d => SourceUrlKeyHelper.Normalize(d.SourceUrl))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var stillMissing = cleaned
            .Where(u => !claimedKeys.Contains(SourceUrlKeyHelper.Normalize(u)))
            .ToList();
        var acceptedToPool = stillMissing.Count > 0;

        return new FourSellerCrawlSubmitResult
        {
            ExistingDrafts = parsed.ExistingDrafts,
            AcceptedToPool = acceptedToPool,
            RawResponse = body
        };
    }

    /// <inheritdoc />
    public async Task<FourSellerPostHtmlResultDto> PostHtmlFileShopAsync(
        Guid userId,
        FourSellerPostHtmlRequest request,
        CancellationToken cancellationToken = default)
    {
        var shopIds = request.ShopIdList
            .Where(id => id > 0)
            .Distinct()
            .ToList();
        if (shopIds.Count == 0)
        {
            return new FourSellerPostHtmlResultDto
            {
                Ok = false,
                Message = "Chọn ít nhất 1 shop trước khi thêm sản phẩm."
            };
        }

        var url = (request.Url ?? string.Empty).Trim();
        var html = request.Html ?? string.Empty;
        if (string.IsNullOrWhiteSpace(url) || html.Length < 200)
        {
            return new FourSellerPostHtmlResultDto
            {
                Ok = false,
                Message = "Không lấy được nội dung trang sản phẩm."
            };
        }

        var site = (request.Site ?? string.Empty).Trim();
        var siteList = (request.SiteList ?? [])
            .Select(s => (s ?? string.Empty).Trim())
            .Where(s => s.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (siteList.Count == 0 && site.Length > 0)
        {
            siteList.Add(site);
        }

        // Best-effort probe (same as extension): ignore failures before postHtml.
        try
        {
            await PostCrawlUrlsRawAsync(userId, CrawlUrlsPath, [url], cancellationToken);
        }
        catch
        {
            // Intentionally ignored — postHtml still creates drafts when this call fails.
        }

        var payloadJson = JsonSerializer.Serialize(new
        {
            url,
            html,
            shopIdList = shopIds,
            site,
            siteList
        }, JsonOptions);

        var zipBytes = BuildPostHtmlZip(payloadJson);
        var cookie = await EnsureFourSellerLoggedInAsync(userId, cancellationToken);
        var requestId = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var path = $"{PostHtmlFileShopPath}?requestId={requestId}";

        var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(zipBytes);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/plain");
        form.Add(fileContent, "file", "file.zip");
        form.Add(new StringContent("true"), "isV1");
        form.Add(new StringContent(PostHtmlVersion), "version");

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, path);
        httpRequest.Headers.TryAddWithoutValidation("Accept", "application/json, text/plain, */*");
        httpRequest.Headers.TryAddWithoutValidation("Accept-Language", "vi,en-US;q=0.9,en;q=0.8");
        httpRequest.Headers.TryAddWithoutValidation("Origin", Origin);
        httpRequest.Headers.TryAddWithoutValidation("Referer", ScrapeReferer);
        httpRequest.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
        httpRequest.Headers.Add("Cookie", cookie);
        httpRequest.Content = form;

        var response = await SendFourSellerAsync(httpRequest, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return new FourSellerPostHtmlResultDto
            {
                Ok = false,
                Message = ExtractPostHtmlError(body, $"postHtml HTTP {(int)response.StatusCode}")
            };
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.TryGetProperty("code", out var codeProp) &&
                codeProp.ValueKind == JsonValueKind.Number &&
                codeProp.GetInt32() != 0)
            {
                return new FourSellerPostHtmlResultDto
                {
                    Ok = false,
                    Message = ExtractPostHtmlError(body, $"4Seller trả code {codeProp.GetInt32()}")
                };
            }
        }
        catch (JsonException)
        {
            // Non-JSON success body is rare; treat as OK when HTTP succeeded.
        }

        return new FourSellerPostHtmlResultDto
        {
            Ok = true,
            Message = "Đã thêm sản phẩm"
        };
    }

    private static byte[] BuildPostHtmlZip(string payloadJson)
    {
        using var zipStream = new MemoryStream();
        using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("file.txt", CompressionLevel.Optimal);
            using var entryStream = entry.Open();
            var bytes = Encoding.UTF8.GetBytes(payloadJson);
            entryStream.Write(bytes, 0, bytes.Length);
        }

        return zipStream.ToArray();
    }

    private static string ExtractPostHtmlError(string body, string fallback)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return fallback;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            var fromApi = ReadApiErrorMessage(root);
            if (!string.IsNullOrWhiteSpace(fromApi))
            {
                return fromApi;
            }
        }
        catch (JsonException)
        {
            // fall through
        }

        return $"{fallback}: {Truncate(body, 200)}";
    }

    /// <summary>
    /// Posts crawl URLs. Requires exact HTTP 200 and <c>code:0</c>; otherwise throws.
    /// </summary>
    private async Task<string> PostCrawlUrlsRawAsync(
        Guid userId,
        string path,
        IReadOnlyList<string> urls,
        CancellationToken cancellationToken)
    {
        var cookie = await EnsureFourSellerLoggedInAsync(userId, cancellationToken);

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, path);
        httpRequest.Headers.TryAddWithoutValidation("Accept", "application/json, text/plain, */*");
        httpRequest.Headers.TryAddWithoutValidation("Accept-Language", "vi,en-US;q=0.9,en;q=0.8");
        httpRequest.Headers.TryAddWithoutValidation("Origin", Origin);
        httpRequest.Headers.TryAddWithoutValidation("Referer", ScrapeReferer);
        httpRequest.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
        httpRequest.Headers.Add("Cookie", cookie);
        var requestJson = JsonSerializer.Serialize(urls.ToList());
        httpRequest.Content = new StringContent(requestJson, Encoding.UTF8, "application/json");

        var response = await SendFourSellerAsync(httpRequest, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var status = (int)response.StatusCode;


        if (status != 200)
        {
            throw new BusinessException(
                $"4Seller crawl thất bại (HTTP {status} {path}): {Truncate(body, 300)}");
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.TryGetProperty("success", out var successProp) &&
                successProp.ValueKind == JsonValueKind.False)
            {
                var messages = ReadApiErrorMessage(root) ?? Truncate(body, 300);
                throw new BusinessException($"4Seller crawl thất bại: {messages}");
            }

            if (!root.TryGetProperty("code", out var codeProp) ||
                codeProp.ValueKind != JsonValueKind.Number ||
                codeProp.GetInt32() != 0)
            {
                var messages = ReadApiErrorMessage(root) ?? Truncate(body, 300);
                throw new BusinessException($"4Seller crawl thất bại (code ≠ 0): {messages}");
            }
        }
        catch (JsonException)
        {
            throw new BusinessException($"4Seller crawl trả về JSON không hợp lệ: {Truncate(body, 300)}");
        }

        return body;
    }

    private sealed class CrawlSubmitParseResult
    {
        public List<string> UrlsNeedingConfirm { get; } = [];
        public List<FourSellerMoveDraftRecordDto> ExistingDrafts { get; } = [];
    }

    /// <summary>
    /// Parses crawl <c>data</c>: empty = pool; strings = confirm; objects with id = existing drafts.
    /// </summary>
    private static CrawlSubmitParseResult ParseCrawlSubmitData(string body)
    {
        var result = new CrawlSubmitParseResult();
        try
        {
            using var document = JsonDocument.Parse(body);
            if (!document.RootElement.TryGetProperty("data", out var data) ||
                data.ValueKind != JsonValueKind.Array)
            {
                return result;
            }

            foreach (var item in data.EnumerateArray())
            {
                switch (item.ValueKind)
                {
                    case JsonValueKind.String:
                        var s = item.GetString();
                        if (!string.IsNullOrWhiteSpace(s))
                        {
                            result.UrlsNeedingConfirm.Add(s);
                        }

                        break;
                    case JsonValueKind.Object:
                        // Draft objects mean already scraped — claim them, never treat as confirm URLs.
                        TryReadLong(item, out var id, "id");
                        if (id > 0)
                        {
                            result.ExistingDrafts.Add(MapMoveDraftRecord(item));
                        }
                        else
                        {
                            var fromObj = ReadString(item, "url", "sourceUrl", "source_url", "link");
                            if (!string.IsNullOrWhiteSpace(fromObj))
                            {
                                result.UrlsNeedingConfirm.Add(fromObj);
                            }
                        }

                        break;
                }
            }
        }
        catch (JsonException)
        {
            return result;
        }

        return result;
    }

    /// <inheritdoc />
    public async Task<FourSellerMoveDraftPageDto> GetMoveDraftPageAsync(
        Guid userId,
        FourSellerMoveDraftPageRequest request,
        CancellationToken cancellationToken = default)
    {
        var pageCurrent = request.PageCurrent < 1 ? 1 : request.PageCurrent;
        var pageSize = request.PageSize < 1 ? 100 : Math.Min(request.PageSize, 200);

        var payload = new
        {
            pageCurrent,
            pageSize,
            searchValue = string.IsNullOrWhiteSpace(request.SearchValue)
                ? null
                : request.SearchValue.Trim()
        };

        var body = await PostCrawlJsonAsync(userId, MoveDraftPagePath, payload, cancellationToken);
        var page = ParseMoveDraftPageResponse(body, pageCurrent, pageSize);
        return page;
    }

    /// <inheritdoc />
    public async Task<FourSellerSaveToPlatformResponse> SaveToPlatformAsync(
        Guid userId,
        FourSellerSaveToPlatformRequest request,
        CancellationToken cancellationToken = default)
    {
        var draftIds = request.MoveDraftId.Where(id => id > 0).Distinct().ToList();
        var shopIds = request.ShopIdList.Where(id => id > 0).Distinct().ToList();

        if (draftIds.Count == 0)
        {
            throw new BusinessException("Chọn ít nhất một sản phẩm draft để đẩy lên platform.");
        }

        if (shopIds.Count == 0)
        {
            throw new BusinessException("Chọn ít nhất một shop 4Seller (shopIdList).");
        }

        var payload = new
        {
            moveDraftId = draftIds,
            shopIdList = shopIds
        };

        var body = await PostCrawlJsonAsync(userId, SaveToPlatformPath, payload, cancellationToken);
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.String)
        {
            throw new BusinessException("4Seller không trả về scheduleCode.");
        }

        var scheduleCode = data.GetString();
        if (string.IsNullOrWhiteSpace(scheduleCode))
        {
            throw new BusinessException("4Seller không trả về scheduleCode.");
        }

        return new FourSellerSaveToPlatformResponse
        {
            ScheduleCode = scheduleCode
        };
    }

    /// <inheritdoc />
    public async Task<FourSellerScheduleProgressDto> GetScheduleAsync(
        Guid userId,
        string scheduleCode,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(scheduleCode))
        {
            throw new BusinessException("scheduleCode là bắt buộc.");
        }

        var path =
            $"{GetSchedulePath}?scheduleCode={Uri.EscapeDataString(scheduleCode.Trim())}";
        var body = await GetCrawlAsync(userId, path, cancellationToken);
        return ParseScheduleProgressResponse(body);
    }

    /// <inheritdoc />
    public async Task DeleteMoveDraftsAsync(
        Guid userId,
        IReadOnlyList<long> moveDraftIds,
        CancellationToken cancellationToken = default)
    {
        var ids = moveDraftIds.Where(id => id > 0).Distinct().ToList();
        if (ids.Count == 0)
        {
            throw new BusinessException("Chọn ít nhất một draft để xóa.");
        }

        await PostCrawlJsonAsync(userId, DeleteDraftPath, ids, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<FourSellerListingPageDto> GetListingPageAsync(
        Guid userId,
        string platform,
        FourSellerListingPageRequest request,
        CancellationToken cancellationToken = default)
    {
        var platformCode = NormalizeListingPlatform(platform);
        var pageCurrent = request.PageCurrent < 1 ? 1 : request.PageCurrent;
        var pageSize = request.PageSize < 1 ? 100 : Math.Min(request.PageSize, 100);
        // Local UI uses "removed"; 4Seller page API expects "remove".
        var status = ListingSiteStatuses.ToFourSellerPageStatus(
            string.IsNullOrWhiteSpace(request.Status) ? ListingSiteStatuses.Draft : request.Status);
        var searchValues = (request.SearchValue ?? [])
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v.Trim())
            .ToList();

        // Only filter by createTime. Filling platformCreateTime at the same time AND-filters
        // drafts out (no platform create time until published).
        // Request DateFrom/DateTo are UTC (VN business day bounds from admin-web); 4Seller
        // naive createTime* strings are America/Los_Angeles wall-clock for the same absolute window.
        var hasDateRange = request.DateFrom.HasValue && request.DateTo.HasValue;
        object createTimeRange = Array.Empty<object>();
        var createTimeStart = string.Empty;
        var createTimeEnd = string.Empty;
        if (hasDateRange)
        {
            var (localFrom, localTo) = FourSellerOrderDateTime.ToPacificLocalRange(
                request.DateFrom!.Value,
                request.DateTo!.Value);
            createTimeStart = FormatFourSellerDateTimeNaive(localFrom);
            createTimeEnd = FormatFourSellerDateTimeNaive(localTo);
            createTimeRange = new[] { createTimeStart, createTimeEnd };
        }

        // Delta pull: 4Seller compares this against the same naive digits it returns in updateTime,
        // so no timezone conversion here (unlike the create-time filter above).
        object updateTimeRange = Array.Empty<object>();
        var updateTimeStart = string.Empty;
        var updateTimeEnd = string.Empty;
        if (request.UpdatedFrom.HasValue)
        {
            updateTimeStart = FormatFourSellerDateTimeNaive(request.UpdatedFrom.Value);
            updateTimeEnd = FormatFourSellerDateTimeNaive(
                request.UpdatedTo ?? request.UpdatedFrom.Value.AddYears(1));
            updateTimeRange = new[] { updateTimeStart, updateTimeEnd };
        }

        var payload = new Dictionary<string, object?>
        {
            ["pageCurrent"] = pageCurrent,
            ["pageSize"] = pageSize,
            ["isGlobal"] = null,
            ["suspendedStatus"] = "",
            ["status"] = status,
            ["orderBy"] = string.IsNullOrWhiteSpace(request.OrderBy) ? "update_time" : request.OrderBy.Trim(),
            ["desc"] = string.IsNullOrWhiteSpace(request.Desc) ? "desc" : request.Desc.Trim(),
            ["shopId"] = request.ShopId?.Trim() ?? string.Empty,
            ["queryStatus"] = "",
            ["sitePublishStatus"] = "",
            ["searchType"] = string.IsNullOrWhiteSpace(request.SearchType) ? "msku" : request.SearchType.Trim(),
            ["searchValue"] = searchValues,
            ["searchMethod"] = string.IsNullOrWhiteSpace(request.SearchMethod) ? "exact" : request.SearchMethod.Trim(),
            ["groupId"] = "",
            ["migrationPageType"] = "q-all",
            ["listingIdList"] = request.ListingIds is { Count: > 0 }
                ? request.ListingIds.Where(id => !string.IsNullOrWhiteSpace(id)).ToArray()
                : Array.Empty<object>(),
            ["hasVariation"] = "",
            ["stockAlertType"] = "",
            ["stockAlertNum"] = 0,
            ["categoryId"] = "",
            ["site"] = "",
            ["categoryMap"] = new[] { "" },
            ["lowPrice"] = "",
            ["highPrice"] = "",
            ["createTime"] = createTimeRange,
            ["createTimeStart"] = createTimeStart,
            ["createTimeEnd"] = createTimeEnd,
            ["platformCreateTime"] = Array.Empty<object>(),
            ["platformCreateTimeStart"] = "",
            ["platformCreateTimeEnd"] = "",
            ["updateTime"] = updateTimeRange,
            ["updateTimeStart"] = updateTimeStart,
            ["updateTimeEnd"] = updateTimeEnd,
            ["mapped"] = "",
            ["hasMove"] = ""
        };

        var path = $"/api/listing/{platformCode}/page";
        var referer = BuildListingPageReferer(platformCode, status);
        string body;
        try
        {
            body = await PostRawAsync(userId, path, payload, referer, cancellationToken, ListingJsonOptions);
        }
        catch (BusinessException ex) when (request.UpdatedFrom.HasValue
                                           && IsListingPageUpdateTimeFailure(ex))
        {
            // 4Seller intermittently returns Comm.Back.Get_Page_Failure when updateTime* is set
            // even though the same bucket succeeds without the filter (see update-time-probe).
            _logger.LogWarning(
                "4Seller listing page rejected updateTime filter for seller {OwnerId} status {Status} ({Start}–{End}); retrying unfiltered",
                userId,
                status,
                updateTimeStart,
                updateTimeEnd);

            payload["updateTime"] = Array.Empty<object>();
            payload["updateTimeStart"] = string.Empty;
            payload["updateTimeEnd"] = string.Empty;
            body = await PostRawAsync(userId, path, payload, referer, cancellationToken, ListingJsonOptions);
        }

        return ParseListingPageResponse(body, pageCurrent, pageSize);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<FourSellerListingRecordDto>> GetListingBatchDetailAsync(
        Guid userId,
        string platform,
        IReadOnlyList<long> listingIds,
        CancellationToken cancellationToken = default,
        bool allowPerIdFallback = true)
    {
        var platformCode = NormalizeListingPlatform(platform);
        var ids = listingIds.Where(id => id > 0).Distinct().ToList();
        if (ids.Count == 0)
        {
            return [];
        }

        var referer = BuildListingPageReferer(platformCode, "draft");
        var batchPath = $"/api/listing/{platformCode}/batch-detail";

        // The bare id array is what the other batch listing endpoints (delete,
        // batch-get-move-source-listing) send and what 4Seller accepts. The object form with
        // three duplicate id arrays draws 业务异常参数错误 on every call. Both are tried so a
        // server-side shape change degrades to the old behaviour instead of losing every detail.
        var payloadShapes = new (string Name, object Payload)[]
        {
            ("array", ids),
            ("object", new { listingIdList = ids, listingIds = ids, ids })
        };

        foreach (var (shapeName, payload) in payloadShapes)
        {
            try
            {
                var body = await PostRawAsync(
                    userId,
                    batchPath,
                    payload,
                    referer,
                    cancellationToken,
                    ListingJsonOptions);
                var parsed = ParseListingDetailListResponse(body);
                if (parsed.Count > 0)
                {
                    return parsed;
                }

                // HTTP 200 and code 0, yet nothing parsed: the response shape moved. Without a
                // body sample this is indistinguishable from an API error, since both reach the
                // sync loop as an empty list.
                _logger.LogWarning(
                    "batch-detail returned no records for {Count} ids despite a successful "
                    + "response (user {UserId}, platform {Platform}, payload {Shape}). "
                    + "Body sample: {BodySample}",
                    ids.Count,
                    userId,
                    platformCode,
                    shapeName,
                    Truncate(body, 500));
            }
            catch (BusinessException ex)
            {
                _logger.LogWarning(
                    "batch-detail rejected by 4Seller for {Count} ids (user {UserId}, platform "
                    + "{Platform}, payload {Shape}): {Reason}",
                    ids.Count,
                    userId,
                    platformCode,
                    shapeName,
                    ex.Message);
            }
        }

        if (!allowPerIdFallback)
        {
            return [];
        }

        var results = new List<FourSellerListingRecordDto>();
        var detailPath = $"/api/listing/{platformCode}/detail";
        foreach (var id in ids)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var body = await PostFormRawAsync(
                    userId,
                    detailPath,
                    new Dictionary<string, string> { ["listingId"] = id.ToString(), ["id"] = id.ToString() },
                    $"{ListingEditReferer}{id}.html?status=draft",
                    cancellationToken);
                var record = ParseSingleListingDetailResponse(body, id);
                if (record is not null)
                {
                    results.Add(record);
                }
            }
            catch (BusinessException)
            {
                // Skip failed detail; import layer reports missing ids.
            }
        }

        return results;
    }

    /// <inheritdoc />
    public async Task DeleteListingsAsync(
        Guid userId,
        string platform,
        IReadOnlyList<long> listingIds,
        CancellationToken cancellationToken = default)
    {
        var platformCode = NormalizeListingPlatform(platform);
        var ids = listingIds.Where(id => id > 0).Distinct().ToList();
        if (ids.Count == 0)
        {
            throw new BusinessException("Chọn ít nhất một listing để xóa.");
        }

        var path = $"/api/listing/{platformCode}/delete";
        var referer = BuildListingPageReferer(platformCode, "draft");
        await PostRawAsync(userId, path, ids, referer, cancellationToken, ListingJsonOptions);
    }

    /// <inheritdoc />
    public async Task<FourSellerListingStatusCountDto> GetListingStatusCountAsync(
        Guid userId,
        string platform,
        string? shopId = null,
        CancellationToken cancellationToken = default)
    {
        var platformCode = NormalizeListingPlatform(platform);
        var payload = new Dictionary<string, object?>
        {
            ["shopId"] = string.IsNullOrWhiteSpace(shopId) ? null : shopId.Trim(),
            ["queryStatus"] = "",
            ["sitePublishStatus"] = "",
            ["searchType"] = "msku",
            ["searchValue"] = Array.Empty<object>(),
            ["searchMethod"] = "exact",
            ["groupId"] = "",
            ["migrationPageType"] = "q-all",
            ["listingIdList"] = Array.Empty<object>(),
            ["isGlobal"] = null,
            ["erpStatus"] = "",
            ["platform"] = platformCode,
            ["hasVariation"] = "",
            ["stockAlertType"] = "",
            ["stockAlertNum"] = 0,
            ["categoryId"] = "",
            ["site"] = "",
            ["categoryMap"] = new[] { "" },
            ["lowPrice"] = "",
            ["highPrice"] = "",
            ["createTime"] = Array.Empty<object>(),
            ["createTimeStart"] = "",
            ["createTimeEnd"] = "",
            ["platformCreateTime"] = Array.Empty<object>(),
            ["platformCreateTimeStart"] = "",
            ["platformCreateTimeEnd"] = "",
            ["updateTime"] = Array.Empty<object>(),
            ["updateTimeStart"] = "",
            ["updateTimeEnd"] = "",
            ["mapped"] = ""
        };

        var referer = $"https://www.4seller.com/web/listing/{platformCode}.html?type={platformCode}Active";
        var body = await PostRawAsync(
            userId,
            "/api/listing/get-status-count",
            payload,
            referer,
            cancellationToken,
            ListingJsonOptions);
        return ParseListingStatusCountResponse(body);
    }

    /// <inheritdoc />
    public async Task<FourSellerListingPushResultDto> SaveListingDraftAsync(
        Guid userId,
        string platform,
        object payload,
        CancellationToken cancellationToken = default)
    {
        var platformCode = NormalizeListingPlatform(platform);
        var path = $"/api/listing/{platformCode}/save-draft";
        var referer = BuildListingPageReferer(platformCode, "draft");
        var body = await PostRawAsync(userId, path, payload, referer, cancellationToken, ListingJsonOptions);
        return FourSellerListingPushResponseParser.EnsureAccepted(
            FourSellerListingPushResponseParser.Parse(body));
    }

    /// <inheritdoc />
    public async Task<FourSellerListingPushResultDto> PublishListingAsync(
        Guid userId,
        string platform,
        object payload,
        CancellationToken cancellationToken = default)
    {
        var platformCode = NormalizeListingPlatform(platform);
        var path = $"/api/listing/{platformCode}/publish";
        var referer = BuildListingEditReferer(
            platformCode,
            TryReadPayloadListingId(payload),
            ListingSiteStatuses.Draft);
        var body = await PostRawAsync(userId, path, payload, referer, cancellationToken, ListingJsonOptions);
        return FourSellerListingPushResponseParser.EnsureAccepted(
            FourSellerListingPushResponseParser.Parse(body));
    }

    /// <inheritdoc />
    public async Task<FourSellerListingPushResultDto> UpdateListingAsync(
        Guid userId,
        string platform,
        object payload,
        string? siteStatus = null,
        CancellationToken cancellationToken = default)
    {
        var platformCode = NormalizeListingPlatform(platform);
        var path = $"/api/listing/{platformCode}/update";
        var referer = BuildListingEditReferer(
            platformCode,
            TryReadPayloadListingId(payload),
            siteStatus);
        var body = await PostRawAsync(userId, path, payload, referer, cancellationToken, ListingJsonOptions);
        return FourSellerListingPushResponseParser.EnsureAccepted(
            FourSellerListingPushResponseParser.Parse(body));
    }

    /// <inheritdoc />
    public async Task<FourSellerListingPushResultDto> DeactivateListingsAsync(
        Guid userId,
        string platform,
        IReadOnlyList<long> listingIds,
        CancellationToken cancellationToken = default)
    {
        var ids = (listingIds ?? []).Where(id => id > 0).ToList();
        if (ids.Count == 0)
        {
            throw new BusinessException("Không có listing id hợp lệ để deactivate.");
        }

        var platformCode = NormalizeListingPlatform(platform);
        var path = $"/api/listing/{platformCode}/batch-deactivate";
        // Browser Active list: …/listing/{platform}.html?type={platform}Active + body number[].
        var referer = BuildListingPageReferer(platformCode, "active");
        var body = await PostRawAsync(userId, path, ids, referer, cancellationToken, ListingJsonOptions);
        return FourSellerListingPushResponseParser.EnsureAccepted(
            FourSellerListingPushResponseParser.Parse(body));
    }

    /// <inheritdoc />
    public async Task<FourSellerListingPushResultDto> ActivateListingsAsync(
        Guid userId,
        string platform,
        IReadOnlyList<long> listingIds,
        CancellationToken cancellationToken = default)
    {
        var ids = (listingIds ?? []).Where(id => id > 0).ToList();
        if (ids.Count == 0)
        {
            throw new BusinessException("Không có listing id hợp lệ để activate.");
        }

        var platformCode = NormalizeListingPlatform(platform);
        var path = $"/api/listing/{platformCode}/batch-activate";
        var referer = BuildListingPageReferer(platformCode, "active");
        var body = await PostRawAsync(userId, path, ids, referer, cancellationToken, ListingJsonOptions);
        return FourSellerListingPushResponseParser.EnsureAccepted(
            FourSellerListingPushResponseParser.Parse(body));
    }

    /// <inheritdoc />
    public async Task<FourSellerListingPushResultDto> CopyListingAsync(
        Guid userId,
        string platform,
        long listingId,
        CancellationToken cancellationToken = default)
    {
        if (listingId <= 0)
        {
            throw new BusinessException("listingId không hợp lệ để copy.");
        }

        var platformCode = NormalizeListingPlatform(platform);
        var path =
            $"/api/listing/{platformCode}/copy?listingId={listingId.ToString(CultureInfo.InvariantCulture)}";
        var referer = BuildListingPageReferer(platformCode, "draft");
        var body = await GetRawAsync(userId, path, referer, cancellationToken);
        return FourSellerListingPushResponseParser.EnsureAccepted(
            FourSellerListingPushResponseParser.Parse(body));
    }

    /// <inheritdoc />
    public async Task SyncOneLocalListingAsync(
        Guid userId,
        string platform,
        string productId,
        long shopId,
        CancellationToken cancellationToken = default)
    {
        var trimmedProductId = (productId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(trimmedProductId))
        {
            throw new BusinessException("productId là bắt buộc để sync-one-local-listing.");
        }

        if (shopId <= 0)
        {
            throw new BusinessException("shopId 4Seller không hợp lệ để sync-one-local-listing.");
        }

        var platformCode = NormalizeListingPlatform(platform);
        var path = $"/api/listing/{platformCode}/sync-one-local-listing";
        var referer = BuildListingPageReferer(platformCode, "active");
        // Exact 4Seller body shape: { "productId": "<id>", "shopId": <number> }
        var payload = new Dictionary<string, object>
        {
            ["productId"] = trimmedProductId,
            ["shopId"] = shopId,
        };
        await PostRawAsync(userId, path, payload, referer, cancellationToken, ListingJsonOptions);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<FourSellerMoveSourceListingDto>> BatchGetMoveSourceListingAsync(
        Guid userId,
        IReadOnlyList<long> listingIds,
        CancellationToken cancellationToken = default)
    {
        var ids = (listingIds ?? []).Where(id => id > 0).Distinct().ToList();
        if (ids.Count == 0)
        {
            throw new BusinessException("Không có listing id hợp lệ để lấy move-source.");
        }

        var body = await PostRawAsync(
            userId,
            BatchGetMoveSourceListingPath,
            ids,
            TidyListReferer,
            cancellationToken,
            ListingJsonOptions);
        return ParseMoveSourceListingResponse(body);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<FourSellerRecentUseCategoryDto>> GetRecentUseCategoriesAsync(
        Guid userId,
        long shopId,
        string? site = null,
        CancellationToken cancellationToken = default)
    {
        if (shopId <= 0)
        {
            throw new BusinessException("shopId 4Seller không hợp lệ để lấy recent-use category.");
        }

        var siteCode = string.IsNullOrWhiteSpace(site) ? "US" : site.Trim();
        var path =
            $"{RecentUseCategoryPath}?shopId={shopId}&site={Uri.EscapeDataString(siteCode)}";
        var body = await GetRawAsync(userId, path, TidyListReferer, cancellationToken);
        return ParseRecentUseCategoryResponse(body);
    }

    /// <inheritdoc />
    public async Task BatchBindCategoryAsync(
        Guid userId,
        IReadOnlyList<FourSellerBindCategoryItemDto> items,
        CancellationToken cancellationToken = default)
    {
        var payload = (items ?? [])
            .Where(i => i.ListingId > 0 && i.CategoryId > 0)
            .Select(i => new Dictionary<string, object>
            {
                ["listingId"] = i.ListingId,
                ["categoryId"] = i.CategoryId
            })
            .ToList();

        if (payload.Count == 0)
        {
            throw new BusinessException("Không có cặp listingId/categoryId hợp lệ để bind category.");
        }

        var body = await PostRawAsync(
            userId,
            BatchBingingCategoryPath,
            payload,
            TidyListReferer,
            cancellationToken,
            ListingJsonOptions);

        // Expect { code: 0, data: true }
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        if (root.TryGetProperty("data", out var dataProp) &&
            dataProp.ValueKind == JsonValueKind.False)
        {
            throw new BusinessException("4Seller bind category trả về data=false.");
        }
    }

    /// <summary>Builds the 4Seller listing edit-page referer (<c>.../edit/{id}.html?status=…</c>).</summary>
    private static string BuildListingEditReferer(
        string platform,
        long? listingId,
        string? siteStatus = null)
    {
        var status = string.IsNullOrWhiteSpace(siteStatus)
            ? ListingSiteStatuses.Active
            : ListingSiteStatuses.ToFourSellerPageStatus(siteStatus);
        return listingId is > 0
            ? $"https://www.4seller.com/web/listing/{platform}/edit/{listingId.Value}.html?status={status}"
            : ListingEditReferer;
    }

    /// <summary>Best-effort extract of the listing <c>id</c> from the push payload (JsonElement).</summary>
    private static long? TryReadPayloadListingId(object payload)
    {
        if (payload is JsonElement el &&
            el.ValueKind == JsonValueKind.Object &&
            el.TryGetProperty("id", out var idEl))
        {
            if (idEl.ValueKind == JsonValueKind.Number && idEl.TryGetInt64(out var num))
            {
                return num;
            }

            if (idEl.ValueKind == JsonValueKind.String &&
                long.TryParse(idEl.GetString(), out var parsed))
            {
                return parsed;
            }
        }

        return null;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<FourSellerBrandDto>> GetBrandListByShopIdAsync(
        Guid userId,
        long shopId,
        CancellationToken cancellationToken = default)
    {
        if (shopId <= 0)
        {
            throw new BusinessException("ShopId 4Seller không hợp lệ.");
        }

        var path = $"{BrandListByShopPath}?shopId={shopId}";
        var body = await GetRawAsync(userId, path, ListingReferer, cancellationToken);
        return ParseBrandListResponse(body);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<FourSellerBrandDto>> SuggestBrandsByShopIdAsync(
        Guid userId,
        long shopId,
        string brandName,
        CancellationToken cancellationToken = default)
    {
        if (shopId <= 0)
        {
            throw new BusinessException("ShopId 4Seller không hợp lệ.");
        }

        var query = (brandName ?? string.Empty).Trim();
        if (query.Length == 0)
        {
            return [];
        }

        var path =
            $"{BrandSuggestByShopPath}?shopId={shopId}&brandName={Uri.EscapeDataString(query)}";
        var body = await GetRawAsync(userId, path, ListingReferer, cancellationToken);
        return ParseBrandListResponse(body);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<FourSellerWarehouseDto>> GetWarehouseListByShopIdAsync(
        Guid userId,
        long shopId,
        int fbt = 0,
        string? allocationMode = null,
        CancellationToken cancellationToken = default)
    {
        if (shopId <= 0)
        {
            throw new BusinessException("ShopId 4Seller không hợp lệ.");
        }

        var mode = allocationMode ?? string.Empty;
        var path =
            $"{WarehouseListByShopPath}?shopId={shopId}&fbt={fbt}&allocationMode={Uri.EscapeDataString(mode)}";
        var body = await GetRawAsync(userId, path, ListingReferer, cancellationToken);
        return ParseWarehouseListResponse(body);
    }

    /// <inheritdoc />
    public async Task<(IReadOnlyList<FourSellerBrandDto> Brands, IReadOnlyList<FourSellerWarehouseDto> Warehouses)>
        GetBrandAndWarehouseListsByShopIdAsync(
            Guid userId,
            long shopId,
            int fbt = 0,
            string? allocationMode = null,
            CancellationToken cancellationToken = default)
    {
        if (shopId <= 0)
        {
            throw new BusinessException("ShopId 4Seller không hợp lệ.");
        }

        // One cookie / is-login check, then parallel outbound HTTP (no shared EF after this).
        var cookie = await EnsureFourSellerLoggedInAsync(userId, cancellationToken);
        var mode = allocationMode ?? string.Empty;
        var brandPath = $"{BrandListByShopPath}?shopId={shopId}";
        var warehousePath =
            $"{WarehouseListByShopPath}?shopId={shopId}&fbt={fbt}&allocationMode={Uri.EscapeDataString(mode)}";

        var brandTask = GetRawWithCookieAsync(cookie, brandPath, ListingReferer, cancellationToken);
        var warehouseTask = GetRawWithCookieAsync(cookie, warehousePath, ListingReferer, cancellationToken);
        await Task.WhenAll(brandTask, warehouseTask);

        return (
            ParseBrandListResponse(await brandTask),
            ParseWarehouseListResponse(await warehouseTask));
    }

    /// <inheritdoc />
    public async Task<FourSellerOrderPageDto> GetOrderPageAsync(
        Guid userId,
        FourSellerOrderPageRequest request,
        CancellationToken cancellationToken = default)
    {
        var pageCurrent = request.PageCurrent < 1 ? 1 : request.PageCurrent;
        var pageSize = request.PageSize < 1 ? 100 : Math.Min(request.PageSize, 100);
        var orderStatus = string.IsNullOrWhiteSpace(request.OrderStatus)
            ? "to_ship"
            : request.OrderStatus.Trim();
        // 4Seller API expects American spelling.
        if (string.Equals(orderStatus, "cancelled", StringComparison.OrdinalIgnoreCase))
        {
            orderStatus = "canceled";
        }

        // 4Seller date filters are AND across filled pairs. Filter only by Create date:
        // every order has a create time regardless of status, whereas Paid/Ship/PrintLabel/Cancel
        // are empty for pending/unpaid orders and would exclude brand-new orders from the sync.
        // Request DateFrom/DateTo are UTC (VN business day bounds); 4Seller dateQueryCreate*
        // expects America/Los_Angeles wall-clock for the same absolute window.
        var hasDateRange = request.DateFrom.HasValue && request.DateTo.HasValue;
        var dateStart = string.Empty;
        var dateEnd = string.Empty;
        if (hasDateRange)
        {
            var (localFrom, localTo) = FourSellerOrderDateTime.ToPacificLocalRange(
                request.DateFrom!.Value,
                request.DateTo!.Value);
            dateStart = FormatFourSellerDateTimeNaive(localFrom);
            dateEnd = FormatFourSellerDateTimeNaive(localTo);
        }

        var payload = new Dictionary<string, object?>
        {
            ["pageCurrent"] = pageCurrent,
            ["pageSize"] = pageSize,
            ["orderStatus"] = orderStatus,
            ["platform"] = request.Platform?.Trim() ?? string.Empty,
            ["shopIdList"] = Array.Empty<object>(),
            ["warehouseId"] = "",
            ["logisticsServiceId"] = "",
            ["itemType"] = "",
            ["printStatus"] = "",
            ["shippingMethod"] = Array.Empty<object>(),
            ["searchType"] = string.IsNullOrWhiteSpace(request.SearchType) ? "orderNo" : request.SearchType.Trim(),
            ["searchMethod"] = string.IsNullOrWhiteSpace(request.SearchMethod) ? "exact" : request.SearchMethod.Trim(),
            ["searchValue"] = request.SearchValue?.Trim() ?? string.Empty,
            ["noteQuery"] = "",
            ["itemMin"] = "",
            ["itemMax"] = "",
            ["weightMin"] = "",
            ["weightMax"] = "",
            ["amountMax"] = "",
            ["amountMin"] = "",
            ["weightUnit"] = "oz",
            ["lengthMin"] = "",
            ["lengthMax"] = "",
            ["widthMin"] = "",
            ["widthMax"] = "",
            ["heightMin"] = "",
            ["heightMax"] = "",
            ["dimensionUnit"] = "in",
            ["expiresHour"] = "",
            ["hours"] = "",
            ["bundled"] = "",
            ["platformStatus"] = "",
            ["allOrderStatus"] = "",
            ["exceptionType"] = "",
            ["shipFromAddressId"] = "",
            ["countryCode"] = "",
            ["countryName"] = "",
            ["country"] = "",
            ["state"] = "",
            ["buyerNote"] = "",
            ["sellerNote"] = "",
            ["internalNote"] = "",
            ["tagQuery"] = "",
            ["platformMarker"] = "",
            ["dateQueryCreateStartTime"] = dateStart,
            ["dateQueryCreateEndTime"] = dateEnd,
            ["dateQueryPaidStartTime"] = "",
            ["dateQueryPaidEndTime"] = "",
            ["dateQueryShipStartTime"] = "",
            ["dateQueryShipEndTime"] = "",
            ["dateQueryPrintLabelStartTime"] = "",
            ["dateQueryPrintLabelEndTime"] = "",
            ["dateQueryCancelStartTime"] = "",
            ["dateQueryCancelEndTime"] = "",
            ["logisticsServiceMap"] = new[] { "" },
            ["tagIdList"] = Array.Empty<object>(),
            ["orderBy"] = string.IsNullOrWhiteSpace(request.OrderBy) ? "orderPaidTime" : request.OrderBy.Trim(),
            ["desc"] = string.IsNullOrWhiteSpace(request.Desc) ? "desc" : request.Desc.Trim()
        };

        var refererType = orderStatus.ToLowerInvariant() switch
        {
            "all" => "allOrder",
            "pending" => "pending",
            "on_hold" => "onHold",
            "to_ship" => "toShip",
            "in_process" => "inProcess",
            "shipped" => "shipped",
            "canceled" => "canceled",
            _ => "toShip"
        };
        var referer = $"https://www.4seller.com/web/order2.html?type={refererType}";
        var body = await PostRawAsync(userId, OrderPagePath, payload, referer, cancellationToken, ListingJsonOptions);
        return ParseOrderPageResponse(body, pageCurrent, pageSize);
    }

    /// <inheritdoc />
    public async Task TriggerTikTokReturnSyncAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        // Non-zero code / success=false already throws inside GetRawAsync; the body is just `true`.
        await GetRawAsync(userId, TikTokReturnSyncPath, AfterSalesReferer, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<FourSellerReturnPageDto> GetTikTokReturnPageAsync(
        Guid userId,
        int pageCurrent,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var page = pageCurrent < 1 ? 1 : pageCurrent;
        var size = pageSize < 1 ? 100 : Math.Min(pageSize, 100);

        var payload = new Dictionary<string, object?>
        {
            ["pageCurrent"] = page,
            ["pageSize"] = size,
            // Sorting by last change (not creation) is what makes an incremental pull possible: a
            // case created weeks ago still surfaces the day the buyer ships the item back.
            ["orderBy"] = "platformUpdateTime",
            ["desc"] = "desc",
            ["status"] = ""
        };

        var body = await PostRawAsync(userId, TikTokReturnPagePath, payload, AfterSalesReferer, cancellationToken);
        return ParseReturnPageResponse(body, page, size);
    }

    /// <inheritdoc />
    public async Task<FourSellerOrderAmountDetailDto> GetOrderDetailAsync(
        Guid userId,
        long fourSellerOrderId,
        CancellationToken cancellationToken = default)
    {
        if (fourSellerOrderId <= 0)
        {
            throw new BusinessException("fourSellerOrderId không hợp lệ.");
        }

        var path = $"{OrderDetailPath}?id={fourSellerOrderId.ToString(CultureInfo.InvariantCulture)}";
        var body = await GetRawAsync(userId, path, OrderAllReferer, cancellationToken);
        return ParseOrderAmountDetailResponse(body, fourSellerOrderId);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<long, FourSellerOrderAmountDetailDto>> GetOrderDetailsAsync(
        Guid userId,
        IReadOnlyList<long> fourSellerOrderIds,
        int maxConcurrency = 2,
        CancellationToken cancellationToken = default)
    {
        var ids = fourSellerOrderIds
            .Where(id => id > 0)
            .Distinct()
            .ToList();
        if (ids.Count == 0)
        {
            return new Dictionary<long, FourSellerOrderAmountDetailDto>();
        }

        // Cookie + proxy resolve once on this thread (scoped DbContext is not thread-safe).
        var cookie = await EnsureFourSellerLoggedInAsync(userId, cancellationToken);
        var concurrency = Math.Clamp(maxConcurrency, 1, 4);
        var fetched = new ConcurrentDictionary<long, FourSellerOrderAmountDetailDto>();

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = linkedCts.Token;

        try
        {
            await Parallel.ForEachAsync(
                ids,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = concurrency,
                    CancellationToken = token
                },
                async (orderId, ct) =>
                {
                    try
                    {
                        var path = $"{OrderDetailPath}?id={orderId.ToString(CultureInfo.InvariantCulture)}";
                        var body = await GetRawWithCookieAsync(cookie, path, OrderAllReferer, ct);
                        fetched[orderId] = ParseOrderAmountDetailResponse(body, orderId);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (FourSellerBlockedException)
                    {
                        // Stop remaining GETs — same IP/proxy will keep failing.
                        linkedCts.Cancel();
                        throw;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(
                            ex,
                            "4Seller order detail failed for id={OrderId}",
                            orderId);
                    }
                });
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Linked cancel from FourSellerBlockedException — return what we have.
        }
        catch (FourSellerBlockedException ex)
        {
            _logger.LogWarning(
                ex,
                "4Seller blocked during order-detail batch; returning {Count} successful details",
                fetched.Count);
        }

        return fetched;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<FourSellerShippingServiceDto>> ListPlatformShippingServicesAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var body = await GetRawAsync(userId, ListPlatformShippingServicePath, OrderToShipReferer, cancellationToken);
        return ParseShippingServicesResponse(body);
    }

    /// <inheritdoc />
    public async Task<FourSellerProductDiscountPageDto> GetProductDiscountActivityPageAsync(
        Guid userId,
        FourSellerProductDiscountPageRequest request,
        CancellationToken cancellationToken = default)
    {
        var pageCurrent = request.PageCurrent < 1 ? 1 : request.PageCurrent;
        var pageSize = request.PageSize < 1 ? 100 : Math.Min(request.PageSize, 100);
        var discountType = request.DiscountType?.Trim() ?? string.Empty;

        var payload = new Dictionary<string, object?>
        {
            ["pageCurrent"] = pageCurrent,
            ["pageSize"] = pageSize,
            ["discountType"] = discountType,
            ["shopId"] = request.ShopId?.Trim() ?? string.Empty,
            ["promotionStatus"] = request.PromotionStatus?.Trim() ?? string.Empty,
            ["searchType"] = string.IsNullOrWhiteSpace(request.SearchType)
                ? "activityName"
                : request.SearchType.Trim(),
            ["searchValue"] = request.SearchValue?.Trim() ?? string.Empty
        };

        var referer = ResolvePromotionActivityReferer(discountType);
        var body = await PostRawAsync(
            userId,
            ProductDiscountActivityPagePath,
            payload,
            referer,
            cancellationToken);

        return ParseProductDiscountPageResponse(body, pageCurrent, pageSize);
    }

    /// <inheritdoc />
    public async Task DeactivateProductDiscountActivityAsync(
        Guid userId,
        long activityId,
        string? discountType = null,
        CancellationToken cancellationToken = default)
    {
        if (activityId <= 0)
        {
            throw new BusinessException("Activity id 4Seller không hợp lệ.");
        }

        var path = $"{ProductDiscountDeactivatePath}?id={activityId}";
        var referer = ResolvePromotionActivityReferer(discountType);
        _ = await GetRawAsync(userId, path, referer, cancellationToken);
    }

    private static string ResolvePromotionActivityReferer(string? discountType) =>
        string.Equals(discountType?.Trim(), FlashSaleDiscountType, StringComparison.OrdinalIgnoreCase)
            ? FlashDealReferer
            : ProductDiscountReferer;

    private static FourSellerProductDiscountPageDto ParseProductDiscountPageResponse(
        string body,
        int pageCurrent,
        int pageSize)
    {
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;

        if (root.TryGetProperty("code", out var codeEl) &&
            codeEl.ValueKind == JsonValueKind.Number &&
            codeEl.TryGetInt32(out var code) &&
            code != 0)
        {
            var messages = root.TryGetProperty("messages", out var msgEl)
                ? msgEl.GetString()
                : null;
            throw new BusinessException(
                string.IsNullOrWhiteSpace(messages)
                    ? $"4Seller product-discount page failed (code={code})."
                    : messages);
        }

        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
        {
            return new FourSellerProductDiscountPageDto
            {
                PageCurrent = pageCurrent,
                PageSize = pageSize
            };
        }

        var total = data.TryGetProperty("total", out var totalEl) && totalEl.TryGetInt32(out var t) ? t : 0;
        var page = new FourSellerProductDiscountPageDto
        {
            PageCurrent = data.TryGetProperty("pageCurrent", out var pcEl) && pcEl.TryGetInt32(out var pc)
                ? pc
                : pageCurrent,
            PageSize = data.TryGetProperty("pageSize", out var psEl) && psEl.TryGetInt32(out var ps)
                ? ps
                : pageSize,
            Total = total
        };

        if (!data.TryGetProperty("records", out var records) || records.ValueKind != JsonValueKind.Array)
        {
            return page;
        }

        foreach (var item in records.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            page.Records.Add(new FourSellerProductDiscountRecordDto
            {
                Id = TryReadLong(item, out var id, "id") ? id : 0,
                ShopId = TryReadLong(item, out var shopId, "shopId") ? shopId : 0,
                ShopName = ReadString(item, "shopName") ?? string.Empty,
                PlatformActivityId = ReadString(item, "platformActivityId") ?? string.Empty,
                ActivityName = ReadString(item, "activityName") ?? string.Empty,
                DiscountType = ReadString(item, "discountType") ?? string.Empty,
                BeginTime = TryReadLong(item, out var begin, "beginTime") ? begin : 0,
                EndTime = TryReadLong(item, out var end, "endTime") ? end : 0,
                PromotionStatus = ReadString(item, "promotionStatus") ?? string.Empty,
                ProductLevel = ReadString(item, "productLevel") ?? string.Empty,
                ProductCount = TryReadInt(item, out var productCount, "productCount") ? productCount : 0,
                ProductFailCount = TryReadInt(item, out var failCount, "productFailCount") ? failCount : 0,
                ErrorMessage = ReadString(item, "errorMessage")
            });
        }

        return page;
    }

    /// <inheritdoc />
    public async Task<string> MarkShipAsync(
        Guid userId,
        IReadOnlyList<FourSellerMarkShipItemDto> items,
        CancellationToken cancellationToken = default)
    {
        if (items.Count == 0)
        {
            throw new BusinessException("Chọn ít nhất một đơn để Mark Shipped.");
        }

        var payload = items.Select(i => new Dictionary<string, object?>
        {
            ["id"] = i.Id,
            ["platform"] = i.Platform,
            ["shopId"] = i.ShopId,
            ["shopName"] = i.ShopName ?? string.Empty,
            ["platformOrderId"] = i.PlatformOrderId ?? string.Empty,
            ["platformOrderNo"] = i.PlatformOrderNo ?? i.PlatformOrderId ?? string.Empty,
            ["shippingService"] = i.ShippingService,
            ["trackingNo"] = i.TrackingNo,
            ["trackingUrl"] = i.TrackingUrl ?? string.Empty
        }).ToList();

        var body = await PostRawAsync(userId, ToShipMarkShipPath, payload, OrderToShipReferer, cancellationToken);
        return ParseMarkShipRequestId(body);
    }

    /// <inheritdoc />
    public async Task<FourSellerAsyncShipOrderResultDto> GetAsyncShipOrderResultAsync(
        Guid userId,
        string requestId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(requestId))
        {
            throw new BusinessException("requestId Mark Shipped là bắt buộc.");
        }

        var path = $"{GetAsyncShipOrderResultPath}?requestId={Uri.EscapeDataString(requestId.Trim())}";
        var body = await GetRawAsync(userId, path, OrderToShipReferer, cancellationToken);
        return FourSellerAsyncShipOrderResultParser.Parse(body);
    }

    /// <inheritdoc />
    public async Task UpdateOrderNoteAsync(
        Guid userId,
        FourSellerUpdateOrderNoteDto request,
        CancellationToken cancellationToken = default)
    {
        if (request.OrderIdList.Count == 0)
        {
            throw new BusinessException("Chọn ít nhất một đơn để cập nhật ghi chú.");
        }

        var payload = new Dictionary<string, object?>
        {
            ["orderIdList"] = request.OrderIdList.ToList(),
            ["buyerMemo"] = request.BuyerMemo ?? string.Empty,
            ["sellerMemo"] = request.SellerMemo ?? string.Empty,
            ["internalMemo"] = request.InternalMemo ?? string.Empty,
            ["tagIdList"] = Array.Empty<object>(),
            ["updateBuyerMemo"] = true,
            ["updateSellerMemo"] = true,
            ["updateInternalMemo"] = true,
            ["updateTag"] = true
        };

        await PostRawAsync(userId, UpdateOrderNotePath, payload, OrderShippedReferer, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<FourSellerCosUploadResultDto> UploadCosAsync(
        Guid userId,
        Stream content,
        string fileName,
        string contentType,
        string referenceType = "tiktok",
        CancellationToken cancellationToken = default)
    {
        if (content is null)
        {
            throw new BusinessException("Thiếu nội dung file upload.");
        }

        var safeName = string.IsNullOrWhiteSpace(fileName) ? "upload.bin" : fileName.Trim();
        var safeType = string.IsNullOrWhiteSpace(contentType)
            ? "application/octet-stream"
            : contentType.Trim();
        var safeRef = string.IsNullOrWhiteSpace(referenceType)
            ? "tiktok"
            : referenceType.Trim().ToLowerInvariant();

        await using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        var bytes = buffer.ToArray();
        if (bytes.Length == 0)
        {
            throw new BusinessException("File upload trống.");
        }

        var md5Hex = Convert.ToHexString(MD5.HashData(bytes)).ToLowerInvariant();

        var signBody = await PostRawAsync(
            userId,
            "/api/cos/get-sign",
            new
            {
                md5 = md5Hex,
                fileName = safeName,
                referenceType = safeRef
            },
            ListingReferer,
            cancellationToken);

        using var signDoc = JsonDocument.Parse(signBody);
        var data = signDoc.RootElement.TryGetProperty("data", out var dataEl)
            ? dataEl
            : signDoc.RootElement;

        var key = data.TryGetProperty("key", out var keyEl) ? keyEl.GetString() : null;
        var domain = data.TryGetProperty("domain", out var domainEl) ? domainEl.GetString() : null;
        var sign = data.TryGetProperty("sign", out var signEl) ? signEl.GetString() : null;
        var sessionToken = data.TryGetProperty("sessionToken", out var tokenEl)
            ? tokenEl.GetString()
            : null;
        var operation = data.TryGetProperty("operation", out var opEl) ? opEl.GetString() : null;
        long? localMaterialId = null;
        if (data.TryGetProperty("localMaterialId", out var matEl) &&
            matEl.ValueKind == JsonValueKind.Number &&
            matEl.TryGetInt64(out var matId))
        {
            localMaterialId = matId;
        }

        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(domain))
        {
            throw new BusinessException(
                $"4Seller get-sign thiếu key/domain: {Truncate(signBody, 200)}");
        }

        var normalizedKey = key.StartsWith('/') ? key : "/" + key;
        var publicUrl = $"https://{domain.Trim().TrimEnd('/')}{normalizedKey}";

        // Skip PUT when object already exists (no credentials) — URL is still valid.
        var needsPut = !string.IsNullOrWhiteSpace(sign) &&
                       !string.IsNullOrWhiteSpace(sessionToken) &&
                       !string.Equals(operation, "exist", StringComparison.OrdinalIgnoreCase);

        long putMs = 0;
        if (needsPut)
        {
            var swPut = System.Diagnostics.Stopwatch.StartNew();
            using var putRequest = new HttpRequestMessage(HttpMethod.Put, publicUrl);
            putRequest.Headers.TryAddWithoutValidation("Authorization", sign);
            putRequest.Headers.TryAddWithoutValidation("x-cos-security-token", sessionToken);
            putRequest.Content = new ByteArrayContent(bytes);
            putRequest.Content.Headers.ContentType =
                new System.Net.Http.Headers.MediaTypeHeaderValue(safeType);

            var putResponse = await _clientProvider.GetClient(null).SendAsync(putRequest, cancellationToken);
            putMs = swPut.ElapsedMilliseconds;
            if (!putResponse.IsSuccessStatusCode)
            {
                var putBody = await putResponse.Content.ReadAsStringAsync(cancellationToken);
                throw new BusinessException(
                    $"Upload COS thất bại (HTTP {(int)putResponse.StatusCode}): {Truncate(putBody, 200)}");
            }
        }

        return new FourSellerCosUploadResultDto
        {
            Url = publicUrl,
            Key = normalizedKey,
            Domain = domain.Trim(),
            LocalMaterialId = localMaterialId,
            Md5 = md5Hex,
            Operation = operation,
            FileName = safeName
        };
    }

    private static string NormalizeListingPlatform(string platform)
    {
        var code = (platform ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new BusinessException("Platform listing là bắt buộc (vd. tiktok).");
        }

        return code;
    }

    private static string BuildListingPageReferer(string platform, string status)
    {
        var normalized = ListingSiteStatuses.ToFourSellerPageStatus(status);
        var type = normalized.Equals("draft", StringComparison.OrdinalIgnoreCase)
            ? $"{platform}Draft"
            : normalized.Equals("active", StringComparison.OrdinalIgnoreCase)
                ? $"{platform}Active"
                : $"{platform}{char.ToUpperInvariant(normalized[0])}{normalized[1..]}";
        return $"https://www.4seller.com/web/listing/{platform}.html?type={type}";
    }

    /// <inheritdoc />
    public FourSellerShopCheckResponse MatchShopAgainstList(
        FourSellerShopCheckRequest request,
        IReadOnlyList<FourSellerShopRecordDto> fourSellerShops)
    {
        var platformCode = request.PlatformCode.Trim();
        var region = string.IsNullOrWhiteSpace(request.Region) ? "US" : request.Region.Trim();

        var candidates = fourSellerShops
            .Where(r => PlatformMatches(r.Platform, platformCode))
            .Where(r => string.IsNullOrWhiteSpace(region) ||
                        string.Equals(r.MarketplaceId, region, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var match = FindMatch(request, candidates);
        if (match is null)
        {
            return new FourSellerShopCheckResponse
            {
                IsConnected = false
            };
        }

        var isConnected = string.Equals(match.Record.StatusDesc, "Connected", StringComparison.OrdinalIgnoreCase);

        return new FourSellerShopCheckResponse
        {
            IsConnected = isConnected,
            MatchedBy = match.MatchedBy,
            MatchedShop = match.Record
        };
    }

    private async Task<T> PostAsync<T>(
        Guid userId,
        string path,
        object payload,
        string referer,
        CancellationToken cancellationToken)
    {
        var body = await PostRawAsync(userId, path, payload, referer, cancellationToken);

        var apiResponse = JsonSerializer.Deserialize<T>(body, JsonOptions);
        if (apiResponse is FourSellerApiResponseBase { Success: false } failed)
        {
            throw new BusinessException($"4Seller API thất bại: {failed.Messages ?? Truncate(body, 200)}");
        }

        if (apiResponse is FourSellerApiResponseBase baseResponse &&
            (baseResponse.Code != 0 || !baseResponse.Success))
        {
            throw new BusinessException($"4Seller API thất bại: {baseResponse.Messages ?? Truncate(body, 200)}");
        }

        if (apiResponse is null)
        {
            throw new BusinessException($"4Seller API trả về dữ liệu không hợp lệ: {Truncate(body, 200)}");
        }

        return apiResponse;
    }

    /// <summary>
    /// Validates the user's 4Seller session via GET /api/user/is-login before outbound API calls.
    /// Returns the resolved cookie so callers can reuse it without a second DB read.
    /// Only latches <c>expired</c> after a confirmed (double) probe; transient failures throw
    /// <see cref="FourSellerUnavailableException"/> without changing DB status.
    /// </summary>
    private async Task<string> EnsureFourSellerLoggedInAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (_sessionValidatedUserId == userId && !string.IsNullOrEmpty(_sessionValidatedCookie))
        {
            return _sessionValidatedCookie;
        }

        var cookie = await ResolveCookieAsync(userId, cancellationToken, requireLoggedInStatus: true);

        // Process-wide cache: skip is-login when this cookie was validated recently.
        if (_sessionCache.TryGetValid(userId, cookie))
        {
            _sessionValidatedUserId = userId;
            _sessionValidatedCookie = cookie;
            return cookie;
        }

        var (state, detail) = await ProbeIsLoginAsync(cookie, cancellationToken);
        if (state == FourSellerSessionState.LoggedIn)
        {
            _sessionCache.MarkValid(userId, cookie);
            _sessionValidatedUserId = userId;
            _sessionValidatedCookie = cookie;
            return cookie;
        }

        if (state == FourSellerSessionState.Unverified)
        {
            _sessionValidatedUserId = null;
            _sessionValidatedCookie = null;
            throw new FourSellerUnavailableException(detail, _activeProxyUrl);
        }

        // Expired — confirm with a second probe so a single flaky response does not latch.
        try
        {
            await Task.Delay(ExpireConfirmDelay, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }

        var (confirmState, confirmDetail) = await ProbeIsLoginAsync(cookie, cancellationToken);
        if (confirmState == FourSellerSessionState.LoggedIn)
        {
            _logger.LogInformation(
                "4Seller is-login expired on first probe but LoggedIn on confirm (seller {OwnerId})",
                userId);
            _sessionCache.MarkValid(userId, cookie);
            _sessionValidatedUserId = userId;
            _sessionValidatedCookie = cookie;
            return cookie;
        }

        if (confirmState == FourSellerSessionState.Unverified)
        {
            _sessionValidatedUserId = null;
            _sessionValidatedCookie = null;
            throw new FourSellerUnavailableException(confirmDetail, _activeProxyUrl);
        }

        _sessionValidatedUserId = null;
        _sessionValidatedCookie = null;
        _sessionCache.Invalidate(userId);
        await PersistCookieLoginStatusAsync(userId, FourSellerCookieLoginStatuses.Expired, cancellationToken);
        throw new BusinessException(confirmDetail);
    }

    /// <summary>
    /// Calls 4Seller is-login and classifies the result as LoggedIn / Expired / Unverified.
    /// Network / WAF failures surface as Unverified (or rethrow Blocked for 567).
    /// </summary>
    private async Task<(FourSellerSessionState State, string Detail)> ProbeIsLoginAsync(
        string cookie,
        CancellationToken cancellationToken)
    {
        using var httpRequest = new HttpRequestMessage(HttpMethod.Get, IsLoginPath);
        httpRequest.Headers.TryAddWithoutValidation("Accept", "*/*");
        httpRequest.Headers.TryAddWithoutValidation("Cache-Control", "no-cache");
        httpRequest.Headers.TryAddWithoutValidation("Pragma", "no-cache");
        httpRequest.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
        httpRequest.Headers.Add("Cookie", cookie);

        HttpResponseMessage response;
        try
        {
            response = await SendFourSellerAsync(httpRequest, cancellationToken);
        }
        catch (FourSellerBlockedException)
        {
            throw;
        }
        catch (FourSellerUnavailableException ex)
        {
            return (FourSellerSessionState.Unverified, ex.Message);
        }
        catch (Exception ex) when (IsProxyConnectFailure(ex) || ex is HttpRequestException or TaskCanceledException)
        {
            var networkDetail =
                $"Không xác minh được phiên 4Seller (lỗi mạng/proxy): {ex.Message}. " +
                "Đây KHÔNG phải cookie hết hạn.";
            LogProbeFailure(FourSellerSessionState.Unverified, statusCode: null, body: null, networkDetail);
            return (FourSellerSessionState.Unverified, networkDetail);
        }

        string body;
        int statusCode;
        using (response)
        {
            statusCode = (int)response.StatusCode;
            body = await response.Content.ReadAsStringAsync(cancellationToken);
        }

        var state = FourSellerSessionProbe.Classify(statusCode, body, out var classifyDetail);
        if (state != FourSellerSessionState.LoggedIn)
        {
            LogProbeFailure(state, statusCode, body, classifyDetail);
        }

        return (state, classifyDetail);
    }

    private void LogProbeFailure(
        FourSellerSessionState state,
        int? statusCode,
        string? body,
        string detail)
    {
        var code = string.Empty;
        var messages = string.Empty;
        if (!string.IsNullOrWhiteSpace(body))
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;
                if (root.TryGetProperty("code", out var codeProp) && codeProp.ValueKind == JsonValueKind.Number)
                {
                    code = codeProp.GetInt32().ToString(CultureInfo.InvariantCulture);
                }

                if (root.TryGetProperty("messages", out var msgProp))
                {
                    messages = msgProp.ValueKind == JsonValueKind.String
                        ? msgProp.GetString() ?? string.Empty
                        : msgProp.ToString();
                }
            }
            catch (JsonException)
            {
                // body already truncated below
            }
        }

        _logger.LogWarning(
            "4Seller is-login {State} (seller {OwnerId}, via {Via}, HTTP {Status}, code={Code}, messages={Messages}): {Detail} | body={Body}",
            state,
            _servingUserId,
            ProxyLabel(_activeProxyUrl),
            statusCode,
            code,
            Truncate(messages, 80),
            detail,
            Truncate(body ?? string.Empty, 160));
    }

    private async Task<string> ResolveCookieAsync(
        Guid userId,
        CancellationToken cancellationToken,
        bool requireLoggedInStatus)
    {
        _servingUserId = userId;

        // Always read latest from DB — in-memory cache kept expired cookie after user update.
        var row = await _dbContext.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.FourSellerCookie, u.FourSellerCookieLoginStatus, u.FourSellerProxy })
            .FirstOrDefaultAsync(cancellationToken);

        if (row is null || string.IsNullOrWhiteSpace(row.FourSellerCookie))
        {
            throw new BusinessException(
                "Chưa cấu hình cookie 4Seller cho tài khoản này. Cập nhật tại Hồ sơ cá nhân.");
        }

        // Resolve the egress proxy for this seller (sticky absolute) for all subsequent sends.
        _activeProxyIsSticky = !string.IsNullOrWhiteSpace(row.FourSellerProxy);
        _activeProxyUrl = await _proxyManager.ResolveAsync(userId, row.FourSellerProxy, cancellationToken);

        if (requireLoggedInStatus &&
            !string.Equals(
                row.FourSellerCookieLoginStatus,
                FourSellerCookieLoginStatuses.LoggedIn,
                StringComparison.Ordinal))
        {
            throw new BusinessException(
                string.Equals(
                    row.FourSellerCookieLoginStatus,
                    FourSellerCookieLoginStatuses.Expired,
                    StringComparison.Ordinal)
                    ? "Cookie 4Seller đã hết hạn. Chạy Check login hoặc cập nhật cookie tại Hồ sơ cá nhân."
                    : "Cookie 4Seller chưa được xác nhận đăng nhập. Chạy Check login trước khi gọi 4Seller.");
        }

        var trimmed = row.FourSellerCookie.Trim();
        if (!CookieHasUserToken(trimmed))
        {
            throw new BusinessException(
                "Cookie 4Seller thiếu userToken hoặc không hợp lệ. Đăng nhập 4Seller trên trình duyệt rồi copy cookie đầy đủ vào Hồ sơ cá nhân.");
        }

        return trimmed;
    }

    private async Task PersistCookieLoginStatusAsync(
        Guid userId,
        string status,
        CancellationToken cancellationToken)
    {
        var user = await _dbContext.Users
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null || user.FourSellerCookieLoginStatus == status)
        {
            return;
        }

        user.FourSellerCookieLoginStatus = status;
        user.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);

        if (string.Equals(status, FourSellerCookieLoginStatuses.Expired, StringComparison.Ordinal) ||
            string.Equals(status, FourSellerCookieLoginStatuses.None, StringComparison.Ordinal) ||
            string.Equals(status, FourSellerCookieLoginStatuses.Unchecked, StringComparison.Ordinal))
        {
            _sessionCache.Invalidate(userId);
        }
    }

    private static bool CookieHasUserToken(string cookie)
    {
        // userToken=<non-empty>
        const string key = "userToken=";
        var idx = cookie.IndexOf(key, StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
        {
            return false;
        }

        var start = idx + key.Length;
        var end = cookie.IndexOf(';', start);
        var value = end < 0 ? cookie[start..] : cookie[start..end];
        return !string.IsNullOrWhiteSpace(value);
    }

    /// <summary>
    /// Crawl JSON POST: browser-like headers (Accept, Origin, Referer, UA) + Cookie + Content-Type.
    /// </summary>
    private async Task<string> PostCrawlJsonAsync(
        Guid userId,
        string path,
        object payload,
        CancellationToken cancellationToken)
    {
        var cookie = await EnsureFourSellerLoggedInAsync(userId, cancellationToken);

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, path);
        httpRequest.Headers.TryAddWithoutValidation("Accept", "application/json, text/plain, */*");
        httpRequest.Headers.TryAddWithoutValidation("Accept-Language", "vi,en-US;q=0.9,en;q=0.8");
        httpRequest.Headers.TryAddWithoutValidation("Origin", Origin);
        httpRequest.Headers.TryAddWithoutValidation("Referer", ScrapeReferer);
        httpRequest.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
        httpRequest.Headers.Add("Cookie", cookie);
        httpRequest.Content = new StringContent(
            JsonSerializer.Serialize(payload, JsonOptions),
            Encoding.UTF8,
            "application/json");

        var response = await SendFourSellerAsync(httpRequest, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new BusinessException(
                $"4Seller API trả về lỗi ({(int)response.StatusCode}): {Truncate(body, 200)}");
        }

        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        if (root.TryGetProperty("success", out var successProp) &&
            successProp.ValueKind == JsonValueKind.False)
        {
            var messages = ReadApiErrorMessage(root) ?? Truncate(body, 200);
            throw new BusinessException($"4Seller API thất bại: {messages}");
        }

        if (root.TryGetProperty("code", out var codeProp) &&
            codeProp.ValueKind == JsonValueKind.Number &&
            codeProp.GetInt32() != 0)
        {
            var messages = ReadApiErrorMessage(root) ?? Truncate(body, 200);
            throw new BusinessException($"4Seller API thất bại: {messages}");
        }

        return body;
    }

    /// <summary>
    /// Crawl GET: browser-like headers + Cookie.
    /// </summary>
    private async Task<string> GetCrawlAsync(Guid userId, string path, CancellationToken cancellationToken)
    {
        var cookie = await EnsureFourSellerLoggedInAsync(userId, cancellationToken);

        using var httpRequest = new HttpRequestMessage(HttpMethod.Get, path);
        httpRequest.Headers.TryAddWithoutValidation("Accept", "application/json, text/plain, */*");
        httpRequest.Headers.TryAddWithoutValidation("Accept-Language", "vi,en-US;q=0.9,en;q=0.8");
        httpRequest.Headers.TryAddWithoutValidation("Origin", Origin);
        httpRequest.Headers.TryAddWithoutValidation("Referer", ScrapeReferer);
        httpRequest.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
        httpRequest.Headers.Add("Cookie", cookie);

        var response = await SendFourSellerAsync(httpRequest, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new BusinessException(
                $"4Seller API trả về lỗi ({(int)response.StatusCode}): {Truncate(body, 200)}");
        }

        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        if (root.TryGetProperty("success", out var successProp) &&
            successProp.ValueKind == JsonValueKind.False)
        {
            var messages = ReadApiErrorMessage(root) ?? Truncate(body, 200);
            throw new BusinessException($"4Seller API thất bại: {messages}");
        }

        if (root.TryGetProperty("code", out var codeProp) &&
            codeProp.ValueKind == JsonValueKind.Number &&
            codeProp.GetInt32() != 0)
        {
            var messages = ReadApiErrorMessage(root) ?? Truncate(body, 200);
            throw new BusinessException($"4Seller API thất bại: {messages}");
        }

        return body;
    }

    private async Task<string> PostRawAsync(
        Guid userId,
        string path,
        object payload,
        string referer,
        CancellationToken cancellationToken,
        JsonSerializerOptions? jsonOptions = null)
    {
        var cookie = await EnsureFourSellerLoggedInAsync(userId, cancellationToken);
        var options = jsonOptions ?? JsonOptions;

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, path);
        httpRequest.Headers.Add("Cookie", cookie);
        httpRequest.Headers.TryAddWithoutValidation("Accept", "application/json, text/plain, */*");
        httpRequest.Headers.TryAddWithoutValidation("Accept-Language", "vi,en-US;q=0.9,en;q=0.8");
        httpRequest.Headers.TryAddWithoutValidation("Origin", Origin);
        httpRequest.Headers.TryAddWithoutValidation("Referer", referer);
        httpRequest.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
        httpRequest.Content = new StringContent(
            JsonSerializer.Serialize(payload, options),
            Encoding.UTF8,
            "application/json");

        var response = await SendFourSellerAsync(httpRequest, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new BusinessException(
                $"4Seller API trả về lỗi ({(int)response.StatusCode}): {Truncate(body, 200)}");
        }

        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        if (root.TryGetProperty("success", out var successProp) &&
            successProp.ValueKind == JsonValueKind.False)
        {
            var messages = ReadApiErrorMessage(root) ?? Truncate(body, 200);
            _logger.LogDebug(
                "4Seller API success=false on {Path}: {Message}; body={Body}",
                path,
                messages,
                Truncate(body, 500));
            throw new BusinessException($"4Seller API thất bại: {messages}");
        }

        if (root.TryGetProperty("code", out var codeProp) &&
            codeProp.ValueKind == JsonValueKind.Number &&
            codeProp.GetInt32() != 0)
        {
            var messages = ReadApiErrorMessage(root) ?? Truncate(body, 200);
            _logger.LogDebug(
                "4Seller API code={Code} on {Path}: {Message}; body={Body}",
                codeProp.GetInt32(),
                path,
                messages,
                Truncate(body, 500));
            throw new BusinessException($"4Seller API thất bại: {messages}");
        }

        return body;
    }

    private static bool IsListingPageUpdateTimeFailure(BusinessException ex) =>
        ex.Message.Contains("Get_Page_Failure", StringComparison.OrdinalIgnoreCase);

    private async Task<string> GetRawAsync(
        Guid userId,
        string path,
        string referer,
        CancellationToken cancellationToken)
    {
        var cookie = await EnsureFourSellerLoggedInAsync(userId, cancellationToken);
        return await GetRawWithCookieAsync(cookie, path, referer, cancellationToken);
    }

    private async Task<string> GetRawWithCookieAsync(
        string cookie,
        string path,
        string referer,
        CancellationToken cancellationToken)
    {
        using var httpRequest = new HttpRequestMessage(HttpMethod.Get, path);
        httpRequest.Headers.Add("Cookie", cookie);
        httpRequest.Headers.TryAddWithoutValidation("Accept", "application/json, text/plain, */*");
        httpRequest.Headers.TryAddWithoutValidation("Accept-Language", "en-US");
        httpRequest.Headers.TryAddWithoutValidation("Origin", Origin);
        httpRequest.Headers.TryAddWithoutValidation("Referer", referer);
        httpRequest.Headers.TryAddWithoutValidation("User-Agent", UserAgent);

        var response = await SendFourSellerAsync(httpRequest, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new BusinessException(
                $"4Seller API trả về lỗi ({(int)response.StatusCode}): {Truncate(body, 200)}");
        }

        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        if (root.TryGetProperty("success", out var successProp) &&
            successProp.ValueKind == JsonValueKind.False)
        {
            var messages = ReadApiErrorMessage(root) ?? Truncate(body, 200);
            throw new BusinessException($"4Seller API thất bại: {messages}");
        }

        if (root.TryGetProperty("code", out var codeProp) &&
            codeProp.ValueKind == JsonValueKind.Number &&
            codeProp.GetInt32() != 0)
        {
            var messages = ReadApiErrorMessage(root) ?? Truncate(body, 200);
            throw new BusinessException($"4Seller API thất bại: {messages}");
        }

        return body;
    }

    private static string? ReadApiErrorMessage(JsonElement root)
    {
        if (root.TryGetProperty("messages", out var messages))
        {
            var text = ReadJsonElementAsText(messages);
            if (!string.IsNullOrWhiteSpace(text))
            {
                return TranslateFourSellerMessage(text);
            }
        }

        if (root.TryGetProperty("msg", out var msg))
        {
            var text = ReadJsonElementAsText(msg);
            if (!string.IsNullOrWhiteSpace(text))
            {
                return TranslateFourSellerMessage(text);
            }
        }

        if (root.TryGetProperty("message", out var message))
        {
            var text = ReadJsonElementAsText(message);
            if (!string.IsNullOrWhiteSpace(text))
            {
                return TranslateFourSellerMessage(text);
            }
        }

        if (root.TryGetProperty("data", out var data))
        {
            if (data.ValueKind is JsonValueKind.String or JsonValueKind.Number)
            {
                var text = data.ToString();
                if (!string.IsNullOrWhiteSpace(text) &&
                    !string.Equals(text, "null", StringComparison.OrdinalIgnoreCase))
                {
                    return TranslateFourSellerMessage(text);
                }
            }

            if (data.ValueKind == JsonValueKind.Object)
            {
                foreach (var key in new[] { "errorMessage", "error_message", "message", "msg", "errorMsg", "error", "reason" })
                {
                    if (!data.TryGetProperty(key, out var nested))
                    {
                        continue;
                    }

                    var text = ReadJsonElementAsText(nested);
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        return TranslateFourSellerMessage(text);
                    }
                }
            }
        }

        return null;
    }

    private static string? ReadJsonElementAsText(JsonElement element) =>
        element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.ToString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Array => string.Join(
                "; ",
                element.EnumerateArray()
                    .Select(ReadJsonElementAsText)
                    .Where(s => !string.IsNullOrWhiteSpace(s))),
            _ => null
        };

    private static string TranslateFourSellerMessage(string message)
    {
        var trimmed = message.Trim();
        if (trimmed.Contains("业务异常参数错误", StringComparison.Ordinal))
        {
            return "Lỗi tham số không hợp lệ (业务异常参数错误) — thường do payload thiếu/sai field bắt buộc, nhiều mảng variant trùng (`variation` + `variationList`), hoặc thiếu `null` explicit cho field như `isGlobal`. Kiểm tra category, shop, warehouse, SKU/giá/tồn và đồng bộ lại raw detail.";
        }

        if (trimmed.Contains("参数错误", StringComparison.Ordinal))
        {
            return $"Lỗi tham số 4Seller: {trimmed}";
        }

        return trimmed;
    }

    private async Task<string> PostFormRawAsync(
        Guid userId,
        string path,
        IReadOnlyDictionary<string, string> form,
        string referer,
        CancellationToken cancellationToken)
    {
        var cookie = await EnsureFourSellerLoggedInAsync(userId, cancellationToken);
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, path);
        httpRequest.Headers.Add("Cookie", cookie);
        httpRequest.Headers.TryAddWithoutValidation("Accept", "application/json, text/plain, */*");
        httpRequest.Headers.TryAddWithoutValidation("Accept-Language", "en-US");
        httpRequest.Headers.TryAddWithoutValidation("Origin", Origin);
        httpRequest.Headers.TryAddWithoutValidation("Referer", referer);
        httpRequest.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
        httpRequest.Content = new FormUrlEncodedContent(form);

        var response = await SendFourSellerAsync(httpRequest, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new BusinessException(
                $"4Seller API trả về lỗi ({(int)response.StatusCode}): {Truncate(body, 200)}");
        }

        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        if (root.TryGetProperty("code", out var codeProp) &&
            codeProp.ValueKind == JsonValueKind.Number &&
            codeProp.GetInt32() != 0)
        {
            var msg = root.TryGetProperty("msg", out var msgProp)
                ? msgProp.ToString()
                : Truncate(body, 200);
            throw new BusinessException($"4Seller AI category thất bại: {msg}");
        }

        return body;
    }

    private static FourSellerAiCategoryResultDto ParseAiCategoryResponse(string body, string site)
    {
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
        {
            // Keep the body: a `code: 0` with empty `data` means the AI declined the title, and the
            // only way to tell that apart from a wrong shop context is to see what came back.
            throw new BusinessException($"4Seller AI không trả về category: {Truncate(body, 200)}");
        }

        var categoryId = ReadString(data, "categoryId", "category_id")
            ?? throw new BusinessException("4Seller AI không trả về categoryId.");
        var categoryName = ReadString(data, "categoryName", "category_name") ?? categoryId;
        var nodePath = ReadString(data, "nodePath", "node_path");
        var nodePathId = ReadString(data, "nodePathId", "node_path_id");
        var isLeaf = true;
        if (TryReadInt(data, out var leafCategory, "leafCategory", "leaf_category"))
        {
            isLeaf = leafCategory == 1;
        }

        var pathIds = string.IsNullOrWhiteSpace(nodePathId)
            ? [categoryId]
            : nodePathId.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();

        if (pathIds.Count == 0 || pathIds[^1] != categoryId)
        {
            if (!pathIds.Contains(categoryId))
            {
                pathIds.Add(categoryId);
            }
        }

        return new FourSellerAiCategoryResultDto
        {
            CategoryId = categoryId,
            CategoryName = categoryName,
            NodePath = nodePath,
            NodePathId = nodePathId,
            PathIds = pathIds,
            IsLeaf = isLeaf,
            Site = site
        };
    }

    private static IReadOnlyList<FourSellerMoveSourceListingDto> ParseMoveSourceListingResponse(string body)
    {
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var list = new List<FourSellerMoveSourceListingDto>();
        foreach (var item in data.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            TryReadLong(item, out var matchId, "matchId", "match_id");
            TryReadLong(item, out var targetShopId, "targetShopId", "target_shop_id");
            TryReadLong(item, out var targetListingId, "targetListingId", "target_listing_id");
            var match = false;
            if (item.TryGetProperty("match", out var matchProp))
            {
                match = matchProp.ValueKind switch
                {
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    JsonValueKind.Number => matchProp.GetInt32() != 0,
                    JsonValueKind.String => bool.TryParse(matchProp.GetString(), out var b) && b,
                    _ => false
                };
            }

            list.Add(new FourSellerMoveSourceListingDto
            {
                Match = match,
                MatchId = matchId > 0 ? matchId : null,
                SourceCategoryId = ReadString(item, "sourceCategoryId", "source_category_id"),
                SourceCategoryName = ReadString(item, "sourceCategoryName", "source_category_name"),
                SourceCategoryPath = ReadString(item, "sourceCategoryPath", "source_category_path"),
                SourcePlatform = ReadString(item, "sourcePlatform", "source_platform"),
                TargetShopId = targetShopId,
                TargetShopName = ReadString(item, "targetShopName", "target_shop_name"),
                TargetListingId = targetListingId,
                ListingName = ReadString(item, "listingName", "listing_name"),
                ListingUrl = ReadString(item, "listingUrl", "listing_url"),
                TargetCategoryId = ReadString(item, "targetCategoryId", "target_category_id"),
                TargetCategoryName = ReadString(item, "targetCategoryName", "target_category_name"),
                TargetCategoryPath = ReadString(item, "targetCategoryPath", "target_category_path"),
                TargetCategoryNodePathId = ReadString(
                    item,
                    "targetCategoryNodePathId",
                    "target_category_node_path_id")
            });
        }

        return list;
    }

    private static IReadOnlyList<FourSellerRecentUseCategoryDto> ParseRecentUseCategoryResponse(string body)
    {
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var list = new List<FourSellerRecentUseCategoryDto>();
        foreach (var item in data.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            TryReadLong(item, out var id, "id");
            if (id <= 0)
            {
                continue;
            }

            TryReadInt(item, out var leafCategory, "leafCategory", "leaf_category");
            list.Add(new FourSellerRecentUseCategoryDto
            {
                Id = id,
                Site = ReadString(item, "site") ?? "US",
                CategoryId = ReadString(item, "categoryId", "category_id") ?? string.Empty,
                CategoryParentId = ReadString(item, "categoryParentId", "category_parent_id"),
                CategoryName = ReadString(item, "categoryName", "category_name") ?? string.Empty,
                NodePath = ReadString(item, "nodePath", "node_path"),
                NodePathId = ReadString(item, "nodePathId", "node_path_id"),
                LeafCategory = leafCategory,
                CategoryVersion = ReadString(item, "categoryVersion", "category_version")
            });
        }

        return list;
    }

    private static FourSellerMoveDraftPageDto ParseMoveDraftPageResponse(
        string body,
        int fallbackPage,
        int fallbackSize)
    {
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
        {
            return new FourSellerMoveDraftPageDto
            {
                PageCurrent = fallbackPage,
                PageSize = fallbackSize
            };
        }

        var records = new List<FourSellerMoveDraftRecordDto>();
        if (data.TryGetProperty("records", out var recordsEl) &&
            recordsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in recordsEl.EnumerateArray())
            {
                records.Add(MapMoveDraftRecord(item));
            }
        }

        TryReadInt(data, out var total, "total");
        TryReadInt(data, out var pageSize, "pageSize", "page_size");
        TryReadInt(data, out var pageCurrent, "pageCurrent", "page_current");

        return new FourSellerMoveDraftPageDto
        {
            Records = records,
            Total = total,
            PageSize = pageSize > 0 ? pageSize : fallbackSize,
            PageCurrent = pageCurrent > 0 ? pageCurrent : fallbackPage
        };
    }

    private static FourSellerMoveDraftRecordDto MapMoveDraftRecord(JsonElement element)
    {
        TryReadLong(element, out var id, "id");
        TryReadInt(element, out var status, "status");
        TryReadInt(element, out var variantCount, "variantCount", "variant_count");
        TryReadDecimal(element, out var lowPrice, "lowPrice", "low_price");
        TryReadDecimal(element, out var highPrice, "highPrice", "high_price");

        return new FourSellerMoveDraftRecordDto
        {
            Id = id,
            Status = status,
            Image = ReadString(element, "image"),
            SourceUrl = ReadString(element, "sourceUrl", "source_url"),
            VariantCount = variantCount,
            LowPrice = lowPrice,
            HighPrice = highPrice,
            Currency = ReadString(element, "currency"),
            Creator = ReadString(element, "creator"),
            CreateTime = ReadString(element, "createTime", "create_time"),
            ProductName = ReadString(element, "productName", "product_name")
        };
    }

    private static FourSellerOrderPageDto ParseOrderPageResponse(
        string body,
        int fallbackPage,
        int fallbackSize)
    {
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
        {
            return new FourSellerOrderPageDto
            {
                PageCurrent = fallbackPage,
                PageSize = fallbackSize
            };
        }

        var records = new List<FourSellerOrderRecordDto>();
        if (data.TryGetProperty("records", out var recordsEl) &&
            recordsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in recordsEl.EnumerateArray())
            {
                records.Add(MapOrderRecord(item));
            }
        }

        TryReadInt(data, out var total, "total");
        TryReadInt(data, out var pageSize, "pageSize", "page_size");
        TryReadInt(data, out var pageCurrent, "pageCurrent", "page_current");

        return new FourSellerOrderPageDto
        {
            Records = records,
            Total = total,
            PageSize = pageSize > 0 ? pageSize : fallbackSize,
            PageCurrent = pageCurrent > 0 ? pageCurrent : fallbackPage
        };
    }

    private static FourSellerReturnPageDto ParseReturnPageResponse(
        string body,
        int fallbackPage,
        int fallbackSize)
    {
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
        {
            return new FourSellerReturnPageDto
            {
                PageCurrent = fallbackPage,
                PageSize = fallbackSize
            };
        }

        var records = new List<FourSellerReturnRecordDto>();
        if (data.TryGetProperty("records", out var recordsEl) &&
            recordsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in recordsEl.EnumerateArray())
            {
                records.Add(MapReturnRecord(item));
            }
        }

        TryReadInt(data, out var total, "total");
        TryReadInt(data, out var pageSize, "pageSize", "page_size");
        TryReadInt(data, out var pageCurrent, "pageCurrent", "page_current");

        return new FourSellerReturnPageDto
        {
            Records = records,
            Total = total,
            PageSize = pageSize > 0 ? pageSize : fallbackSize,
            PageCurrent = pageCurrent > 0 ? pageCurrent : fallbackPage
        };
    }

    private static FourSellerReturnRecordDto MapReturnRecord(JsonElement element)
    {
        TryReadLong(element, out var id, "id");
        TryReadLong(element, out var erpOrderId, "erpOrderId", "erp_order_id");
        TryReadDecimal(element, out var refundTotal, "refundTotal", "refund_total");

        long? shopId = null;
        if (TryReadLong(element, out var shop, "shopId", "shop_id") && shop > 0)
        {
            shopId = shop;
        }

        return new FourSellerReturnRecordDto
        {
            Id = id,
            ReturnId = ReadString(element, "returnId", "return_id"),
            PlatformOrderId = ReadString(element, "platformOrderId", "platform_order_id"),
            ErpOrderId = erpOrderId,
            ShopId = shopId,
            ReturnType = ReadString(element, "returnType", "return_type"),
            PlatformOrderStatus = ReadString(element, "platformOrderStatus", "platform_order_status"),
            PlatformReturnStatus = ReadString(element, "platformReturnStatus", "platform_return_status"),
            ReturnStatus = ReadString(element, "returnStatus", "return_status"),
            Status = ReadString(element, "status"),
            SettlementStatus = ReadString(element, "settlementStatus", "settlement_status"),
            ReturnReason = ReadString(element, "returnReason", "return_reason"),
            ReturnTrackingNumber = ReadString(element, "returnTrackingNumber", "return_tracking_number"),
            RefundTotal = refundTotal ?? 0m,
            Currency = ReadString(element, "currency"),
            PlatformCreateTime = AppTimeZones.ParseFourSellerNaiveAsUtc(
                ReadString(element, "platformCreateTime", "platform_create_time")),
            PlatformUpdateTime = AppTimeZones.ParseFourSellerNaiveAsUtc(
                ReadString(element, "platformUpdateTime", "platform_update_time"))
        };
    }

    private static FourSellerOrderAmountDetailDto ParseOrderAmountDetailResponse(
        string body,
        long fallbackOrderId)
    {
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
        {
            throw new BusinessException("4Seller order detail không có data.");
        }

        string? shippingMethod = null;
        long orderId = fallbackOrderId;
        if (data.TryGetProperty("orderVo", out var orderVo) && orderVo.ValueKind == JsonValueKind.Object)
        {
            shippingMethod = ReadString(orderVo, "shippingMethod", "shipping_method");
            if (TryReadLong(orderVo, out var id, "id") && id > 0)
            {
                orderId = id;
            }
        }

        // Prefer orderAmountVo; some orders return it null and put amounts in costVo/costDetailVo.
        JsonElement? amountSource = null;
        if (TryGetObjectProperty(data, out var amountVo, "orderAmountVo", "order_amount_vo"))
        {
            amountSource = amountVo;
        }
        else if (data.TryGetProperty("costVo", out var costVoNested) &&
                 costVoNested.ValueKind == JsonValueKind.Object &&
                 TryGetObjectProperty(costVoNested, out var nestedAmount, "orderAmountVo", "order_amount_vo"))
        {
            amountSource = nestedAmount;
        }

        decimal? totalPaid;
        decimal? productBefore;
        decimal? productPlatformDisc;
        decimal? productSellerDisc;
        decimal? shipBefore;
        decimal? shipSellerDisc;
        decimal? totalTax;
        decimal? otherFee;
        string? currency;

        if (amountSource is { } amount)
        {
            TryReadDecimal(amount, out totalPaid, "totalPaidAmount", "total_paid_amount");
            TryReadDecimal(amount, out productBefore, "productAmountBeforeDiscount", "product_amount_before_discount");
            TryReadDecimal(amount, out productPlatformDisc, "productPlatformDiscount", "product_platform_discount");
            TryReadDecimal(amount, out productSellerDisc, "productSellerDiscount", "product_seller_discount");
            TryReadDecimal(amount, out shipBefore, "shippingFeeBeforeDiscount", "shipping_fee_before_discount");
            TryReadDecimal(amount, out shipSellerDisc, "shippingFeeSellerDiscount", "shipping_fee_seller_discount");
            TryReadDecimal(amount, out totalTax, "totalTax", "total_tax");
            TryReadDecimal(amount, out otherFee, "otherFee", "other_fee");
            currency = ReadString(amount, "currency");
        }
        else if (TryParseOrderAmountFromCostFallback(data, out totalPaid, out productBefore,
                     out productPlatformDisc, out productSellerDisc, out shipBefore, out shipSellerDisc,
                     out totalTax, out otherFee, out currency))
        {
            // Mapped from costDetailVo + costVo when orderAmountVo is null.
        }
        else
        {
            throw new BusinessException(
                "4Seller order detail thiếu orderAmountVo (và không có costDetailVo/costVo fallback).");
        }

        return new FourSellerOrderAmountDetailDto
        {
            OrderId = orderId,
            ShippingMethod = shippingMethod,
            TotalPaidAmount = totalPaid,
            ProductAmountBeforeDiscount = productBefore ?? 0,
            ProductPlatformDiscount = productPlatformDisc ?? 0,
            ProductSellerDiscount = productSellerDisc ?? 0,
            ShippingFeeBeforeDiscount = shipBefore ?? 0,
            ShippingFeeSellerDiscount = shipSellerDisc ?? 0,
            TotalTax = totalTax ?? 0,
            OtherFee = otherFee ?? 0,
            Currency = currency
        };
    }

    /// <summary>
    /// Maps <c>costDetailVo</c> + <c>costVo</c> when top-level <c>orderAmountVo</c> is null
    /// (seen on some TikTok to-ship orders).
    /// </summary>
    private static bool TryParseOrderAmountFromCostFallback(
        JsonElement data,
        out decimal? totalPaid,
        out decimal? productBefore,
        out decimal? productPlatformDisc,
        out decimal? productSellerDisc,
        out decimal? shipBefore,
        out decimal? shipSellerDisc,
        out decimal? totalTax,
        out decimal? otherFee,
        out string? currency)
    {
        totalPaid = null;
        productBefore = null;
        productPlatformDisc = null;
        productSellerDisc = null;
        shipBefore = null;
        shipSellerDisc = null;
        totalTax = null;
        otherFee = null;
        currency = null;

        var hasCostDetail = data.TryGetProperty("costDetailVo", out var costDetail) &&
                            costDetail.ValueKind == JsonValueKind.Object;
        var hasCostVo = data.TryGetProperty("costVo", out var costVo) &&
                        costVo.ValueKind == JsonValueKind.Object;
        if (!hasCostDetail && !hasCostVo)
        {
            return false;
        }

        if (hasCostVo)
        {
            TryReadDecimal(costVo, out totalPaid, "totalPaidByCustomer", "total_paid_by_customer", "totalPaidAmount");
            TryReadDecimal(costVo, out totalTax, "totalTax", "total_tax");
            TryReadDecimal(costVo, out otherFee, "otherFee", "other_fee");
            currency = ReadString(costVo, "currency");
        }

        if (hasCostDetail)
        {
            TryReadDecimal(costDetail, out productBefore, "originalTotalProductPrice", "original_total_product_price");
            TryReadDecimal(costDetail, out productPlatformDisc, "platformDiscount", "platform_discount");
            TryReadDecimal(costDetail, out productSellerDisc, "sellerDiscount", "seller_discount");
            TryReadDecimal(costDetail, out shipBefore, "originalShippingFee", "original_shipping_fee");
            TryReadDecimal(costDetail, out shipSellerDisc, "shippingFeeSellerDiscount", "shipping_fee_seller_discount");
            if (!totalTax.HasValue)
            {
                TryReadDecimal(costDetail, out totalTax, "tax", "totalTax", "total_tax");
            }

            if (!otherFee.HasValue)
            {
                TryReadDecimal(costDetail, out otherFee, "otherFee", "other_fee");
            }

            if (!totalPaid.HasValue)
            {
                TryReadDecimal(costDetail, out totalPaid, "totalAmount", "total_amount");
            }
        }

        return true;
    }

    private static bool TryGetObjectProperty(
        JsonElement parent,
        out JsonElement value,
        params string[] names)
    {
        foreach (var name in names)
        {
            if (parent.TryGetProperty(name, out value) && value.ValueKind == JsonValueKind.Object)
            {
                return true;
            }
        }

        value = default;
        return false;
    }

    private static IReadOnlyList<FourSellerShippingServiceDto> ParseShippingServicesResponse(string body)
    {
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<FourSellerShippingServiceDto>();
        }

        var list = new List<FourSellerShippingServiceDto>();
        foreach (var item in data.EnumerateArray())
        {
            TryReadLong(item, out var id, "id");
            var carrier = ReadString(item, "carrier", "name") ?? string.Empty;
            if (string.IsNullOrWhiteSpace(carrier))
            {
                continue;
            }

            list.Add(new FourSellerShippingServiceDto
            {
                Id = id,
                Carrier = carrier.Trim(),
                Platform = ReadString(item, "platform"),
                Region = ReadString(item, "region")
            });
        }

        return list;
    }

    private static string ParseMarkShipRequestId(string body)
    {
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        if (!root.TryGetProperty("data", out var data))
        {
            throw new BusinessException("4Seller Mark Shipped không trả về requestId.");
        }

        var requestId = data.ValueKind switch
        {
            JsonValueKind.String => data.GetString(),
            JsonValueKind.Number => data.ToString(),
            JsonValueKind.Object when data.TryGetProperty("requestId", out var rid) =>
                rid.ValueKind == JsonValueKind.String ? rid.GetString() : rid.ToString(),
            _ => null
        };

        if (string.IsNullOrWhiteSpace(requestId))
        {
            throw new BusinessException("4Seller Mark Shipped không trả về requestId hợp lệ.");
        }

        return requestId.Trim();
    }

    private static FourSellerOrderRecordDto MapOrderRecord(JsonElement element)
    {
        TryReadLong(element, out var orderId, "orderId", "order_id");
        TryReadDecimal(element, out var amount, "amount");
        TryReadDecimal(element, out var totalPaid, "totalPaidAmount", "total_paid_amount");
        TryReadLong(element, out var shopId, "shopId", "shop_id");

        var record = new FourSellerOrderRecordDto
        {
            OrderId = orderId,
            Amount = amount ?? 0,
            TotalPaidAmount = totalPaid ?? 0,
            Currency = ReadString(element, "currency"),
            OrderStatus = ReadString(element, "orderStatus", "order_status"),
            PlatformOrderStatus = ReadString(element, "platformOrderStatus", "platform_order_status"),
            Platform = ReadString(element, "platform"),
            PlatformOrderId = ReadString(element, "platformOrderId", "platform_order_id"),
            PlatformOrderNo = ReadString(element, "platformOrderNo", "platform_order_no"),
            ShopId = shopId > 0 ? shopId : null,
            ShopName = ReadString(element, "shopName", "shop_name"),
            BuyerMemo = ReadString(element, "buyerMemo", "buyer_memo"),
            SellerMemo = ReadString(element, "sellerMemo", "seller_memo"),
            InternalMemo = ReadString(element, "internalMemo", "internal_memo"),
            ShipmentNo = ReadString(element, "shipmentNo", "shipment_no"),
            TrackingNo = ReadString(
                element,
                "trackingNo",
                "tracking_no",
                "trackingNumber",
                "tracking_number"),
            ShippingMethod = ReadString(element, "shippingMethod", "shipping_method"),
            RawJson = element.GetRawText()
        };

        if (element.TryGetProperty("orderAddressVo", out var address) &&
            address.ValueKind == JsonValueKind.Object)
        {
            record.OrderAddressVo = new FourSellerOrderAddressDto
            {
                Name = ReadString(address, "name"),
                City = ReadString(address, "city"),
                PostalCode = ReadString(address, "postalCode", "postal_code"),
                Address = ReadString(address, "address"),
                Address1 = ReadString(address, "address1"),
                Phone = ReadString(address, "phone"),
                Email = ReadString(address, "email"),
                CountryCode = ReadString(address, "countryCode", "country_code"),
                State = ReadString(address, "state")
            };
        }

        if (element.TryGetProperty("timeInfo", out var timeInfo) &&
            timeInfo.ValueKind == JsonValueKind.Object)
        {
            record.TimeInfo = new FourSellerOrderTimeInfoDto
            {
                OrderPaidTime = ReadString(timeInfo, "orderPaidTime", "order_paid_time"),
                OrderCreateTime = ReadString(timeInfo, "orderCreateTime", "order_create_time"),
                LatestShipDate = ReadString(timeInfo, "latestShipDate", "latest_ship_date"),
                OrderShippedTime = ReadString(timeInfo, "orderShippedTime", "order_shipped_time"),
                LatestDeliveryDate = ReadString(timeInfo, "latestDeliveryDate", "latest_delivery_date"),
                CancelTime = ReadString(timeInfo, "cancelTime", "cancel_time")
            };
        }

        if (element.TryGetProperty("shippingServiceInfo", out var shipping) &&
            shipping.ValueKind == JsonValueKind.Object)
        {
            record.ShippingServiceInfo = new FourSellerOrderShippingServiceDto
            {
                ShippingService = ReadString(shipping, "shippingService", "shipping_service")
            };
            record.TrackingNo ??= ReadString(
                shipping,
                "trackingNo",
                "tracking_no",
                "trackingNumber",
                "tracking_number");
            record.ShipmentNo ??= ReadString(shipping, "shipmentNo", "shipment_no");
        }

        if (element.TryGetProperty("packageInfo", out var packageInfo) &&
            packageInfo.ValueKind == JsonValueKind.Object)
        {
            TryReadDecimal(packageInfo, out var weight, "weight");
            TryReadDecimal(packageInfo, out var length, "length");
            TryReadDecimal(packageInfo, out var width, "width");
            TryReadDecimal(packageInfo, out var height, "height");
            record.PackageInfo = new FourSellerOrderPackageDto
            {
                Weight = weight,
                WeightUnit = ReadString(packageInfo, "weightUnit", "weight_unit"),
                Length = length,
                Width = width,
                Height = height,
                DimensionUnit = ReadString(packageInfo, "dimensionUnit", "dimension_unit")
            };
        }

        if (element.TryGetProperty("orderItemInfoList", out var items) &&
            items.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in items.EnumerateArray())
            {
                TryReadLong(item, out var orderItemId, "orderItemId", "order_item_id");
                TryReadLong(item, out var itemOrderId, "orderId", "order_id");
                TryReadDecimal(item, out var itemPrice, "itemPrice", "item_price");
                TryReadInt(item, out var quantity, "quantity");

                record.OrderItemInfoList.Add(new FourSellerOrderItemDto
                {
                    OrderItemId = orderItemId,
                    OrderId = itemOrderId > 0 ? itemOrderId : orderId,
                    SellerSku = ReadString(item, "sellerSku", "seller_sku"),
                    Title = ReadString(item, "title"),
                    ImgUrl = ReadString(
                        item,
                        "imgUrl",
                        "img_url",
                        "imageUrl",
                        "image_url",
                        "image",
                        "picUrl",
                        "pic_url",
                        "thumbUrl",
                        "thumbnail"),
                    ListingUrl = ReadString(item, "listingUrl", "listing_url"),
                    SourceUrl = ReadString(item, "sourceUrl", "source_url"),
                    VariantAttr = ReadString(item, "variantAttr", "variant_attr"),
                    Quantity = quantity,
                    ItemPrice = itemPrice ?? 0,
                    Currency = ReadString(item, "currency")
                });
            }
        }

        return record;
    }

    private static FourSellerListingStatusCountDto ParseListingStatusCountResponse(string body)
    {
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
        {
            return new FourSellerListingStatusCountDto();
        }

        TryReadInt(data, out var active, "activeCount", "active_count");
        TryReadInt(data, out var inactive, "inactiveCount", "inactive_count");
        TryReadInt(data, out var removed, "removedCount", "removed_count");
        TryReadInt(data, out var incomplete, "incompleteCount", "incomplete_count");
        TryReadInt(data, out var suspended, "suspendedCount", "suspended_count");
        TryReadInt(data, out var privateCount, "privateCount", "private_count");
        TryReadInt(data, out var pending, "pendingCount", "pending_count");
        TryReadInt(data, out var reject, "rejectCount", "reject_count");
        TryReadInt(data, out var unsaleable, "unsaleableCount", "unsaleable_count");

        return new FourSellerListingStatusCountDto
        {
            ActiveCount = active,
            InactiveCount = inactive,
            RemovedCount = removed,
            IncompleteCount = incomplete,
            SuspendedCount = suspended,
            PrivateCount = privateCount,
            PendingCount = pending,
            RejectCount = reject,
            UnsaleableCount = unsaleable
        };
    }

    private static FourSellerListingPageDto ParseListingPageResponse(
        string body,
        int fallbackPage,
        int fallbackSize)
    {
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
        {
            return new FourSellerListingPageDto
            {
                PageCurrent = fallbackPage,
                PageSize = fallbackSize
            };
        }

        var records = new List<FourSellerListingRecordDto>();
        if (data.TryGetProperty("records", out var recordsEl) &&
            recordsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in recordsEl.EnumerateArray())
            {
                records.Add(MapListingRecord(item));
            }
        }

        TryReadInt(data, out var total, "total");
        TryReadInt(data, out var pageSize, "pageSize", "page_size");
        TryReadInt(data, out var pageCurrent, "pageCurrent", "page_current");

        return new FourSellerListingPageDto
        {
            Records = records,
            Total = total,
            PageSize = pageSize > 0 ? pageSize : fallbackSize,
            PageCurrent = pageCurrent > 0 ? pageCurrent : fallbackPage
        };
    }

    private static IReadOnlyList<FourSellerListingRecordDto> ParseListingDetailListResponse(string body)
    {
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        if (!root.TryGetProperty("data", out var data))
        {
            return [];
        }

        var results = new List<FourSellerListingRecordDto>();
        if (data.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in data.EnumerateArray())
            {
                results.Add(MapListingRecord(item, captureRaw: true));
            }

            return results;
        }

        if (data.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        foreach (var key in new[] { "records", "list", "items", "details", "detailList" })
        {
            if (data.TryGetProperty(key, out var nested) && nested.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in nested.EnumerateArray())
                {
                    results.Add(MapListingRecord(item, captureRaw: true));
                }

                return results;
            }
        }

        // Single detail object under data.
        if (data.TryGetProperty("id", out _) || data.TryGetProperty("productName", out _))
        {
            results.Add(MapListingRecord(data, captureRaw: true));
        }

        return results;
    }

    private static FourSellerListingRecordDto? ParseSingleListingDetailResponse(string body, long fallbackId)
    {
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        if (!root.TryGetProperty("data", out var data))
        {
            return null;
        }

        JsonElement target = data;
        if (data.ValueKind == JsonValueKind.Array)
        {
            if (data.GetArrayLength() == 0)
            {
                return null;
            }

            target = data[0];
        }
        else if (data.ValueKind == JsonValueKind.Object)
        {
            foreach (var key in new[] { "records", "list", "items", "detail", "listing" })
            {
                if (data.TryGetProperty(key, out var nested))
                {
                    if (nested.ValueKind == JsonValueKind.Array && nested.GetArrayLength() > 0)
                    {
                        target = nested[0];
                        break;
                    }

                    if (nested.ValueKind == JsonValueKind.Object)
                    {
                        target = nested;
                        break;
                    }
                }
            }
        }
        else
        {
            return null;
        }

        var record = MapListingRecord(target, captureRaw: true);
        if (record.Id <= 0)
        {
            record.Id = fallbackId;
        }

        return record;
    }

    private static FourSellerListingRecordDto MapListingRecord(JsonElement element, bool captureRaw = false)
    {
        TryReadLong(element, out var id, "id", "listingId", "listing_id");
        TryReadLong(element, out var shopId, "shopId", "shop_id");
        TryReadInt(element, out var variationCount, "variationCount", "variation_count");
        TryReadInt(element, out var availableStock, "availableStock", "available_stock");
        TryReadDecimal(element, out var lowPrice, "lowPrice", "low_price");
        TryReadDecimal(element, out var highPrice, "highPrice", "high_price");
        TryReadDecimal(element, out var packageWeight, "packageWeight", "package_weight", "weight");
        TryReadDecimal(element, out var packageLength, "packageLength", "package_length", "length");
        TryReadDecimal(element, out var packageWidth, "packageWidth", "package_width", "width");
        TryReadDecimal(element, out var packageHeight, "packageHeight", "package_height", "height");

        var description =
            ReadString(element, "descriptionHtml", "description_html", "description", "productDescription")
            ?? ReadNestedString(element, "productInfo", "description")
            ?? ReadNestedString(element, "productInfo", "descriptionHtml");

        var detailImages = new List<string>();
        // Do NOT ingest allImageList into shared gallery — it can be 100+ URLs (all color shots
        // flattened) and blows up product list/detail payloads. Color galleries come from skuImgs.
        CollectImageUrls(
            element,
            detailImages,
            "imageList",
            "images",
            "photoList",
            "photos",
            "detailImages");

        var colorBoundImages = CollectColorBoundImageUrls(element);

        var variations = new List<FourSellerListingVariationDto>();
        if (TryGetArray(element, out var variationEl, "variationList", "variations", "skuList", "skus", "variation"))
        {
            foreach (var item in variationEl.EnumerateArray())
            {
                variations.Add(MapListingVariation(item));
            }
        }

        if (variationCount <= 0 && variations.Count > 0)
        {
            variationCount = variations.Count;
        }

        int? productStatus = null;
        if (TryReadInt(element, out var productStatusValue, "productStatus", "product_status"))
        {
            productStatus = productStatusValue;
        }

        return new FourSellerListingRecordDto
        {
            Id = id,
            ProductName = ReadString(element, "productName", "product_name", "title", "name"),
            MainImage = ReadString(element, "mainImage", "main_image", "image"),
            ShopId = shopId > 0 ? shopId : null,
            ShopName = ReadString(element, "shopName", "shop_name"),
            Site = ReadString(element, "site", "siteId", "site_id"),
            Currency = ReadString(element, "currency"),
            CategoryId = ReadString(element, "categoryId", "category_id"),
            CategoryName = ReadString(element, "categoryName", "category_name", "categoryPath"),
            LowPrice = lowPrice,
            HighPrice = highPrice,
            AvailableStock = availableStock > 0 ? availableStock : null,
            VariationCount = variationCount,
            // Do not fall back to "status" — that can be a numeric TikTok code or page filter.
            ErpStatus = ReadString(element, "erpStatus", "erp_status"),
            ProductStatus = productStatus,
            PublishStatus = ReadString(element, "publishStatus", "publish_status"),
            FailedMessage = ReadFailedMessage(element),
            MarketplaceProductId = ReadString(
                element,
                "productId",
                "product_id",
                "tiktokProductId",
                "tikTokProductId"),
            SourceUrl = ReadString(element, "sourceUrl", "source_url", "listingSourceUrl"),
            WarehouseId = ReadString(element, "warehouseId", "warehouse_id"),
            CreateTime = ReadString(element, "createTime", "create_time"),
            UpdateTime = ReadString(element, "updateTime", "update_time"),
            DescriptionHtml = description,
            PackageWeight = packageWeight,
            PackageLength = packageLength,
            PackageWidth = packageWidth,
            PackageHeight = packageHeight,
            WeightUnit = ReadString(element, "weightUnit", "weight_unit"),
            DimensionUnit = ReadString(element, "dimensionUnit", "dimension_unit"),
            VariationList = variations,
            DetailImageUrls = detailImages,
            ColorImageUrls = colorBoundImages,
            RawDetailJson = captureRaw ? element.GetRawText() : null
        };
    }

    private static FourSellerListingVariationDto MapListingVariation(JsonElement element)
    {
        TryReadLong(element, out var id, "id");
        TryReadDecimal(element, out var originalPrice, "originalPrice", "original_price", "salePrice", "price");
        TryReadDecimal(element, out var globalPrice, "globalPrice", "global_price");
        var stock = ReadVariationStock(element);

        var mainImage = ReadString(element, "mainImage", "main_image", "image", "imageUrl");
        var imageUrls = new List<string>();
        CollectImageUrls(
            element,
            imageUrls,
            "imageList",
            "images",
            "photoList",
            "photos",
            "skuImageList",
            "skuImages",
            "variantImages",
            "imageUrls");

        // Nested sales attributes sometimes carry sku_img / image.url_list for the color axis
        foreach (var attrListName in new[]
                 {
                     "salesAttributes", "sales_attributes", "saleAttributes",
                     "salePropList", "propertyList", "skuSalesProps", "sku_sales_props"
                 })
        {
            if (!TryGetArray(element, out var attrs, attrListName))
            {
                continue;
            }

            foreach (var attr in attrs.EnumerateArray())
            {
                if (attr.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                AppendExtractedUrls(imageUrls, attr);
                if (attr.TryGetProperty("sku_img", out var skuImg) ||
                    attr.TryGetProperty("skuImg", out skuImg) ||
                    attr.TryGetProperty("image", out skuImg))
                {
                    AppendExtractedUrls(imageUrls, skuImg);
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(mainImage))
        {
            foreach (var part in mainImage.Split(
                         '|',
                         StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!imageUrls.Contains(part, StringComparer.OrdinalIgnoreCase))
                {
                    imageUrls.Insert(0, part);
                }
            }
        }

        return new FourSellerListingVariationDto
        {
            Id = id,
            MainImage = mainImage,
            ImageUrls = imageUrls,
            Title = ReadString(element, "title", "name", "skuName") ?? BuildVariationTitleFromOptions(element),
            SellerSku = ReadString(element, "sellerSku", "seller_sku", "sku", "msku"),
            OriginalPrice = originalPrice,
            GlobalPrice = globalPrice,
            AvailableStock = stock
        };
    }

    /// <summary>
    /// Page rows expose availableStock; detail SKUs often only have stockInfoList[].availableStock.
    /// Prefer <c>stockInfoList</c> over <c>globalStockInfoList</c> (do not sum both — same qty mirrored
    /// into both lists used to inflate stock on every sync). Within a list, take max per warehouse
    /// then sum distinct warehouses.
    /// </summary>
    internal static int? ReadVariationStock(JsonElement element)
    {
        foreach (var listName in new[] { "stockInfoList", "stock_info_list", "globalStockInfoList", "stocks" })
        {
            if (!TryGetArray(element, out var arr, listName))
            {
                continue;
            }

            var perWarehouse = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var found = false;
            foreach (var item in arr.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                if (TryReadInt(
                        item,
                        out var qty,
                        "availableStock",
                        "available_stock",
                        "quantity",
                        "availableQuantity",
                        "available_quantity",
                        "stock",
                        "inventory") &&
                    qty >= 0)
                {
                    found = true;
                    var wid = ReadString(item, "warehouse_id")
                              ?? ReadString(item, "warehouseId")
                              ?? string.Empty;
                    if (!perWarehouse.TryGetValue(wid, out var existing) || qty > existing)
                    {
                        perWarehouse[wid] = qty;
                    }
                }
            }

            if (found)
            {
                long total = 0;
                foreach (var q in perWarehouse.Values)
                {
                    total += q;
                }

                return total > int.MaxValue ? int.MaxValue : (int)total;
            }
        }

        if (TryReadInt(element, out var top, "availableStock", "available_stock", "quantity", "inventory") &&
            top >= 0)
        {
            return top;
        }

        return null;
    }

    /// <summary>
    /// Detail payload uses option1/option2 (Color/Size) instead of title "Color/Size".
    /// </summary>
    private static string? BuildVariationTitleFromOptions(JsonElement element)
    {
        var parts = new List<string>();
        foreach (var name in new[] { "option1", "option2", "option3" })
        {
            var value = ReadString(element, name);
            if (!string.IsNullOrWhiteSpace(value))
            {
                parts.Add(value.Trim());
            }
        }

        return parts.Count > 0 ? string.Join('/', parts) : null;
    }

    private static bool TryGetArray(JsonElement element, out JsonElement array, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.Array)
            {
                array = prop;
                return true;
            }
        }

        array = default;
        return false;
    }

    private static void CollectImageUrls(JsonElement element, List<string> target, params string[] names)
    {
        foreach (var name in names)
        {
            if (!element.TryGetProperty(name, out var prop))
            {
                continue;
            }

            AppendExtractedUrls(target, prop);
        }
    }

    /// <summary>
    /// Extracts HTTP(S) image URLs from string / object / array shapes used by 4Seller / TikTok
    /// (url, uri, outer_url, url_list, nested image objects, pipe-separated strings).
    /// </summary>
    private static void AppendExtractedUrls(List<string> target, JsonElement node)
    {
        switch (node.ValueKind)
        {
            case JsonValueKind.String:
            {
                var raw = node.GetString();
                if (string.IsNullOrWhiteSpace(raw))
                {
                    return;
                }

                foreach (var part in raw.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    if (LooksLikeImageUrl(part) && !target.Contains(part, StringComparer.OrdinalIgnoreCase))
                    {
                        target.Add(part);
                    }
                }

                return;
            }
            case JsonValueKind.Array:
                foreach (var item in node.EnumerateArray())
                {
                    AppendExtractedUrls(target, item);
                }

                return;
            case JsonValueKind.Object:
            {
                foreach (var key in new[]
                         {
                             "url", "imageUrl", "image", "mainImage", "uri", "outer_url", "outerUrl",
                             "thumbUrl", "thumbnail", "img_url", "imgUrl"
                         })
                {
                    if (!node.TryGetProperty(key, out var prop))
                    {
                        continue;
                    }

                    if (prop.ValueKind == JsonValueKind.String)
                    {
                        AppendExtractedUrls(target, prop);
                    }
                    else if (prop.ValueKind == JsonValueKind.Object || prop.ValueKind == JsonValueKind.Array)
                    {
                        AppendExtractedUrls(target, prop);
                    }
                }

                foreach (var listKey in new[] { "url_list", "urlList", "urls", "imageList", "images" })
                {
                    if (!node.TryGetProperty(listKey, out var list))
                    {
                        continue;
                    }

                    // url_list is usually the same asset mirrored across CDNs — keep first HTTP URL only
                    if ((listKey is "url_list" or "urlList") && list.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in list.EnumerateArray())
                        {
                            var before = target.Count;
                            AppendExtractedUrls(target, item);
                            if (target.Count > before)
                            {
                                break;
                            }
                        }

                        continue;
                    }

                    AppendExtractedUrls(target, list);
                }

                return;
            }
        }
    }

    private static bool LooksLikeImageUrl(string value) =>
        value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
        value.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Ingests skuImgs / salePropImageMap as object map (color → urls) or Color-row array.
    /// </summary>
    private static void IngestColorBoundMapNode(
        JsonElement map,
        Action<string?, JsonElement> addAll)
    {
        if (map.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in map.EnumerateObject())
            {
                addAll(prop.Name, prop.Value);
            }

            return;
        }

        if (map.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var item in map.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var attrName = ReadString(item, "attrName", "attr_name", "name");
            if (!string.IsNullOrWhiteSpace(attrName) &&
                !attrName.Contains("color", StringComparison.OrdinalIgnoreCase) &&
                !attrName.Contains("colour", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var color = ReadString(
                item,
                "custom_value",
                "customValue",
                "attrValue",
                "attr_value",
                "attributeValue",
                "propertyValue",
                "salePropValue",
                "prop_value",
                "propValue",
                "value",
                "valueName",
                "value_name",
                "color",
                "colorValue",
                "colorName",
                "commentColor",
                "key",
                "title",
                "label");
            // Prefer multi-URL img_url (pipe-separated) before other fields inside AddAll.
            if (item.TryGetProperty("img_url", out var imgUrl) ||
                item.TryGetProperty("imgUrl", out imgUrl))
            {
                addAll(color, imgUrl);
            }

            addAll(color, item);
        }
    }

    /// <summary>
    /// Collects color → image URL lists from common 4Seller payload shapes
    /// (salePropImageMap, color-tagged imageList, sales property value lists).
    /// </summary>
    private static Dictionary<string, List<string>> CollectColorBoundImageUrls(JsonElement element)
    {
        var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        void Add(string? color, string? url)
        {
            if (string.IsNullOrWhiteSpace(color) || string.IsNullOrWhiteSpace(url))
            {
                return;
            }

            color = color.Trim();
            url = url.Trim();
            if (!LooksLikeImageUrl(url))
            {
                return;
            }

            if (!result.TryGetValue(color, out var list))
            {
                list = [];
                result[color] = list;
            }

            if (!list.Contains(url, StringComparer.OrdinalIgnoreCase))
            {
                list.Add(url);
            }
        }

        void AddAll(string? color, JsonElement node)
        {
            var urls = new List<string>();
            AppendExtractedUrls(urls, node);
            foreach (var url in urls)
            {
                Add(color, url);
            }
        }

        foreach (var mapName in new[]
                 {
                     "salePropImageMap", "sale_prop_image_map",
                     "propertyImageMap", "property_image_map",
                     "colorImageMap", "color_image_map",
                     "variantImageMap", "skuImageMap",
                     "colorImages", "variantImages",
                     // 4Seller TikTok listing detail (batch-detail/detail)
                     "skuImgs", "skuImages", "uploadImageInfoMap"
                 })
        {
            if (!element.TryGetProperty(mapName, out var map))
            {
                continue;
            }

            // Detail often sends skuImgs as a JSON string, object map, or array of Color rows.
            if (map.ValueKind == JsonValueKind.String)
            {
                var raw = map.GetString()?.Trim();
                if (string.IsNullOrWhiteSpace(raw))
                {
                    continue;
                }

                try
                {
                    using var doc = JsonDocument.Parse(raw);
                    IngestColorBoundMapNode(doc.RootElement, AddAll);
                }
                catch (JsonException)
                {
                    // ignore malformed skuImgs payloads
                }

                continue;
            }

            IngestColorBoundMapNode(map, AddAll);
        }

        foreach (var listName in new[]
                 {
                     "imageList", "images", "photoList", "photos", "skuImageList", "variantImageList",
                     "allImageList"
                 })
        {
            if (!element.TryGetProperty(listName, out var arr) || arr.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var item in arr.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var color = ReadString(
                    item,
                    "color",
                    "colorValue",
                    "colorName",
                    "commentColor",
                    "propertyValue",
                    "salePropValue",
                    "prop_value",
                    "propValue",
                    "specValue",
                    "name",
                    "value");
                AddAll(color, item);
            }
        }

        // Color axis property trees: { name: "Color", values: [{ name, image/images/... }] }
        foreach (var propListName in new[]
                 {
                     "salePropList", "salesPropertyList", "sales_property_list",
                     "propertyList", "saleAttributes", "salesAttributes", "sales_attributes",
                     "skuSalesProps", "sku_sales_props"
                 })
        {
            if (!TryGetArray(element, out var props, propListName))
            {
                continue;
            }

            foreach (var prop in props.EnumerateArray())
            {
                if (prop.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var propName = ReadString(prop, "name", "attrName", "attr_name", "propertyName", "prop_name");
                var looksLikeColor =
                    !string.IsNullOrWhiteSpace(propName) &&
                    (propName.Contains("color", StringComparison.OrdinalIgnoreCase) ||
                     propName.Contains("colour", StringComparison.OrdinalIgnoreCase));

                if (!looksLikeColor)
                {
                    continue;
                }

                // Single-value attribute on a SKU row (name=Color, value_name=Red, sku_img=...)
                var singleValue = ReadString(prop, "value", "value_name", "valueName", "prop_value", "propertyValue");
                if (!string.IsNullOrWhiteSpace(singleValue))
                {
                    AddAll(singleValue, prop);
                    if (prop.TryGetProperty("sku_img", out var skuImg) ||
                        prop.TryGetProperty("skuImg", out skuImg))
                    {
                        AddAll(singleValue, skuImg);
                    }
                }

                foreach (var valuesName in new[]
                         {
                             "values", "valueList", "propertyValues", "property_values",
                             "salePropValues", "options"
                         })
                {
                    if (!TryGetArray(prop, out var values, valuesName))
                    {
                        continue;
                    }

                    foreach (var value in values.EnumerateArray())
                    {
                        if (value.ValueKind != JsonValueKind.Object)
                        {
                            continue;
                        }

                        var color = ReadString(
                            value,
                            "name",
                            "value",
                            "value_name",
                            "valueName",
                            "prop_value",
                            "propertyValue",
                            "color",
                            "colorValue");
                        AddAll(color, value);
                    }
                }
            }
        }

        return result;
    }

    private static string? ReadNestedString(JsonElement element, string objectName, params string[] names)
    {
        if (!element.TryGetProperty(objectName, out var nested) || nested.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return ReadString(nested, names);
    }

    private static FourSellerScheduleProgressDto ParseScheduleProgressResponse(string body)
    {
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
        {
            return new FourSellerScheduleProgressDto();
        }

        TryReadInt(data, out var total, "total");
        TryReadInt(data, out var succeed, "succeed", "success");
        TryReadInt(data, out var failed, "failed");

        var failedList = new List<object>();
        if (data.TryGetProperty("failedList", out var failedEl) &&
            failedEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in failedEl.EnumerateArray())
            {
                failedList.Add(item.ValueKind switch
                {
                    JsonValueKind.String => item.GetString() ?? string.Empty,
                    JsonValueKind.Number => item.ToString(),
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    JsonValueKind.Null => string.Empty,
                    _ => item.GetRawText()
                });
            }
        }

        return new FourSellerScheduleProgressDto
        {
            Total = total,
            Succeed = succeed,
            Failed = failed,
            FailedList = failedList
        };
    }

    private static IReadOnlyList<FourSellerCategoryNodeDto> ParseCategoryListResponse(string body)
    {
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        if (!root.TryGetProperty("data", out var data))
        {
            return [];
        }

        var nodes = ExtractCategoryArray(data);
        return nodes.Select(MapCategoryNode).Where(n => !string.IsNullOrWhiteSpace(n.Id)).ToList();
    }

    private static IReadOnlyList<FourSellerBrandDto> ParseBrandListResponse(string body)
    {
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var brands = new List<FourSellerBrandDto>();
        foreach (var item in data.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var brandId = ReadString(item, "brandId", "brand_id", "id");
            if (string.IsNullOrWhiteSpace(brandId))
            {
                continue;
            }

            var displayName = ReadString(item, "displayName", "display_name", "originalName", "name")
                ?? brandId;
            brands.Add(new FourSellerBrandDto
            {
                BrandId = brandId.Trim(),
                OriginalName = ReadString(item, "originalName", "original_name"),
                DisplayName = displayName.Trim(),
                Status = ReadString(item, "status")
            });
        }

        return brands;
    }

    private static IReadOnlyList<FourSellerCategoryAttributeDto> ParseCategoryAttributeListResponse(string body)
    {
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        if (!root.TryGetProperty("data", out var data))
        {
            return [];
        }

        var attrs = new List<FourSellerCategoryAttributeDto>();
        foreach (var item in ExtractCategoryAttributeArray(data))
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            // Prefer attrId — bare "id" is the 4Seller row id, not TikTok attribute id.
            var attributeId = ReadString(
                item,
                "attrId",
                "attributeId",
                "attribute_id");
            var attributeName = ReadString(
                item,
                "attrName",
                "attributeName",
                "attribute_name",
                "name");
            if (string.IsNullOrWhiteSpace(attributeId) && string.IsNullOrWhiteSpace(attributeName))
            {
                continue;
            }

            var isRequired = ReadFlag01(
                item,
                "isMandatory",
                "is_mandatory",
                "isRequired",
                "is_required",
                "is_requried",
                "required");
            var isMultiple = ReadFlag01(
                item,
                "isMultipleSelected",
                "is_multiple_selected",
                "isMultiple",
                "is_multiple",
                "isMultipleSelection",
                "is_multiple_selection");
            var isCustomizable = ReadFlag01(
                item,
                "isCustomized",
                "is_customized",
                "isCustomizable",
                "is_customizable",
                "customizable");

            var values = ParseCategoryAttributeValues(item);
            var attrType = ReadString(item, "attrType", "attributeType", "attribute_type", "inputType");
            var requirementConditionsJson = ReadRequirementConditionsJson(item);

            attrs.Add(new FourSellerCategoryAttributeDto
            {
                AttributeId = (attributeId ?? attributeName ?? string.Empty).Trim(),
                AttributeName = (attributeName ?? attributeId ?? string.Empty).Trim(),
                IsRequired = isRequired,
                IsMultiple = isMultiple,
                IsCustomizable = isCustomizable,
                AttributeType = attrType,
                RequirementConditionsJson = requirementConditionsJson,
                Values = values
            });
        }

        return attrs;
    }

    /// <summary>
    /// Reads <c>requirementConditions</c> as a compact JSON string (array) for conditional Specifics.
    /// </summary>
    private static string? ReadRequirementConditionsJson(JsonElement item)
    {
        if (!item.TryGetProperty("requirementConditions", out var prop)
            && !item.TryGetProperty("requirement_conditions", out prop))
        {
            return null;
        }

        if (prop.ValueKind == JsonValueKind.String)
        {
            var raw = prop.GetString()?.Trim();
            return string.IsNullOrWhiteSpace(raw) ? null : raw;
        }

        if (prop.ValueKind == JsonValueKind.Array)
        {
            return prop.GetRawText();
        }

        if (prop.ValueKind == JsonValueKind.Null || prop.ValueKind == JsonValueKind.Undefined)
        {
            return null;
        }

        return prop.GetRawText();
    }

    /// <summary>
    /// Reads category attribute enum values from a JSON array or a stringified JSON array
    /// (4Seller returns <c>valueList</c> as an escaped JSON string).
    /// </summary>
    private static List<FourSellerCategoryAttributeValueDto> ParseCategoryAttributeValues(JsonElement item)
    {
        var values = new List<FourSellerCategoryAttributeValueDto>();
        if (!TryGetArrayOrStringifiedArray(
                item,
                out var valuesArr,
                "valueList",
                "values",
                "attributeValues",
                "options"))
        {
            return values;
        }

        foreach (var valueItem in valuesArr.EnumerateArray())
        {
            if (valueItem.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var valueId = ReadString(valueItem, "valueId", "value_id", "id") ?? string.Empty;
            var valueName = ReadString(valueItem, "valueName", "value_name", "name", "value");
            if (string.IsNullOrWhiteSpace(valueName) && string.IsNullOrWhiteSpace(valueId))
            {
                continue;
            }

            values.Add(new FourSellerCategoryAttributeValueDto
            {
                ValueId = valueId.Trim(),
                ValueName = (valueName ?? valueId).Trim()
            });
        }

        return values;
    }

    /// <summary>True when a bool/0/1 flag property is present and set.</summary>
    private static bool ReadFlag01(JsonElement element, params string[] names)
    {
        if (TryReadBool(element, out var b, names) && b)
        {
            return true;
        }

        return TryReadInt(element, out var flag, names) && flag is 1;
    }

    /// <summary>
    /// Finds the first property that is either a JSON array or a string containing a JSON array.
    /// </summary>
    private static bool TryGetArrayOrStringifiedArray(
        JsonElement element,
        out JsonElement array,
        params string[] names)
    {
        foreach (var name in names)
        {
            if (!element.TryGetProperty(name, out var prop))
            {
                continue;
            }

            if (prop.ValueKind == JsonValueKind.Array)
            {
                array = prop;
                return true;
            }

            if (prop.ValueKind == JsonValueKind.String)
            {
                var raw = prop.GetString();
                if (string.IsNullOrWhiteSpace(raw))
                {
                    continue;
                }

                try
                {
                    using var doc = JsonDocument.Parse(raw);
                    if (doc.RootElement.ValueKind == JsonValueKind.Array)
                    {
                        // Clone so the element outlives the disposed document.
                        array = doc.RootElement.Clone();
                        return true;
                    }
                }
                catch (JsonException)
                {
                    // ignore malformed stringified lists
                }
            }
        }

        array = default;
        return false;
    }

    private static IEnumerable<JsonElement> ExtractCategoryAttributeArray(JsonElement data)
    {
        if (data.ValueKind == JsonValueKind.Array)
        {
            return data.EnumerateArray();
        }

        if (data.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        foreach (var key in new[]
                 {
                     "list", "records", "attributes", "attributeList", "productAttributes",
                     "categoryAttributes", "items", "data"
                 })
        {
            if (data.TryGetProperty(key, out var nested) && nested.ValueKind == JsonValueKind.Array)
            {
                return nested.EnumerateArray();
            }
        }

        return [];
    }

    private static bool TryGetFirstArray(JsonElement element, out JsonElement array, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.Array)
            {
                array = prop;
                return true;
            }
        }

        array = default;
        return false;
    }

    private static IReadOnlyList<FourSellerWarehouseDto> ParseWarehouseListResponse(string body)
    {
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var warehouses = new List<FourSellerWarehouseDto>();
        foreach (var item in data.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var warehouseId = ReadString(item, "warehouseId", "warehouse_id");
            if (string.IsNullOrWhiteSpace(warehouseId))
            {
                continue;
            }

            TryReadInt(item, out var warehouseType, "warehouseType", "warehouse_type");
            TryReadInt(item, out var warehouseSubType, "warehouseSubType", "warehouse_sub_type");
            TryReadInt(item, out var effectStatus, "warehouseEffectStatus", "warehouse_effect_status");
            TryReadBool(item, out var isDefault, "isDefault", "is_default");
            TryReadBool(item, out var fbt, "fbt");

            warehouses.Add(new FourSellerWarehouseDto
            {
                WarehouseId = warehouseId.Trim(),
                WarehouseName = ReadString(item, "warehouseName", "warehouse_name") ?? warehouseId.Trim(),
                WarehouseType = warehouseType,
                WarehouseSubType = warehouseSubType,
                WarehouseEffectStatus = effectStatus,
                IsDefault = isDefault,
                Fbt = fbt,
                AllocationMode = ReadString(item, "allocationMode", "allocation_mode")
            });
        }

        return warehouses;
    }

    private static IEnumerable<JsonElement> ExtractCategoryArray(JsonElement data)
    {
        if (data.ValueKind == JsonValueKind.Array)
        {
            return data.EnumerateArray();
        }

        if (data.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        foreach (var key in new[] { "list", "records", "categories", "categoryList", "items" })
        {
            if (data.TryGetProperty(key, out var nested) && nested.ValueKind == JsonValueKind.Array)
            {
                return nested.EnumerateArray();
            }
        }

        return [];
    }

    private static FourSellerCategoryNodeDto MapCategoryNode(JsonElement element)
    {
        // Prefer categoryId — `id` is 4Seller's internal row id, not the TikTok category.
        var id = ReadString(element, "categoryId", "category_id");
        var name = ReadString(element, "categoryName", "category_name", "name");
        var parentId = ReadString(element, "categoryParentId", "category_parent_id", "parentId");
        var nodePathId = ReadString(element, "nodePathId", "node_path_id");
        var nodePath = ReadString(element, "nodePath", "node_path");
        var isLeaf = ResolveIsLeaf(element);

        return new FourSellerCategoryNodeDto
        {
            Id = id ?? string.Empty,
            Name = name ?? string.Empty,
            ParentCategoryId = NormalizeParentId(parentId),
            IsLeaf = isLeaf,
            NodePathId = nodePathId,
            NodePath = nodePath,
            RawPayload = element.GetRawText()
        };
    }

    private static string? NormalizeParentId(string? parentId)
    {
        if (string.IsNullOrWhiteSpace(parentId) || parentId == "0")
        {
            return null;
        }

        return parentId.Trim();
    }

    private static bool ResolveIsLeaf(JsonElement element)
    {
        // Real 4Seller field: leafCategory 0 = has children, 1 = leaf.
        if (TryReadInt(element, out var leafCategory, "leafCategory", "leaf_category"))
        {
            return leafCategory == 1;
        }

        if (TryReadBool(element, out var isLeaf, "isLeaf", "leaf", "is_leaf"))
        {
            return isLeaf;
        }

        if (TryReadBool(element, out var hasChildren, "hasChildren", "has_children", "isParent", "is_parent"))
        {
            return !hasChildren;
        }

        return false;
    }

  private static string? ReadFailedMessage(JsonElement element) =>
        FourSellerFailedMessageReader.Read(element);

    private static string? ReadString(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (!element.TryGetProperty(name, out var prop))
            {
                continue;
            }

            return prop.ValueKind switch
            {
                JsonValueKind.String => prop.GetString(),
                JsonValueKind.Number => prop.ToString(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => null
            };
        }

        return null;
    }

    private static bool TryReadBool(JsonElement element, out bool value, params string[] names)
    {
        foreach (var name in names)
        {
            if (!element.TryGetProperty(name, out var prop))
            {
                continue;
            }

            if (prop.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                value = prop.GetBoolean();
                return true;
            }

            if (prop.ValueKind == JsonValueKind.Number)
            {
                value = prop.GetInt32() != 0;
                return true;
            }

            if (prop.ValueKind == JsonValueKind.String &&
                bool.TryParse(prop.GetString(), out var parsed))
            {
                value = parsed;
                return true;
            }
        }

        value = false;
        return false;
    }

    private static bool TryReadInt(JsonElement element, out int value, params string[] names)
    {
        foreach (var name in names)
        {
            if (!element.TryGetProperty(name, out var prop))
            {
                continue;
            }

            if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out value))
            {
                return true;
            }

            if (prop.ValueKind == JsonValueKind.String &&
                int.TryParse(prop.GetString(), out value))
            {
                return true;
            }
        }

        value = 0;
        return false;
    }

    private static bool TryReadLong(JsonElement element, out long value, params string[] names)
    {
        foreach (var name in names)
        {
            if (!element.TryGetProperty(name, out var prop))
            {
                continue;
            }

            if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt64(out value))
            {
                return true;
            }

            if (prop.ValueKind == JsonValueKind.String &&
                long.TryParse(prop.GetString(), out value))
            {
                return true;
            }
        }

        value = 0;
        return false;
    }

    private static bool TryReadDecimal(JsonElement element, out decimal? value, params string[] names)
    {
        foreach (var name in names)
        {
            if (!element.TryGetProperty(name, out var prop))
            {
                continue;
            }

            if (prop.ValueKind == JsonValueKind.Number && prop.TryGetDecimal(out var numeric))
            {
                value = numeric;
                return true;
            }

            if (prop.ValueKind == JsonValueKind.String &&
                decimal.TryParse(prop.GetString(), out var parsed))
            {
                value = parsed;
                return true;
            }
        }

        value = null;
        return false;
    }

    private static MatchResult? FindMatch(
        FourSellerShopCheckRequest request,
        IReadOnlyList<FourSellerShopRecordDto> records)
    {
        var shopName = NormalizeName(request.ShopName);
        var externalId = NormalizeId(request.ExternalShopId);
        var fourSellerId = NormalizeId(request.FourSellerShopId);

        if (!string.IsNullOrEmpty(fourSellerId))
        {
            var byFourSellerId = records.FirstOrDefault(r =>
                string.Equals(r.Id.ToString(), fourSellerId, StringComparison.OrdinalIgnoreCase));
            if (byFourSellerId is not null)
            {
                return new MatchResult(byFourSellerId, "fourSellerId");
            }
        }

        if (!string.IsNullOrEmpty(externalId))
        {
            var byPartnerId = records.FirstOrDefault(r =>
                string.Equals(r.SellingPartnerId, externalId, StringComparison.OrdinalIgnoreCase));
            if (byPartnerId is not null)
            {
                return new MatchResult(byPartnerId, "sellingPartnerId");
            }
        }

        if (!string.IsNullOrEmpty(fourSellerId))
        {
            var byPartnerId = records.FirstOrDefault(r =>
                string.Equals(r.SellingPartnerId, fourSellerId, StringComparison.OrdinalIgnoreCase));
            if (byPartnerId is not null)
            {
                return new MatchResult(byPartnerId, "sellingPartnerId");
            }
        }

        if (!string.IsNullOrEmpty(shopName))
        {
            var byPlatformName = records.FirstOrDefault(r =>
                NormalizeName(r.PlatformShopName) == shopName);
            if (byPlatformName is not null)
            {
                return new MatchResult(byPlatformName, "platformShopName");
            }

            var byShopName = records.FirstOrDefault(r =>
                NormalizeName(r.ShopName) == shopName ||
                NormalizeName(r.ShopName).StartsWith(shopName, StringComparison.OrdinalIgnoreCase));
            if (byShopName is not null)
            {
                return new MatchResult(byShopName, "platformShopName");
            }
        }

        return null;
    }

    private static bool PlatformMatches(string fourSellerPlatform, string localPlatformCode) =>
        string.Equals(
            fourSellerPlatform.Replace(" ", string.Empty),
            localPlatformCode.Replace(" ", string.Empty),
            StringComparison.OrdinalIgnoreCase);

    private static string NormalizeName(string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToLowerInvariant();

    private static string? NormalizeId(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static FourSellerShopRecordDto MapRecord(FourSellerShopListRecord record) => new()
    {
        Id = record.Id,
        Platform = record.Platform ?? string.Empty,
        PlatformShopName = record.PlatformShopName ?? string.Empty,
        ShopName = record.ShopName ?? string.Empty,
        SellingPartnerId = record.SellingPartnerId ?? string.Empty,
        StatusDesc = record.StatusDesc ?? string.Empty,
        MarketplaceId = record.MarketplaceId ?? string.Empty,
        Currency = record.Currency,
        CreateTime = record.CreateTime
    };

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength] + "...";

    /// <summary>Formats a wall-clock datetime the way 4Seller filters expect (<c>yyyy-MM-dd HH:mm:ss</c>).</summary>
    private static string FormatFourSellerDateTimeNaive(DateTime value) =>
        value.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);

    private sealed record MatchResult(FourSellerShopRecordDto Record, string MatchedBy);

    private abstract class FourSellerApiResponseBase
    {
        public int Code { get; set; }
        public string? Messages { get; set; }
        public bool Success { get; set; }
    }

    private sealed class FourSellerApiResponse<T> : FourSellerApiResponseBase
    {
        public T? Data { get; set; }
    }

    private sealed class FourSellerShopListData
    {
        public List<FourSellerShopListRecord> Records { get; set; } = [];
    }

    private sealed class FourSellerShopListRecord
    {
        public long Id { get; set; }
        public string? Platform { get; set; }
        public string? ShopName { get; set; }
        public string? PlatformShopName { get; set; }
        public string? SellingPartnerId { get; set; }
        public string? StatusDesc { get; set; }
        public string? MarketplaceId { get; set; }
        public string? Currency { get; set; }
        public string? CreateTime { get; set; }
    }

    private sealed class FourSellerApiAuthRedirectBody
    {
        public string PlatformCode { get; set; } = string.Empty;
        public string ShopName { get; set; } = string.Empty;
        public List<string> SiteList { get; set; } = [];
        public string ShopifyShop { get; set; } = string.Empty;
    }
}
