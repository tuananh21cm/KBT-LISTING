using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Application.DTOs.FourSeller;
using Application.DTOs.Shein;
using Application.Interfaces;
using Application.Services;
using Domain.Orders;
using Infrastructure.Persistence;
using Infrastructure.Persistence.Seeding;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Shared.Constants;
using Shared.Helpers;

namespace Api.Controllers;

/// <summary>
/// Dump dữ liệu thô của một đơn để chẩn đoán luồng timeline fulfil: payload gốc 4Seller, payload
/// gốc SHEIN theo từng link, cùng toàn bộ cột mốc và provenance. Mọi field hiển thị trên UI đều
/// là kết quả suy diễn từ những giá trị này, nên khi timeline sai thì đây là nơi duy nhất phân
/// biệt được lỗi trích xuất với lỗi dữ liệu nguồn.
///
/// Chỉ hoạt động khi <c>ASPNETCORE_ENVIRONMENT=Development</c>; ngoài Development mọi action trả
/// 404 để endpoint không tồn tại trên môi trường thật.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/v1/dev/diagnostics")]
[ApiExplorerSettings(IgnoreApi = true)]
public class DevOrderDiagnosticsController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly IWebHostEnvironment _env;
    private readonly ISheinOrderClient _sheinClient;

    /// <summary>Khởi tạo controller chẩn đoán.</summary>
    public DevOrderDiagnosticsController(
        ApplicationDbContext db,
        IWebHostEnvironment env,
        ISheinOrderClient sheinClient)
    {
        _db = db;
        _env = env;
        _sheinClient = sheinClient;
    }

    /// <summary>
    /// Gọi thử SHEIN từ backend bằng session đã lưu trên tài khoản fulfil và proxy riêng của nó.
    /// Đây là bước xác thực bắt buộc trước khi bật chế độ Realtime: nếu SHEIN chặn request từ
    /// .NET (TLS fingerprint khác trình duyệt) thì mọi thứ phía sau đều vô nghĩa.
    ///
    /// Trả về phần đầu payload gốc để đối chiếu shape với những gì extension nhận được.
    /// </summary>
    [HttpGet("shein-probe/{accountEmail}")]
    public async Task<IActionResult> SheinProbe(
        string accountEmail,
        [FromQuery] string? billno,
        [FromQuery] int limit,
        CancellationToken cancellationToken)
    {
        if (!_env.IsDevelopment()) return NotFound();
        if (string.IsNullOrWhiteSpace(accountEmail)) return BadRequest("accountEmail là bắt buộc.");

        var needle = accountEmail.Trim().ToLowerInvariant();
        var account = await _db.FulfilAccounts
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Email != null && a.Email.ToLower() == needle, cancellationToken);

        if (account is null) return NotFound($"Không thấy tài khoản fulfil nào dùng email '{accountEmail}'.");

        var session = SheinSessionContext.TryCreate(
            account.Id,
            account.Name,
            account.Email,
            account.SheinHeadersJson,
            account.SheinCookie,
            account.SheinOrigin,
            account.SheinProxy);

        if (session is null)
        {
            return Ok(new
            {
                account.Name,
                account.Email,
                ok = false,
                reason = "Chưa có session hợp lệ — bấm Sync Order (hoặc Lưu session) trên extension trước.",
                hasHeaders = !string.IsNullOrWhiteSpace(account.SheinHeadersJson),
                hasCookie = !string.IsNullOrWhiteSpace(account.SheinCookie),
                proxy = account.SheinProxy
            });
        }

        var result = string.IsNullOrWhiteSpace(billno)
            ? await _sheinClient.FetchOrderListPageAsync(session, 1, limit > 0 ? limit : 10, cancellationToken)
            : await _sheinClient.FetchOrderDetailAsync(session, billno.Trim(), cancellationToken);

        var preview = result.Payload?.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
        return Ok(new
        {
            account.Name,
            account.Email,
            session.Origin,
            proxy = session.Proxy ?? "(direct)",
            headerCount = session.Headers.Count,
            capturedAt = account.SheinSessionCapturedAt,
            result.Ok,
            result.Error,
            result.SessionInvalid,
            result.ProxyFailed,
            payloadLength = preview?.Length ?? 0,
            payloadPreview = preview is { Length: > 4000 } ? preview[..4000] : preview
        });
    }

    /// <summary>
    /// Dump một đơn theo <paramref name="orderNo"/> (mã đơn sàn) hoặc theo billno SHEIN.
    /// </summary>
    [HttpGet("order/{orderNo}")]
    public async Task<IActionResult> GetOrder(string orderNo, CancellationToken cancellationToken)
    {
        if (!_env.IsDevelopment()) return NotFound();
        if (string.IsNullOrWhiteSpace(orderNo)) return BadRequest("orderNo là bắt buộc.");

        var needle = orderNo.Trim();
        var order = await _db.Orders
            .AsNoTracking()
            .Include(o => o.SupplierPurchases)
            .FirstOrDefaultAsync(
                o => o.PlatformOrderId == needle
                     || o.SupplierPurchases!.Any(l => l.SupplierOrderNo == needle),
                cancellationToken);

        if (order is null) return NotFound($"Không thấy đơn nào khớp '{needle}'.");

        return Ok(new
        {
            order.Id,
            order.PlatformOrderId,
            order.Platform,
            order.OrderStatus,
            order.PlatformOrderStatus,
            order.TrackingNo,
            order.ShippingService,
            order.OrderCreateTime,
            order.OrderPaidTime,
            order.LatestShipDate,
            order.OrderShippedTime,
            order.LatestDeliveryDate,
            milestones = new
            {
                order.FulfilPurchasedAt,
                order.TrackingAddedAt,
                order.InTransitAt,
                order.DeliveredAt,
                order.CanceledAt,
                order.RefundedAt
            },
            order.FulfilTimelineSources,
            SupplierTotal = OrderSupplierCost.SumCharged(order.SupplierPurchases, p => p.SupplierTotal),
            SupplierSubtotal = OrderSupplierCost.SumCharged(order.SupplierPurchases, p => p.SupplierSubtotal),
            order.FulfilAccountId,
            order.FulfilCardId,
            order.LastSyncedAt,
            fourSellerRawPayload = order.RawPayload,
            supplierLinks = order.SupplierPurchases!
                .OrderBy(l => l.CreatedAt)
                .Select(l => new
                {
                    l.Id,
                    l.SupplierOrderNo,
                    l.RelationBillno,
                    l.ConfluenceBatchNo,
                    l.TrackingNo,
                    l.Carrier,
                    l.TrackingKind,
                    l.OrderedAt,
                    l.SupplierPaidAt,
                    l.SupplierUnpaid,
                    l.SupplierTotal,
                    l.SupplierStatus,
                    l.SupplierStatusRaw,
                    l.SupplierStatusAt,
                    l.AutoMarkShipStatus,
                    l.AutoMarkShipTrackingNo,
                    l.AutoMarkShipAttemptedAt,
                    l.AutoMarkShipError,
                    l.CreatedAt,
                    l.UpdatedAt,
                    l.RawPayloadAt,
                    sheinRawPayload = l.RawPayload
                })
                .ToList()
        });
    }

    /// <summary>
    /// Đối chiếu trạng thái hoàn của một đơn với **case return thật** trên 4Seller.
    ///
    /// Đây là chỗ duy nhất phân biệt được ba nguyên nhân khiến đơn hiện `REFUND` mà thiếu mốc hoàn:
    /// case còn đang mở (chưa hoàn tiền), case đã đóng không hoàn, hay case đã hoàn nhưng payload
    /// không mang clock nào. `POST /api/returns/tiktok/page` chỉ nhận cookie seller nên không thể
    /// gọi tay từ browser — phải đi qua cookie của đúng owner đơn.
    ///
    /// Đọc không watermark để thấy cả case cũ mà pull incremental đã đi qua.
    /// </summary>
    /// <param name="fourSeller">Client 4Seller (cookie theo owner).</param>
    /// <param name="platformOrderId">Mã đơn sàn.</param>
    /// <param name="maxPages">Số trang quét, mặc định 10.</param>
    /// <param name="cancellationToken">Token huỷ request.</param>
    [HttpGet("after-sales/{platformOrderId}")]
    public async Task<IActionResult> AfterSalesCases(
        [FromServices] IFourSellerApiService fourSeller,
        string platformOrderId,
        [FromQuery] int maxPages,
        CancellationToken cancellationToken)
    {
        if (!_env.IsDevelopment()) return NotFound();
        if (string.IsNullOrWhiteSpace(platformOrderId)) return BadRequest("platformOrderId là bắt buộc.");

        var needle = platformOrderId.Trim();
        var order = await _db.Orders
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.PlatformOrderId == needle, cancellationToken);

        if (order is null) return NotFound($"Không thấy đơn nào khớp '{needle}'.");

        var pages = maxPages > 0 ? Math.Min(maxPages, AfterSalesSyncSettingKeys.RepairMaxPagesPerOwner) : 10;
        var all = new List<FourSellerReturnRecordDto>();
        var seen = new HashSet<long>();
        string? readError = null;

        try
        {
            for (var page = 1; page <= pages; page++)
            {
                var response = await fourSeller.GetTikTokReturnPageAsync(
                    order.OwnerId, page, AfterSalesSyncSettingKeys.PageSize, cancellationToken);
                if (response.Records.Count == 0) break;

                foreach (var record in response.Records)
                {
                    if (seen.Add(record.Id)) all.Add(record);
                }

                if (response.Records.Count < AfterSalesSyncSettingKeys.PageSize) break;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            readError = ex.Message;
        }

        // Cùng luật khớp đơn như pull: erpOrderId trước, rồi platformOrderId.
        var matched = all
            .Where(r =>
                (r.ErpOrderId > 0 && r.ErpOrderId == order.FourSellerOrderId)
                || string.Equals(r.PlatformOrderId?.Trim(), needle, StringComparison.Ordinal))
            .OrderByDescending(r => r.PlatformCreateTime ?? DateTime.MinValue)
            .ThenByDescending(r => r.Id)
            .ToList();

        static object Describe(FourSellerReturnRecordDto r) => new
        {
            r.Id,
            r.ReturnId,
            r.PlatformOrderId,
            r.ErpOrderId,
            r.PlatformReturnStatus,
            classified = TikTokReturnStatuses.Classify(r.PlatformReturnStatus).ToString(),
            r.ReturnStatus,
            r.Status,
            r.ReturnType,
            r.PlatformOrderStatus,
            r.ReturnReason,
            r.RefundTotal,
            r.Currency,
            r.PlatformCreateTime,
            r.PlatformUpdateTime
        };

        return Ok(new
        {
            local = new
            {
                order.Id,
                order.PlatformOrderId,
                order.FourSellerOrderId,
                order.OwnerId,
                order.PlatformOrderStatus,
                order.WorkflowTab,
                order.RefundedAt,
                order.CanceledAt,
                order.CancelReason,
                order.ReturnStatus,
                order.FulfilTimelineSources
            },
            read = new { pagesScanned = pages, returnsRead = all.Count, readError },
            matchedCount = matched.Count,
            // Case quyết định trạng thái hiện tại — pull chỉ áp case này.
            latest = matched.Count > 0 ? Describe(matched[0]) : null,
            cases = matched.Select(Describe).ToList()
        });
    }

    /// <summary>
    /// So cột denormalize <c>WorkflowTab</c> + cờ tín hiệu với kết quả tính lại từ
    /// <see cref="OrderWorkflowTabs.Resolve"/> và <see cref="OrderFulfilHandlingSignals"/>.
    ///
    /// Tab hiển thị và tab dùng để lọc/đếm giờ đọc từ cột, nên chỉ đáng tin khi cột khớp hàm gốc.
    /// Endpoint này là bản chạy tay của verifier định kỳ; truyền <c>repair=true</c> để ghi lại
    /// những dòng lệch.
    /// </summary>
    /// <param name="verifier">Verifier dùng chung với job định kỳ.</param>
    /// <param name="max">Số đơn quét, mặc định 2000; 0 = toàn bộ.</param>
    /// <param name="repair">Ghi lại cột cho những dòng lệch.</param>
    /// <param name="cancellationToken">Token huỷ request.</param>
    [HttpGet("workflow-signal-drift")]
    public async Task<IActionResult> WorkflowSignalDrift(
        [FromServices] IOrderWorkflowSignalsVerifier verifier,
        [FromQuery] int max,
        [FromQuery] bool repair,
        CancellationToken cancellationToken)
    {
        if (!_env.IsDevelopment()) return NotFound();

        var report = await verifier.VerifyAsync(max > 0 ? max : 2000, repair, cancellationToken);
        return Ok(report);
    }

    /// <summary>
    /// Bóc JSON gốc SHEIN của một billno thành danh sách trường thời gian và trường trạng thái,
    /// kèm đường dẫn đầy đủ. Trả lời trực tiếp ba câu hỏi đang chặn thiết kế timeline: SHEIN dùng
    /// múi giờ nào cho chuỗi wall-clock, có mốc riêng cho việc gán mã carrier không, và trường
    /// logistics thật sự tên là gì. Chỉ trả trường thời gian/trạng thái nên không lộ địa chỉ
    /// hay thông tin người mua.
    /// </summary>
    [HttpGet("shein-raw-analysis/{billno}")]
    public async Task<IActionResult> AnalyzeSheinRaw(string billno, CancellationToken cancellationToken)
    {
        if (!_env.IsDevelopment()) return NotFound();
        if (string.IsNullOrWhiteSpace(billno)) return BadRequest("billno là bắt buộc.");

        var needle = billno.Trim();
        var link = await _db.SupplierPurchases
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.SupplierOrderNo == needle, cancellationToken);

        if (link is null) return NotFound($"Không thấy link SHEIN '{needle}'.");
        if (string.IsNullOrWhiteSpace(link.RawPayload))
        {
            return Ok(new
            {
                captured = false,
                message = "Link chưa có payload gốc. Bật extension.shein_sync.capture_raw_payload "
                          + "rồi chạy Sync Order một lượt."
            });
        }

        using var envelope = JsonDocument.Parse(link.RawPayload);
        var root = envelope.RootElement;

        return Ok(new
        {
            captured = true,
            link.RawPayloadAt,
            listItem = AnalyzeRawFragment(ReadEnvelopeString(root, "listItem")),
            detail = AnalyzeRawFragment(ReadEnvelopeString(root, "detail"))
        });
    }

    /// <summary>
    /// Chạy lại đợt dựng mốc Transit/Giao từ payload gốc, dùng khi cần kiểm chứng sau khi sửa quy
    /// tắc trích xuất. Xoá cờ đánh dấu rồi chạy lại nên có thể gọi nhiều lần.
    /// </summary>
    [HttpPost("rerun-shein-timeline-backfill")]
    public async Task<IActionResult> RerunSheinTimelineBackfill(
        [FromServices] ILogger<DevOrderDiagnosticsController> logger,
        CancellationToken cancellationToken)
    {
        if (!_env.IsDevelopment()) return NotFound();

        var marker = await _db.Settings
            .FirstOrDefaultAsync(s => s.Key == SheinTimelineBackfill.CompletedSettingKey, cancellationToken);
        if (marker is not null)
        {
            marker.Value = string.Empty;
            await _db.SaveChangesAsync(cancellationToken);
        }

        await SheinTimelineBackfill.RunAsync(_db, logger, cancellationToken);
        return Ok(new { ok = true });
    }

    /// <summary>
    /// Quét toàn bộ payload gốc đã thu để kiểm chứng ngữ nghĩa các mốc thời gian SHEIN trên nhiều
    /// đơn thay vì suy diễn từ một mẫu. Với mỗi link trả về trạng thái gói cùng ba mốc
    /// <c>delivery_time</c>, <c>signed_time</c> và <c>track_info.time</c>, đủ để trả lời câu hỏi
    /// then chốt: đơn chưa giao có <c>delivery_time</c> hay không.
    /// </summary>
    [HttpGet("shein-raw-survey")]
    public async Task<IActionResult> SurveySheinRaw(CancellationToken cancellationToken)
    {
        if (!_env.IsDevelopment()) return NotFound();

        var links = await _db.SupplierPurchases
            .AsNoTracking()
            .Where(l => l.RawPayload != null)
            .Join(
                _db.Orders.AsNoTracking(),
                l => l.OrderId,
                o => o.Id,
                (l, o) => new
                {
                    l.SupplierOrderNo,
                    l.RawPayload,
                    o.OrderShippedTime,
                    o.OrderCreateTime,
                    o.InTransitAt,
                    o.DeliveredAt,
                    o.FulfilPurchasedAt,
                    o.FulfilTimelineSources,
                })
            .ToListAsync(cancellationToken);

        var rows = new List<object>();
        foreach (var link in links)
        {
            try
            {
                using var envelope = JsonDocument.Parse(link.RawPayload!);
                var listJson = ReadEnvelopeString(envelope.RootElement, "listItem");
                var detailJson = ReadEnvelopeString(envelope.RootElement, "detail");

                JsonElement? listRoot = null;
                JsonElement? detailOrder = null;
                JsonDocument? listDoc = null;
                JsonDocument? detailDoc = null;

                if (!string.IsNullOrWhiteSpace(listJson))
                {
                    listDoc = JsonDocument.Parse(listJson);
                    listRoot = listDoc.RootElement;
                }
                if (!string.IsNullOrWhiteSpace(detailJson))
                {
                    detailDoc = JsonDocument.Parse(detailJson);
                    if (detailDoc.RootElement.TryGetProperty("order", out var ord)) detailOrder = ord;
                }

                var source = detailOrder ?? listRoot;
                if (source is null) continue;

                rows.Add(new
                {
                    billno = link.SupplierOrderNo,
                    packageState = FindFirstString(source.Value, "packageState"),
                    orderStatusTitle = ReadString(listRoot, "orderStatusTitle"),
                    trackStatusDesc = ReadNestedString(listRoot, "track_info", "status_desc"),
                    goodsStatus = FindFirstString(source.Value, "status"),
                    addTime = ReadUnix(source.Value, "addTime"),
                    deliveryTime = ReadUnix(source.Value, "delivery_time"),
                    signedTime = FindFirstUnix(source.Value, "signed_time"),
                    trackInfoTime = ReadNestedUnix(listRoot, "track_info", "time"),
                    fourSellerShippedTime = link.OrderShippedTime?.ToString("yyyy-MM-dd HH:mm:ss"),
                    fourSellerCreateTime = link.OrderCreateTime?.ToString("yyyy-MM-dd HH:mm:ss"),
                    dbInTransitAt = link.InTransitAt?.ToString("yyyy-MM-dd HH:mm:ss"),
                    dbDeliveredAt = link.DeliveredAt?.ToString("yyyy-MM-dd HH:mm:ss"),
                    dbPurchasedAt = link.FulfilPurchasedAt?.ToString("yyyy-MM-dd HH:mm:ss"),
                    dbSources = link.FulfilTimelineSources,
                });

                listDoc?.Dispose();
                detailDoc?.Dispose();
            }
            catch (JsonException)
            {
                rows.Add(new { billno = link.SupplierOrderNo, parseError = true });
            }
        }

        return Ok(new { count = rows.Count, rows });
    }

    /// <summary>
    /// Chạy <see cref="SheinRawTimelineExtractor"/> trên payload đã lưu của một billno và trả kết
    /// quả thô. Cần tách bạch hai khả năng khi mốc Transit trống: payload không có clock, hay bộ
    /// trích xuất đọc không ra clock đang có trong payload.
    /// </summary>
    [HttpGet("shein-extract/{billno}")]
    public async Task<IActionResult> ExtractSheinTimeline(string billno, CancellationToken cancellationToken)
    {
        if (!_env.IsDevelopment()) return NotFound();

        var needle = (billno ?? string.Empty).Trim();
        var link = await _db.SupplierPurchases
            .AsNoTracking()
            .Where(l => l.SupplierOrderNo == needle)
            .Select(l => new { l.RawPayload, l.SupplierStatus, l.Order.InTransitAt })
            .FirstOrDefaultAsync(cancellationToken);

        if (link?.RawPayload is null) return NotFound($"Link '{needle}' chưa có payload gốc.");

        var parsed = SheinRawTimelineExtractor.Extract(link.RawPayload);
        using var envelope = JsonDocument.Parse(link.RawPayload);
        var listJson = ReadEnvelopeString(envelope.RootElement, "listItem");
        var detailJson = ReadEnvelopeString(envelope.RootElement, "detail");

        static object FragmentInfo(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return new { present = false };
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                var order = root.TryGetProperty("order", out var o) ? o : root;
                var hasPackages = order.ValueKind == JsonValueKind.Object
                    && order.TryGetProperty("order_package_info_list", out var pkgs)
                    && pkgs.ValueKind == JsonValueKind.Array;
                return new
                {
                    present = true,
                    parsed = true,
                    chars = json.Length,
                    hasOrderWrapper = root.TryGetProperty("order", out _),
                    hasPackageList = hasPackages,
                    packageCount = hasPackages
                        ? order.GetProperty("order_package_info_list").GetArrayLength()
                        : 0,
                    rootDeliveryTime = ReadUnix(order, "delivery_time")
                };
            }
            catch (JsonException ex)
            {
                return new { present = true, parsed = false, chars = json.Length, error = ex.Message };
            }
        }

        return Ok(new
        {
            billno = needle,
            link.SupplierStatus,
            dbInTransitAt = link.InTransitAt,
            extractedShippedAt = parsed.ShippedAt,
            extractedDeliveredAt = parsed.DeliveredAt,
            listItem = FragmentInfo(listJson),
            detail = FragmentInfo(detailJson)
        });
    }

    /// <summary>
    /// Đo thứ tự thật giữa mốc Track (mã vận đơn lên sàn, 4Seller) và mốc Transit (hàng rời kho
    /// SHEIN) trên toàn bộ đơn có đủ hai mốc.
    ///
    /// Câu hỏi cần trả lời bằng số liệu chứ không bằng suy luận: hai mốc này có quan hệ nhân quả cố
    /// định (phải có mã rồi mới vận chuyển) hay là hai sự kiện độc lập có thể đảo thứ tự. Nếu đảo
    /// thứ tự chỉ xảy ra lẻ tẻ thì khả năng cao là lỗi ánh xạ; nếu chiếm tỷ lệ đáng kể ở cả hai
    /// chiều thì đó là bản chất của luồng dropship.
    /// </summary>
    [HttpGet("track-transit-order")]
    public async Task<IActionResult> SurveyTrackTransitOrder(CancellationToken cancellationToken)
    {
        if (!_env.IsDevelopment()) return NotFound();

        var rows = await _db.Orders
            .AsNoTracking()
            .Where(o => o.TrackingAddedAt != null && o.InTransitAt != null)
            .Select(o => new
            {
                o.PlatformOrderId,
                Track = o.TrackingAddedAt!.Value,
                Transit = o.InTransitAt!.Value,
                o.OrderShippedTime,
                o.FulfilTimelineSources
            })
            .ToListAsync(cancellationToken);

        static string SourceOf(string? sources, string key)
        {
            if (string.IsNullOrWhiteSpace(sources)) return "legacy";
            try
            {
                using var doc = JsonDocument.Parse(sources);
                return doc.RootElement.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String
                    ? v.GetString() ?? "legacy"
                    : "legacy";
            }
            catch (JsonException)
            {
                return "legacy";
            }
        }

        var transitFirst = rows.Where(r => r.Transit < r.Track).ToList();
        var trackFirst = rows.Where(r => r.Track <= r.Transit).ToList();

        static object GapStats(IEnumerable<TimeSpan> gaps)
        {
            var hours = gaps.Select(g => Math.Abs(g.TotalHours)).OrderBy(h => h).ToList();
            if (hours.Count == 0) return new { count = 0 };
            return new
            {
                count = hours.Count,
                medianHours = Math.Round(hours[hours.Count / 2], 2),
                avgHours = Math.Round(hours.Average(), 2),
                maxHours = Math.Round(hours[^1], 2)
            };
        }

        return Ok(new
        {
            totalWithBoth = rows.Count,
            transitBeforeTrack = new
            {
                count = transitFirst.Count,
                percent = rows.Count == 0 ? 0 : Math.Round(100.0 * transitFirst.Count / rows.Count, 1),
                gap = GapStats(transitFirst.Select(r => r.Track - r.Transit))
            },
            trackBeforeOrEqualTransit = new
            {
                count = trackFirst.Count,
                percent = rows.Count == 0 ? 0 : Math.Round(100.0 * trackFirst.Count / rows.Count, 1),
                gap = GapStats(trackFirst.Select(r => r.Transit - r.Track))
            },
            // Track khớp đúng OrderShippedTime chứng minh mốc này là clock 4Seller, không phải suy diễn.
            trackEqualsFourSellerShippedTime = rows.Count(r =>
                r.OrderShippedTime.HasValue && r.OrderShippedTime.Value == r.Track),
            trackSourceBreakdown = rows
                .GroupBy(r => SourceOf(r.FulfilTimelineSources, "trackingAddedAt"))
                .ToDictionary(g => g.Key, g => g.Count()),
            // Mã hiện tại chỉ đóng Transit với nguồn shein. Nhóm nào mang nguồn khác là tàn dư của
            // phiên bản cũ, và cần biết nó có trùng khít clock 4Seller (tức Transit chỉ là bản sao
            // của Track, khoảng cách vô nghĩa) hay không.
            byTransitSource = rows
                .GroupBy(r => SourceOf(r.FulfilTimelineSources, "inTransitAt"))
                .Select(g => new
                {
                    source = g.Key,
                    count = g.Count(),
                    equalsTrackExactly = g.Count(r => r.Transit == r.Track),
                    equalsFourSellerShippedTime = g.Count(r =>
                        r.OrderShippedTime.HasValue && r.OrderShippedTime.Value == r.Transit),
                    gapAfterTrack = GapStats(g.Select(r => r.Transit - r.Track))
                })
                .ToList()
        });
    }

    private static string? ReadString(JsonElement? root, string name) =>
        root is { } r && r.ValueKind == JsonValueKind.Object && r.TryGetProperty(name, out var v)
            ? ScalarText(v)
            : null;

    private static string? ReadNestedString(JsonElement? root, string outer, string inner) =>
        root is { } r && r.ValueKind == JsonValueKind.Object && r.TryGetProperty(outer, out var o)
        && o.ValueKind == JsonValueKind.Object && o.TryGetProperty(inner, out var v)
            ? ScalarText(v)
            : null;

    private static string? ReadUnix(JsonElement root, string name) =>
        root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out var v)
            ? ToIsoUnix(ScalarText(v))
            : null;

    private static string? ReadNestedUnix(JsonElement? root, string outer, string inner) =>
        ToIsoUnix(ReadNestedString(root, outer, inner));

    /// <summary>Tìm giá trị đầu tiên của một khoá ở bất kỳ độ sâu nào.</summary>
    private static string? FindFirstString(JsonElement node, string name, int depth = 0)
    {
        if (depth > 6) return null;
        switch (node.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var p in node.EnumerateObject())
                {
                    if (p.Name == name && IsScalar(p.Value)) return ScalarText(p.Value);
                }
                foreach (var p in node.EnumerateObject())
                {
                    var found = FindFirstString(p.Value, name, depth + 1);
                    if (found is not null) return found;
                }
                return null;
            case JsonValueKind.Array:
                foreach (var item in node.EnumerateArray())
                {
                    var found = FindFirstString(item, name, depth + 1);
                    if (found is not null) return found;
                }
                return null;
            default:
                return null;
        }
    }

    private static string? FindFirstUnix(JsonElement node, string name) =>
        ToIsoUnix(FindFirstString(node, name));

    private static string? ToIsoUnix(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        if (!long.TryParse(raw, out var value) || value <= 0) return null;
        if (value > 100000000000L) value /= 1000;
        if (value < 946684800L || value > 4102444800L) return null;
        // Always emit Z so diagnostics are not mistaken for VN wall-clock.
        return DateTimeOffset
            .FromUnixTimeSeconds(value)
            .ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Trả một nhánh con của JSON gốc theo đường dẫn dạng <c>order.orderGoodsList.0.goods_operation_list</c>.
    /// Dùng khi cần đọc trọn một cấu trúc (ví dụ nhật ký sự kiện logistics) mà bộ lọc theo tên
    /// khoá không dựng lại được. Khoá chứa thông tin cá nhân bị thay bằng <c>[redacted]</c>.
    /// </summary>
    [HttpGet("shein-raw-path/{billno}")]
    public async Task<IActionResult> ReadSheinRawPath(
        string billno,
        [FromQuery] string path,
        [FromQuery] string fragment,
        CancellationToken cancellationToken)
    {
        if (!_env.IsDevelopment()) return NotFound();
        if (string.IsNullOrWhiteSpace(billno)) return BadRequest("billno là bắt buộc.");
        if (string.IsNullOrWhiteSpace(path)) return BadRequest("path là bắt buộc.");

        var needle = billno.Trim();
        var link = await _db.SupplierPurchases
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.SupplierOrderNo == needle, cancellationToken);

        if (link?.RawPayload is null) return NotFound($"Link '{needle}' chưa có payload gốc.");

        using var envelope = JsonDocument.Parse(link.RawPayload);
        var fragmentName = string.Equals(fragment, "listItem", StringComparison.OrdinalIgnoreCase)
            ? "listItem"
            : "detail";
        var json = ReadEnvelopeString(envelope.RootElement, fragmentName);
        if (string.IsNullOrWhiteSpace(json)) return NotFound($"Mảnh '{fragmentName}' rỗng.");

        using var doc = JsonDocument.Parse(json);
        var current = doc.RootElement;
        foreach (var segment in path.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            if (int.TryParse(segment, out var index))
            {
                if (current.ValueKind != JsonValueKind.Array || index >= current.GetArrayLength())
                {
                    return NotFound($"Không đi được tới '{segment}' trong '{path}'.");
                }
                current = current[index];
            }
            else
            {
                if (current.ValueKind != JsonValueKind.Object
                    || !current.TryGetProperty(segment, out var next))
                {
                    return NotFound($"Không đi được tới '{segment}' trong '{path}'.");
                }
                current = next;
            }
        }

        return Ok(new { fragment = fragmentName, path, value = Redact(current, 0) });
    }

    /// <summary>Sao chép nhánh JSON, thay giá trị của khoá chứa thông tin cá nhân.</summary>
    private static object? Redact(JsonElement node, int depth)
    {
        if (depth > 8) return "[depth-limit]";

        switch (node.ValueKind)
        {
            case JsonValueKind.Object:
                var obj = new Dictionary<string, object?>();
                foreach (var prop in node.EnumerateObject())
                {
                    obj[prop.Name] = PiiKeyRegex.IsMatch(prop.Name)
                        ? "[redacted]"
                        : Redact(prop.Value, depth + 1);
                }
                return obj;

            case JsonValueKind.Array:
                var arr = new List<object?>();
                foreach (var item in node.EnumerateArray())
                {
                    arr.Add(Redact(item, depth + 1));
                    if (arr.Count >= 60) break;
                }
                return arr;

            case JsonValueKind.Null:
                return null;
            case JsonValueKind.True:
                return true;
            case JsonValueKind.False:
                return false;
            case JsonValueKind.Number:
                return node.TryGetInt64(out var l) ? l : node.GetDouble();
            default:
                return node.ToString();
        }
    }

    private static string? ReadEnvelopeString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String
            ? el.GetString()
            : null;

    /// <summary>Phân loại một mảnh JSON gốc; trả lỗi rõ ràng khi mảnh đã bị cắt nên không parse được.</summary>
    private static object AnalyzeRawFragment(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new { present = false };

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            return new { present = true, parsed = false, chars = json.Length, error = ex.Message };
        }

        using (doc)
        {
            var times = new List<object>();
            var states = new List<object>();
            Walk(doc.RootElement, "$", times, states, 0);

            return new
            {
                present = true,
                parsed = true,
                chars = json.Length,
                timeFieldCount = times.Count,
                timeFields = times,
                stateFieldCount = states.Count,
                stateFields = states
            };
        }
    }

    private static void Walk(JsonElement node, string path, List<object> times, List<object> states, int depth)
    {
        if (depth > 12 || times.Count > 400 || states.Count > 400) return;

        switch (node.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var prop in node.EnumerateObject())
                {
                    var childPath = $"{path}.{prop.Name}";
                    if (IsScalar(prop.Value))
                    {
                        if (TimeKeyRegex.IsMatch(prop.Name))
                        {
                            times.Add(new
                            {
                                path = childPath,
                                key = prop.Name,
                                kind = ClassifyTimeValue(prop.Value),
                                value = ScalarText(prop.Value)
                            });
                        }
                        else if (StateKeyRegex.IsMatch(prop.Name) && !PiiKeyRegex.IsMatch(prop.Name))
                        {
                            states.Add(new { path = childPath, key = prop.Name, value = ScalarText(prop.Value) });
                        }
                    }
                    else
                    {
                        Walk(prop.Value, childPath, times, states, depth + 1);
                    }
                }
                break;

            case JsonValueKind.Array:
                var i = 0;
                foreach (var item in node.EnumerateArray())
                {
                    Walk(item, $"{path}[{i++}]", times, states, depth + 1);
                    if (i > 40) break;
                }
                break;
        }
    }

    private static bool IsScalar(JsonElement el) =>
        el.ValueKind is JsonValueKind.String or JsonValueKind.Number
            or JsonValueKind.True or JsonValueKind.False or JsonValueKind.Null;

    private static string ScalarText(JsonElement el) =>
        el.ValueKind == JsonValueKind.Null ? "" : el.ToString();

    /// <summary>
    /// Phân loại giá trị thời gian. Phân biệt được unix (tuyệt đối, tin cậy) với chuỗi naive
    /// (thiếu múi giờ, phải hiệu chuẩn) là điều kiện cần để chốt múi giờ SHEIN.
    /// </summary>
    private static string ClassifyTimeValue(JsonElement el)
    {
        if (el.ValueKind == JsonValueKind.Null) return "null";

        if (el.ValueKind == JsonValueKind.Number && el.TryGetDouble(out var n))
        {
            if (n >= 1e12) return "unixMillis";
            if (n >= 1e9) return "unixSeconds";
            return n == 0 ? "zero" : "number";
        }

        var s = el.ToString().Trim();
        if (s.Length == 0) return "empty";
        if (long.TryParse(s, out var asLong))
        {
            if (asLong >= 1_000_000_000_000L) return "unixMillisAsString";
            if (asLong >= 1_000_000_000L) return "unixSecondsAsString";
            return asLong == 0 ? "zero" : "numericString";
        }
        if (IsoOffsetRegex.IsMatch(s)) return "isoWithOffset";
        if (NaiveWallRegex.IsMatch(s)) return "naiveWallClock";
        return "otherString";
    }

    private static readonly Regex TimeKeyRegex =
        new("time|date|_at$|At$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex StateKeyRegex =
        new("status|state|track|carrier|shipping|logistic|deliver|package|express|operation",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Khoá mang thông tin cá nhân người mua. SHEIN đặt tên địa chỉ dưới tiền tố <c>shipping_</c>
    /// nên bộ lọc trạng thái ở trên quét trúng cả họ tên, điện thoại và địa chỉ — chặn ở đây để
    /// công cụ chẩn đoán không in dữ liệu khách ra log.
    /// </summary>
    private static readonly Regex PiiKeyRegex =
        new("name|phone|tel$|telephone|mobile|email|address|street|postcode|zipcode|passport|card_?no|id_?card",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex IsoOffsetRegex =
        new(@"([zZ]|[+-]\d{2}:?\d{2})$", RegexOptions.Compiled);

    private static readonly Regex NaiveWallRegex =
        new(@"^\d{4}-\d{2}-\d{2}([ T]\d{2}:\d{2}(:\d{2})?)?$", RegexOptions.Compiled);

    /// <summary>
    /// Đọc các setting điều khiển đồng bộ SHEIN, để xác nhận cờ chẩn đoán đã bật hay chưa.
    /// </summary>
    [HttpGet("shein-sync-settings")]
    public async Task<IActionResult> GetSheinSyncSettings(CancellationToken cancellationToken)
    {
        if (!_env.IsDevelopment()) return NotFound();

        var settings = await _db.Settings
            .AsNoTracking()
            .Where(s => s.Key.StartsWith("extension.shein_sync."))
            .OrderBy(s => s.Key)
            .Select(s => new { s.Key, s.Value })
            .ToListAsync(cancellationToken);

        return Ok(settings);
    }

    /// <summary>
    /// Bật/tắt hai setting chẩn đoán mà không cần vào admin UI. Cố tình chỉ cho phép đúng hai
    /// khoá này — mọi khoá khác bị từ chối để endpoint không thành cửa sau sửa cấu hình.
    /// </summary>
    [HttpPost("shein-capture")]
    public async Task<IActionResult> SetSheinCapture(
        [FromQuery] bool enabled,
        [FromQuery] string? forceBillnos,
        CancellationToken cancellationToken)
    {
        if (!_env.IsDevelopment()) return NotFound();

        var wanted = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [ExtensionSheinSyncSettingKeys.CaptureRawPayload] = enabled ? "true" : "false",
            [ExtensionSheinSyncSettingKeys.ForceBillnos] = (forceBillnos ?? string.Empty).Trim()
        };

        var rows = await _db.Settings
            .Where(s => wanted.Keys.Contains(s.Key))
            .ToListAsync(cancellationToken);

        foreach (var row in rows)
        {
            row.Value = wanted[row.Key];
            row.UpdatedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(cancellationToken);

        var missing = wanted.Keys.Except(rows.Select(r => r.Key)).ToList();
        return Ok(new
        {
            updated = rows.Select(r => new { r.Key, r.Value }).ToList(),
            missing
        });
    }

    /// <summary>
    /// Liệt kê link SHEIN đã có payload gốc, để biết lượt sync nào đã thu được dữ liệu.
    /// </summary>
    [HttpGet("captured-links")]
    public async Task<IActionResult> ListCapturedLinks(CancellationToken cancellationToken)
    {
        if (!_env.IsDevelopment()) return NotFound();

        var links = await _db.SupplierPurchases
            .AsNoTracking()
            .Where(l => l.RawPayload != null)
            .OrderByDescending(l => l.RawPayloadAt)
            .Take(50)
            .Select(l => new
            {
                l.SupplierOrderNo,
                platformOrderId = l.Order.PlatformOrderId,
                l.RawPayloadAt,
                rawPayloadChars = l.RawPayload!.Length
            })
            .ToListAsync(cancellationToken);

        return Ok(new { count = links.Count, links });
    }

    /// <summary>
    /// Trạng thái đầy đủ của một dòng <c>SupplierPurchases</c> theo billno, kèm
    /// <c>SkipEvidenceJson</c> đã giải mã.
    ///
    /// Bằng chứng near-miss là câu trả lời có thẩm quyền cho "vì sao billno này không khớp": nó do
    /// chính luồng sync ghi tại thời điểm bỏ qua, nên nó phản ánh **pool thật của lô đó**. Dựng lại
    /// pool sau đó bằng <c>shein-match-explain</c> chỉ là xấp xỉ — cửa sổ pool của sync suy từ mốc mua
    /// sớm nhất của lô, còn endpoint kia suy từ <c>lookbackDays</c>, nên hai bên có thể nói khác nhau.
    /// </summary>
    /// <param name="billno">Mã đơn NCC cần tra.</param>
    /// <param name="cancellationToken">Token huỷ request.</param>
    [HttpGet("supplier-purchase/{billno}")]
    public async Task<IActionResult> GetSupplierPurchase(
        string billno,
        CancellationToken cancellationToken)
    {
        if (!_env.IsDevelopment()) return NotFound();
        if (string.IsNullOrWhiteSpace(billno)) return BadRequest("billno là bắt buộc.");

        var needle = billno.Trim();
        var row = await _db.SupplierPurchases
            .AsNoTracking()
            .Where(p => p.SupplierOrderNo == needle)
            .Select(p => new
            {
                p.SupplierOrderNo,
                p.Source,
                p.MatchState,
                p.SkipReason,
                p.SkipEvidenceJson,
                p.IsCharged,
                p.SupplierTotal,
                p.SupplierSubtotal,
                p.Currency,
                p.OrderedAt,
                p.SupplierPaidAt,
                p.SupplierUnpaid,
                p.IsMembershipOrVirtual,
                p.SupplierOrderType,
                p.SupplierStatus,
                p.TrackingNo,
                p.TrackingKind,
                p.PhoneKey,
                p.ZipKey,
                p.NameKey,
                p.FirstSeenAt,
                p.LastSeenAt,
                p.LastRunId,
                p.MatchedAt,
                orderId = p.OrderId,
                platformOrderId = p.Order != null ? p.Order.PlatformOrderId : null,
                orderCreateTime = p.Order != null ? p.Order.OrderCreateTime : null
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (row is null) return NotFound($"Không có dòng mua nào cho billno '{needle}'.");

        object? evidence = null;
        if (!string.IsNullOrWhiteSpace(row.SkipEvidenceJson))
        {
            try
            {
                evidence = JsonSerializer.Deserialize<JsonElement>(row.SkipEvidenceJson);
            }
            catch (JsonException)
            {
                evidence = row.SkipEvidenceJson;
            }
        }

        return Ok(new { purchase = row, evidence });
    }

    /// <summary>
    /// Thống kê bảng <c>SupplierPurchases</c> theo nguồn / trạng thái khớp, kèm đối chiếu tổng tiền
    /// được tính (<c>IsCharged</c>) với tổng dòng chi CostUpdate trên sổ cái.
    ///
    /// Hai con số cuối phải bằng nhau. Lệch nhau nghĩa là có lần mua tính vào giá vốn mà không có
    /// đồng nào trong sổ cái (hoặc ngược lại) — đúng triệu chứng "đã mua mà không có trong sổ cái".
    /// </summary>
    [HttpGet("supplier-purchases-summary")]
    public async Task<IActionResult> SupplierPurchasesSummary(CancellationToken cancellationToken)
    {
        if (!_env.IsDevelopment()) return NotFound();

        var byState = await _db.SupplierPurchases
            .AsNoTracking()
            .GroupBy(p => new { p.Source, p.MatchState, p.IsCharged })
            .Select(g => new
            {
                g.Key.Source,
                g.Key.MatchState,
                g.Key.IsCharged,
                count = g.Count(),
                total = g.Sum(p => p.SupplierTotal)
            })
            .ToListAsync(cancellationToken);

        var bySkipReason = await _db.SupplierPurchases
            .AsNoTracking()
            .Where(p => p.OrderId == null)
            .GroupBy(p => p.SkipReason)
            .Select(g => new { skipReason = g.Key ?? "(null)", count = g.Count() })
            .ToListAsync(cancellationToken);

        var chargedTotal = await _db.SupplierPurchases
            .AsNoTracking()
            .Where(p => p.IsCharged && p.OrderId != null)
            .SumAsync(p => p.SupplierTotal ?? 0m, cancellationToken);

        var ledgerTotal = await _db.FulfilTransactions
            .AsNoTracking()
            .Where(t => t.Source == "CostUpdate")
            .SumAsync(t => t.Amount, cancellationToken);

        var ledgerUnattributed = await _db.FulfilTransactions
            .AsNoTracking()
            .CountAsync(
                t => t.Source == "CostUpdate" && t.SupplierPurchaseId == null,
                cancellationToken);

        // Cờ IsCharged do migration tính bằng SQL, còn nguồn sự thật là OrderSupplierCost.SelectCharged
        // viết bằng C#. Hai bản dịch của cùng một luật lệch nhau là cách giá mua sai âm thầm, nên
        // chạy lại luật C# trên dữ liệu thật rồi so từng dòng.
        var graphs = await _db.Orders
            .AsNoTracking()
            .Where(o => o.SupplierPurchases!.Any())
            .Include(o => o.SupplierPurchases)
            .ToListAsync(cancellationToken);

        // Bất biến quy kết: Orders.FulfilAccountId là bản sao phi chuẩn hoá của tài khoản trên lần
        // mua, chỉ tồn tại để danh sách đơn hiện cột «TK mua» mà không phải join bảng con. Một đơn
        // có lần mua mang tài khoản mà cột này trống nghĩa là có đường đổi chủ lần mua quên gọi
        // OrderPurchaseEvidence.SyncAttributionWithPurchases — sổ cái vẫn đúng tiền nên lỗi này chỉ
        // lộ ra trên UI, không lộ trong bất kỳ con số nào ở trên.
        var attributionDrift = graphs
            .Where(o => o.FulfilAccountId is null
                && o.SupplierPurchases!.Any(p => p.FulfilAccountId != null))
            .Select(o => new
            {
                orderId = o.Id,
                platformOrderId = o.PlatformOrderId,
                purchaseAccountIds = o.SupplierPurchases!
                    .Where(p => p.FulfilAccountId != null)
                    .Select(p => p.FulfilAccountId!.Value)
                    .Distinct()
                    .ToList()
            })
            .ToList();

        var drift = new List<object>();
        foreach (var order in graphs)
        {
            var stored = order.SupplierPurchases!.Where(p => p.IsCharged).Select(p => p.Id).ToHashSet();
            var expected = OrderSupplierCost.SelectCharged(order.SupplierPurchases!).Select(p => p.Id).ToHashSet();
            if (stored.SetEquals(expected)) continue;

            drift.Add(new
            {
                orderId = order.Id,
                platformOrderId = order.PlatformOrderId,
                storedBillnos = order.SupplierPurchases!
                    .Where(p => stored.Contains(p.Id))
                    .Select(p => p.SupplierOrderNo ?? $"manual:{p.Id}")
                    .ToList(),
                expectedBillnos = order.SupplierPurchases!
                    .Where(p => expected.Contains(p.Id))
                    .Select(p => p.SupplierOrderNo ?? $"manual:{p.Id}")
                    .ToList()
            });
        }

        return Ok(new
        {
            byState,
            bySkipReason,
            chargedTotal,
            ledgerTotal,
            ledgerDelta = chargedTotal - ledgerTotal,
            ledgerUnattributed,
            ordersChecked = graphs.Count,
            chargedDriftCount = drift.Count,
            chargedDrift = drift.Take(20).ToList(),
            attributionDriftCount = attributionDrift.Count,
            attributionDrift = attributionDrift.Take(20).ToList()
        });
    }

    /// <summary>
    /// 20 lượt Sync Order gần nhất, gộp các lô rời rạc theo <c>RunId</c>.
    ///
    /// Bảng tổng hợp trả lời câu hỏi đầu tiên khi một lượt trông sai: tổng khớp + bỏ qua có bằng
    /// số đơn đã lấy không, và bao nhiêu đơn đã khớp vẫn phải gọi lại detail.
    /// </summary>
    [HttpGet("shein-sync-runs")]
    public async Task<IActionResult> ListSheinSyncRuns(CancellationToken cancellationToken)
    {
        if (!_env.IsDevelopment()) return NotFound();

        var recentRunIds = await _db.SheinSyncRunLogs
            .AsNoTracking()
            .GroupBy(x => x.RunId)
            .Select(g => new { RunId = g.Key, StartedAt = g.Min(x => x.CreatedAt) })
            .OrderByDescending(x => x.StartedAt)
            .Take(20)
            .ToListAsync(cancellationToken);

        var ids = recentRunIds.Select(x => x.RunId).ToList();
        var rows = await _db.SheinSyncRunLogs
            .AsNoTracking()
            .Where(x => ids.Contains(x.RunId))
            .ToListAsync(cancellationToken);

        var runs = rows
            .GroupBy(x => x.RunId)
            .Select(BuildRunSummary)
            .OrderByDescending(r => r.StartedAt)
            .ToList();

        return Ok(new { count = runs.Count, runs });
    }

    /// <summary>
    /// Chi tiết một lượt: từng billno bị bỏ qua kèm lý do, và từng billno phase list bắt gọi
    /// detail kèm điều kiện đã bật. Đây là nơi đọc ra "billno nào không khớp pool" thay vì đoán
    /// từ con số tổng.
    /// </summary>
    [HttpGet("shein-sync-runs/{runId}")]
    public async Task<IActionResult> GetSheinSyncRun(string runId, CancellationToken cancellationToken)
    {
        if (!_env.IsDevelopment()) return NotFound();
        if (string.IsNullOrWhiteSpace(runId)) return BadRequest("runId là bắt buộc.");

        var needle = runId.Trim();
        var rows = await _db.SheinSyncRunLogs
            .AsNoTracking()
            .Where(x => x.RunId == needle)
            .OrderBy(x => x.CreatedAt)
            .ToListAsync(cancellationToken);

        if (rows.Count == 0) return NotFound($"Không có nhật ký nào cho runId '{runId}'.");

        var skips = new List<object>();
        var needsDetail = new List<object>();
        foreach (var row in rows)
        {
            foreach (var skip in DeserializeOrEmpty<List<SkipOutcomeRow>>(row.SkipOutcomesJson))
            {
                skips.Add(new { row.Phase, row.BatchIndex, skip.Billno, skip.Reason, skip.Detail });
            }

            foreach (var entry in DeserializeOrEmpty<Dictionary<string, string>>(row.NeedsDetailReasonsJson))
            {
                needsDetail.Add(new
                {
                    row.Phase,
                    row.BatchIndex,
                    billno = entry.Key,
                    reason = entry.Value
                });
            }
        }

        return Ok(new
        {
            summary = BuildRunSummary(rows.GroupBy(x => x.RunId).First()),
            batches = rows.Select(x => new
            {
                x.Phase,
                x.BatchIndex,
                x.Fetched,
                x.Matched,
                x.Unmatched,
                x.Unchanged,
                x.UpdatedTracking,
                x.UpdatedCost,
                x.PoolSize,
                x.DurationMs,
                x.CreatedAt
            }),
            skips,
            needsDetail
        });
    }

    /// <summary>
    /// Giải thích vì sao một billno bị bỏ với lý do <c>no_local_match</c>.
    ///
    /// Luồng khớp đòi phone <b>và</b> zip trùng tuyệt đối, nên khi trượt thì con số tổng chỉ nói
    /// "không khớp" mà không nói lệch ở đâu. Billno chưa khớp thì cũng không có link nên payload
    /// SHEIN không nằm trong DB — vì vậy phải truyền phone/zip/tên đọc từ trang đơn SHEIN vào đây,
    /// rồi endpoint dựng lại đúng pool của luồng sync và xếp các đơn sàn thành ba nhóm: trùng cả
    /// hai, trùng phone lệch zip, trùng zip lệch phone. Nhóm nào có kết quả thì đó chính là chỗ dữ
    /// liệu lệch cần sửa.
    ///
    /// Dùng chung <see cref="SheinMatchKeys"/> với luồng sync để chẩn đoán không nói khác thực tế.
    /// Chỉ trả khoá đã chuẩn hoá của các đơn trùng ít nhất một khoá, không dump địa chỉ cả pool.
    /// </summary>
    /// <param name="phone">Số điện thoại người nhận trên đơn SHEIN.</param>
    /// <param name="zip">Mã bưu chính trên đơn SHEIN.</param>
    /// <param name="name">Họ tên người nhận trên đơn SHEIN (tuỳ chọn, dùng để chấm điểm phụ).</param>
    /// <param name="mode">
    /// <c>all</c> (mặc định) dựng pool mọi đơn không huỷ/hoàn trong <paramref name="lookbackDays"/>;
    /// <c>new</c> dựng pool đơn đang fulfil như chế độ Chỉ đơn mới.
    /// </param>
    /// <param name="lookbackDays">Cửa sổ pool; bỏ trống thì lấy theo Settings.</param>
    /// <param name="cancellationToken">Token huỷ request.</param>
    [HttpGet("shein-match-explain")]
    public async Task<IActionResult> ExplainSheinMatch(
        [FromQuery] string? phone,
        [FromQuery] string? zip,
        [FromQuery] string? name,
        [FromQuery] string? mode,
        [FromQuery] int? lookbackDays,
        CancellationToken cancellationToken)
    {
        if (!_env.IsDevelopment()) return NotFound();

        var phoneKey = SheinMatchKeys.PhoneKey(phone);
        var zipKey = SheinMatchKeys.ZipKey(zip);
        var nameKey = SheinMatchKeys.NameKey(name);
        if (phoneKey.Length == 0 && zipKey.Length == 0)
        {
            return BadRequest("Cần ít nhất một trong hai tham số phone hoặc zip.");
        }

        var isUpdateAll = !string.Equals(mode?.Trim(), "new", StringComparison.OrdinalIgnoreCase);
        var days = lookbackDays ?? await ResolveLookbackDaysAsync(cancellationToken);
        days = Math.Clamp(
            days,
            ExtensionSheinSyncSettingKeys.MinRefreshLookbackDays,
            ExtensionSheinSyncSettingKeys.MaxRefreshLookbackDays);
        var poolSince = DateTime.UtcNow.AddDays(-days);

        var canceledStatuses = new[]
        {
            PlatformOrderStatuses.Cancelled, "CANCELED", "canceled", "cancelled",
            PlatformOrderStatuses.Refund, "REFUNDED", "refunded",
        };
        var fulfilStatuses = PlatformOrderStatuses.FulfilCandidateStatuses;

        var query = _db.Orders.AsNoTracking().Include(o => o.SupplierPurchases).AsSplitQuery();
        var pool = isUpdateAll
            ? await query
                .Where(o => (o.OrderCreateTime == null || o.OrderCreateTime >= poolSince)
                    && (o.PlatformOrderStatus == null
                        || o.PlatformOrderStatus == ""
                        || !canceledStatuses.Contains(o.PlatformOrderStatus)))
                .ToListAsync(cancellationToken)
            : await query
                .Where(o => (o.OrderCreateTime == null || o.OrderCreateTime >= poolSince)
                    && ((o.PlatformOrderStatus != null && fulfilStatuses.Contains(o.PlatformOrderStatus))
                        || ((o.PlatformOrderStatus == null || o.PlatformOrderStatus == "")
                            && (o.OrderStatus == "to_ship"
                                || o.OrderStatus == "in_process"
                                || o.OrderStatus == "pending"
                                || o.OrderStatus == "on_hold"))))
                .ToListAsync(cancellationToken);

        var scored = pool
            .Select(o => new MatchCandidate(
                o,
                SheinMatchKeys.PhoneKey(o.BuyerPhone),
                SheinMatchKeys.ZipKey(o.PostalCode),
                SheinMatchKeys.NameKey(o.BuyerName)))
            .ToList();

        object Describe(MatchCandidate row) => new
        {
            row.Order.PlatformOrderId,
            row.Order.PlatformOrderStatus,
            row.Order.OrderStatus,
            row.Order.OrderCreateTime,
            row.Order.FulfilAccountId,
            phoneKey = row.PhoneKey,
            zipKey = row.ZipKey,
            nameKey = row.NameKey,
            nameMatches = nameKey.Length > 0 && nameKey == row.NameKey,
            linkedBillnos = (row.Order.SupplierPurchases ?? [])
                .Select(l => new { l.SupplierOrderNo, l.SupplierStatus, l.SupplierUnpaid })
                .ToList()
        };

        var phoneHits = phoneKey.Length > 0
            ? scored.Where(r => r.PhoneKey == phoneKey).ToList()
            : new List<MatchCandidate>();
        var zipHits = zipKey.Length > 0
            ? scored.Where(r => r.ZipKey == zipKey).ToList()
            : new List<MatchCandidate>();
        var exact = phoneHits.Where(r => r.ZipKey == zipKey).ToList();

        // Đơn đã có link thì kể cả khớp phone+zip vẫn phải qua CanAttachAdditionalLink, nên nêu
        // rõ ở đây để không kết luận sai là "khớp được mà không gắn".
        return Ok(new
        {
            input = new { phoneKey, zipKey, nameKey },
            mode = isUpdateAll ? "all" : "new",
            lookbackDays = days,
            poolSize = pool.Count,
            verdict = exact.Count switch
            {
                0 when phoneHits.Count == 0 && zipHits.Count == 0 =>
                    "Không đơn nào trùng phone hay zip — đơn sàn nằm ngoài pool (quá Lookback / đã huỷ-hoàn) hoặc chưa sync về.",
                0 when phoneHits.Count > 0 =>
                    "Trùng phone nhưng lệch zip — so cột zipKey của ứng viên với input.",
                0 =>
                    "Trùng zip nhưng lệch phone — so cột phoneKey của ứng viên với input.",
                1 => "Có đúng một đơn khớp phone+zip; nếu sync vẫn bỏ qua thì nguyên nhân nằm ở guard sau bước khớp.",
                _ => "Nhiều đơn khớp phone+zip — khớp nhập nhằng, cần họ tên hoặc thời điểm đặt để phân giải."
            },
            exactMatches = exact.Select(Describe).ToList(),
            phoneMatchZipMismatch = phoneHits.Where(r => r.ZipKey != zipKey).Take(20).Select(Describe).ToList(),
            zipMatchPhoneMismatch = zipHits.Where(r => r.PhoneKey != phoneKey).Take(20).Select(Describe).ToList()
        });
    }

    /// <summary>Đơn sàn trong pool kèm ba khoá khớp đã chuẩn hoá.</summary>
    private sealed record MatchCandidate(
        Domain.Entities.Order Order,
        string PhoneKey,
        string ZipKey,
        string NameKey);

    /// <summary>Lookback hiện hành trong Settings, rơi về mặc định khi thiếu hoặc sai kiểu.</summary>
    private async Task<int> ResolveLookbackDaysAsync(CancellationToken cancellationToken)
    {
        var raw = await _db.Settings
            .AsNoTracking()
            .Where(s => s.Key == ExtensionSheinSyncSettingKeys.RefreshLookbackDays)
            .Select(s => s.Value)
            .FirstOrDefaultAsync(cancellationToken);
        return int.TryParse(raw, out var parsed) && parsed > 0
            ? parsed
            : ExtensionSheinSyncSettingKeys.DefaultRefreshLookbackDays;
    }

    /// <summary>Gộp các lô của một lượt thành một dòng tổng, kèm histogram theo lý do.</summary>
    private static RunSummary BuildRunSummary(IGrouping<string, Domain.Entities.SheinSyncRunLog> group)
    {
        var rows = group.ToList();
        var skipCounts = new Dictionary<string, int>();
        var needsDetailCounts = new Dictionary<string, int>();

        foreach (var row in rows)
        {
            foreach (var pair in DeserializeOrEmpty<Dictionary<string, int>>(row.SkipCountsJson))
            {
                skipCounts[pair.Key] = skipCounts.GetValueOrDefault(pair.Key) + pair.Value;
            }

            foreach (var pair in DeserializeOrEmpty<Dictionary<string, string>>(row.NeedsDetailReasonsJson))
            {
                // total_delta mang kèm hai số tiền, gộp histogram theo phần mã đứng trước.
                var reason = pair.Value.Split(':', 2)[0];
                needsDetailCounts[reason] = needsDetailCounts.GetValueOrDefault(reason) + 1;
            }
        }

        var listRows = rows.Where(r => r.Phase == "list").ToList();
        var listFetched = listRows.Sum(r => r.Fetched);
        var listAccounted = listRows.Sum(r => r.Matched + r.Unmatched + r.Unchanged);

        return new RunSummary
        {
            RunId = group.Key,
            StartedAt = rows.Min(r => r.CreatedAt),
            UpdateMode = rows[0].UpdateMode,
            LookbackDays = rows[0].LookbackDays,
            FulfilAccountName = rows.Select(r => r.FulfilAccountName).FirstOrDefault(n => n != null),
            Batches = rows.Count,
            ListFetched = listFetched,
            DetailFetched = rows.Where(r => r.Phase == "detail").Sum(r => r.Fetched),
            Matched = rows.Sum(r => r.Matched),
            Unmatched = rows.Sum(r => r.Unmatched),
            Unchanged = rows.Sum(r => r.Unchanged),
            UpdatedTracking = rows.Sum(r => r.UpdatedTracking),
            UpdatedCost = rows.Sum(r => r.UpdatedCost),
            PoolSize = rows.Max(r => r.PoolSize),
            DurationMs = rows.Sum(r => r.DurationMs),
            SkipCounts = skipCounts,
            NeedsDetailCounts = needsDetailCounts,
            // Bất biến phase list: mọi billno phải rơi vào đúng một nhóm.
            ListBalanced = listFetched == listAccounted,
            ListUnaccounted = listFetched - listAccounted
        };
    }

    private static T DeserializeOrEmpty<T>(string? json) where T : new()
    {
        if (string.IsNullOrWhiteSpace(json)) return new T();
        try
        {
            return JsonSerializer.Deserialize<T>(json, DiagnosticsJsonOptions) ?? new T();
        }
        catch (JsonException)
        {
            return new T();
        }
    }

    /// <summary>
    /// Đối chiếu bucket <c>all</c> với 6 bucket rời trên cùng tài khoản + cùng khoảng ngày.
    ///
    /// Luồng sync đơn chỉ còn chạy một truy vấn <c>orderStatus=all</c> cho mỗi tài khoản 4Seller.
    /// Cả thiết kế đó đứng trên một giả định mà code không tự kiểm chứng được: <c>all</c> là
    /// <b>tập cha</b> của mọi bucket. Nếu 4Seller loại <c>pending</c> (đơn chưa trả tiền) khỏi tab
    /// All Orders thì ta ngừng đồng bộ cả một nhóm đơn mà không có lỗi nào bắn ra — im lặng và
    /// chỉ lộ ra khi ai đó thấy thiếu đơn.
    ///
    /// Chỉ đọc, không ghi DB. <c>missingFromAll</c> rỗng nghĩa là giả định đúng.
    /// </summary>
    /// <param name="fourSeller">Client 4Seller (cookie theo owner).</param>
    /// <param name="ownerUserId">Giới hạn một tài khoản; bỏ trống = mọi tài khoản LoggedIn.</param>
    /// <param name="lookbackDays">Cửa sổ create-date giờ VN, mặc định 2.</param>
    /// <param name="maxPages">Trần trang mỗi bucket, mặc định 5 (tối đa 20).</param>
    /// <param name="cancellationToken">Token huỷ request.</param>
    [HttpGet("order-page-status-coverage")]
    public async Task<IActionResult> OrderPageStatusCoverage(
        [FromServices] IFourSellerApiService fourSeller,
        [FromQuery] Guid? ownerUserId,
        [FromQuery] int lookbackDays,
        [FromQuery] int maxPages,
        CancellationToken cancellationToken)
    {
        if (!_env.IsDevelopment()) return NotFound();

        var days = lookbackDays > 0 ? lookbackDays : 2;
        var pageCap = maxPages > 0 ? Math.Min(maxPages, 20) : 5;

        var owners = await _db.Users
            .AsNoTracking()
            .Where(u =>
                u.FourSellerCookie != null
                && u.FourSellerCookie.Trim() != string.Empty
                && u.FourSellerCookieLoginStatus == FourSellerCookieLoginStatuses.LoggedIn
                && (ownerUserId == null || u.Id == ownerUserId))
            .Select(u => new { u.Id, u.UserName })
            .ToListAsync(cancellationToken);

        if (owners.Count == 0) return NotFound("Không có tài khoản 4Seller nào đang LoggedIn.");

        var (dateFrom, dateTo) = AppTimeZones.GetVietnamLookbackUtcRange(
            days, OrderSyncSettingKeys.MaxStatusLookbackDays);
        var (pacificFrom, pacificTo) = AppTimeZones.ToPacificLocalRange(dateFrom, dateTo);

        var report = new List<object>();
        var missingTotal = 0;
        foreach (var owner in owners)
        {
            var allRead = new Dictionary<long, string?>();
            var allTotal = await ReadCoverageBucketAsync(
                fourSeller, owner.Id, "all", dateFrom, dateTo, pageCap, allRead, cancellationToken);

            var perBucket = new List<object>();
            var bucketRead = new Dictionary<long, string?>();
            foreach (var bucket in OrderPageCoverageBuckets)
            {
                var ids = new Dictionary<long, string?>();
                var total = await ReadCoverageBucketAsync(
                    fourSeller, owner.Id, bucket, dateFrom, dateTo, pageCap, ids, cancellationToken);
                foreach (var kv in ids) bucketRead[kv.Key] = kv.Value;
                perBucket.Add(new
                {
                    bucket,
                    total,
                    read = ids.Count,
                    // Bucket nào trả về trạng thái khác chính nó nghĩa là 4Seller bỏ qua bộ lọc.
                    returnedStatuses = ids.Values
                        .GroupBy(s => string.IsNullOrWhiteSpace(s) ? "(empty)" : s!)
                        .ToDictionary(g => g.Key, g => g.Count())
                });
            }

            // Đơn mà bucket rời thấy còn "all" không thấy = lỗ hổng thật của thiết kế mới.
            var missing = bucketRead
                .Where(kv => !allRead.ContainsKey(kv.Key))
                .Select(kv => new { orderId = kv.Key, orderStatus = kv.Value })
                .ToList();
            missingTotal += missing.Count;

            report.Add(new
            {
                ownerUserId = owner.Id,
                owner.UserName,
                allTotal,
                allRead = allRead.Count,
                bucketUnionRead = bucketRead.Count,
                perBucket,
                allStatusBreakdown = allRead.Values
                    .GroupBy(s => string.IsNullOrWhiteSpace(s) ? "(empty)" : s!)
                    .ToDictionary(g => g.Key, g => g.Count()),
                missingFromAllCount = missing.Count,
                missingFromAll = missing.Take(50)
            });
        }

        return Ok(new
        {
            lookbackDays = days,
            maxPagesPerBucket = pageCap,
            windowUtc = new { from = dateFrom, to = dateTo },
            windowPacific = new { from = pacificFrom, to = pacificTo },
            missingFromAllTotal = missingTotal,
            verdict = missingTotal == 0
                ? "all là tập cha của 6 bucket trên mọi tài khoản đã quét"
                : "CẢNH BÁO: có đơn chỉ bucket rời thấy được",
            owners = report
        });
    }

    private static readonly string[] OrderPageCoverageBuckets =
        ["pending", "on_hold", "to_ship", "in_process", "shipped", "canceled"];

    /// <summary>
    /// Dò một đơn theo <c>orderNo</c> chính xác qua bucket <c>all</c> và cả 6 bucket rời.
    ///
    /// Hai vòng dò đơn lẻ (<c>SyncPlatformStatusAsync</c>, reconcile Mark Ship) từng phải thử lần
    /// lượt từng bucket cho tới khi trúng. Gộp về một truy vấn <c>all</c> chỉ đúng nếu 4Seller
    /// vẫn honour <c>searchValue</c> khi <c>orderStatus=all</c> — endpoint này kiểm chứng đúng
    /// điều đó: <c>foundIn</c> phải chứa <c>all</c>.
    /// </summary>
    /// <param name="fourSeller">Client 4Seller (cookie theo owner).</param>
    /// <param name="orderNo">Mã đơn sàn (<c>PlatformOrderId</c>).</param>
    /// <param name="cancellationToken">Token huỷ request.</param>
    [HttpGet("order-page-search-probe/{orderNo}")]
    public async Task<IActionResult> OrderPageSearchProbe(
        [FromServices] IFourSellerApiService fourSeller,
        string orderNo,
        CancellationToken cancellationToken)
    {
        if (!_env.IsDevelopment()) return NotFound();
        if (string.IsNullOrWhiteSpace(orderNo)) return BadRequest("orderNo là bắt buộc.");

        var needle = orderNo.Trim();

        // "auto": lấy đơn mới nhất của một cookie còn LoggedIn. Đơn cũ hoặc đơn thuộc shop chưa
        // map owner thì cookie không thấy được, probe sẽ trả 0 ở mọi bucket và không nói lên gì.
        var query = _db.Orders.AsNoTracking().Where(o =>
            o.PlatformOrderId != null
            && o.PlatformOrderId != string.Empty
            && _db.Users.Any(u =>
                u.Id == o.OwnerId
                && u.FourSellerCookieLoginStatus == FourSellerCookieLoginStatuses.LoggedIn));

        var order = string.Equals(needle, "auto", StringComparison.OrdinalIgnoreCase)
            ? await query.OrderByDescending(o => o.CreatedAt).FirstOrDefaultAsync(cancellationToken)
            : await query.FirstOrDefaultAsync(o => o.PlatformOrderId == needle, cancellationToken);

        if (order is null) return NotFound($"Không thấy đơn local nào khớp '{needle}'.");
        needle = order.PlatformOrderId!.Trim();

        var probes = new List<object>();
        var foundIn = new List<string>();
        foreach (var bucket in OrderPageCoverageBuckets.Prepend("all"))
        {
            var page = await fourSeller.GetOrderPageAsync(
                order.OwnerId,
                new FourSellerOrderPageRequest
                {
                    OrderStatus = bucket,
                    PageCurrent = 1,
                    PageSize = 10,
                    SearchType = "orderNo",
                    SearchMethod = "exact",
                    SearchValue = needle,
                    Platform = order.Platform?.Trim() ?? string.Empty
                },
                cancellationToken);

            var match = page.Records.FirstOrDefault(r =>
                r.OrderId == order.FourSellerOrderId
                || string.Equals(r.PlatformOrderId?.Trim(), needle, StringComparison.OrdinalIgnoreCase)
                || string.Equals(r.PlatformOrderNo?.Trim(), needle, StringComparison.OrdinalIgnoreCase));
            if (match is not null) foundIn.Add(bucket);
            probes.Add(new
            {
                bucket,
                page.Total,
                records = page.Records.Count,
                matched = match is not null,
                matchedStatus = match?.OrderStatus
            });
        }

        return Ok(new
        {
            orderNo = needle,
            localOrderStatus = order.OrderStatus,
            localPlatformOrderStatus = order.PlatformOrderStatus,
            foundIn,
            allHonoursSearch = foundIn.Contains("all"),
            probes
        });
    }

    /// <summary>Đọc hết trang của một bucket, gom <c>orderId → orderStatus</c>. Trả <c>total</c> của 4Seller.</summary>
    private static async Task<int> ReadCoverageBucketAsync(
        IFourSellerApiService fourSeller,
        Guid ownerId,
        string bucket,
        DateTime dateFrom,
        DateTime dateTo,
        int pageCap,
        Dictionary<long, string?> sink,
        CancellationToken cancellationToken)
    {
        var total = 0;
        for (var page = 1; page <= pageCap; page++)
        {
            var response = await fourSeller.GetOrderPageAsync(
                ownerId,
                new FourSellerOrderPageRequest
                {
                    PageCurrent = page,
                    PageSize = 100,
                    OrderStatus = bucket,
                    DateFrom = dateFrom,
                    DateTo = dateTo
                },
                cancellationToken);

            total = response.Total;
            foreach (var record in response.Records) sink[record.OrderId] = record.OrderStatus;
            if (response.Records.Count == 0 || page * 100 >= total) break;
        }

        return total;
    }

    private static readonly JsonSerializerOptions DiagnosticsJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private sealed class SkipOutcomeRow
    {
        public string Billno { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
        public string? Detail { get; set; }
    }

    private sealed class RunSummary
    {
        public string RunId { get; set; } = string.Empty;
        public DateTime StartedAt { get; set; }
        public string UpdateMode { get; set; } = string.Empty;
        public int LookbackDays { get; set; }
        public string? FulfilAccountName { get; set; }
        public int Batches { get; set; }
        public int ListFetched { get; set; }
        public int DetailFetched { get; set; }
        public int Matched { get; set; }
        public int Unmatched { get; set; }
        public int Unchanged { get; set; }
        public int UpdatedTracking { get; set; }
        public int UpdatedCost { get; set; }
        public int PoolSize { get; set; }
        public int DurationMs { get; set; }
        public Dictionary<string, int> SkipCounts { get; set; } = [];
        public Dictionary<string, int> NeedsDetailCounts { get; set; } = [];
        public bool ListBalanced { get; set; }
        public int ListUnaccounted { get; set; }
    }
}
