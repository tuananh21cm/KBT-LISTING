using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Application.Services.Shein;

/// <summary>
/// Status and milestone side of the SHEIN payload reader, ported from <c>sheinOrders.ts</c>.
/// </summary>
public static partial class SheinOrderNormalizer
{
    /// <summary>
    /// The us.shein.com storefront prints naive wall-clock strings in US Eastern; unix values are
    /// already absolute. Everything stored is UTC.
    /// </summary>
    private static readonly TimeZoneInfo SheinWallZone = ResolveSheinWallZone();

    private static TimeZoneInfo ResolveSheinWallZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        }
        catch (TimeZoneNotFoundException)
        {
            // Windows without ICU falls back to the Win32 id for the same zone.
            return TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
        }
    }

    [GeneratedRegex(@"^(\d{4})-(\d{2})-(\d{2})[T ](\d{2}):(\d{2})(?::(\d{2}))?(?:\.\d+)?$")]
    private static partial Regex NaiveTimestampRegex();

    [GeneratedRegex(@"^\d+(\.\d+)?$")]
    private static partial Regex NumericStringRegex();

    [GeneratedRegex(@"[zZ]$|[+-]\d{2}:?\d{2}$")]
    private static partial Regex AbsoluteOffsetRegex();

    /// <summary>Milestone clocks pulled out of one SHEIN payload.</summary>
    public readonly record struct SheinMilestones(
        DateTime? ShippedAt,
        DateTime? TrackingAt,
        DateTime? DeliveredAt,
        DateTime? CanceledAt,
        DateTime? RefundedAt);

    /// <summary>Status text, normalized group, and the clock of the current status.</summary>
    public readonly record struct SheinStatusInfo(string? Status, string? Group, DateTime? StatusAt);

    /// <summary>
    /// Parses any SHEIN time shape into UTC: unix seconds, unix milliseconds, an ISO string with
    /// an explicit offset, or a naive wall clock (interpreted as US Eastern).
    /// </summary>
    public static DateTime? CoerceTime(JsonNode? node)
    {
        if (node is not JsonValue value)
        {
            return null;
        }

        if (value.TryGetValue<double>(out var number) && double.IsFinite(number))
        {
            return FromUnix(number);
        }

        var raw = SheinJson.Str(value)?.Trim();
        if (string.IsNullOrEmpty(raw))
        {
            return null;
        }

        if (NumericStringRegex().IsMatch(raw))
        {
            return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var n)
                ? FromUnix(n)
                : null;
        }

        if (AbsoluteOffsetRegex().IsMatch(raw))
        {
            return DateTimeOffset.TryParse(
                raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dto)
                ? dto.UtcDateTime
                : null;
        }

        var normalized = raw.Contains('T') ? raw : raw.Replace(' ', 'T');
        var match = NaiveTimestampRegex().Match(normalized);
        if (match.Success)
        {
            var local = new DateTime(
                int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture),
                int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture),
                int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture),
                int.Parse(match.Groups[4].Value, CultureInfo.InvariantCulture),
                int.Parse(match.Groups[5].Value, CultureInfo.InvariantCulture),
                match.Groups[6].Success ? int.Parse(match.Groups[6].Value, CultureInfo.InvariantCulture) : 0,
                DateTimeKind.Unspecified);
            try
            {
                return TimeZoneInfo.ConvertTimeToUtc(local, SheinWallZone);
            }
            catch (ArgumentException)
            {
                // Wall clock inside the spring-forward gap: shift past the missing hour.
                return TimeZoneInfo.ConvertTimeToUtc(local.AddHours(1), SheinWallZone);
            }
        }

        return DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var parsed)
            ? DateTime.SpecifyKind(parsed, DateTimeKind.Utc)
            : null;
    }

    /// <summary>Giới hạn epoch hợp lệ, khớp <c>SheinRawTimelineExtractor.ReadUnix</c>.</summary>
    private const double MinEpochSeconds = 946_684_800;   // 2000-01-01
    private const double MaxEpochSeconds = 4_102_444_800; // 2100-01-01

    /// <summary>
    /// Epoch → UTC, trả <c>null</c> cho mọi giá trị không phải mốc thật.
    ///
    /// SHEIN dùng <c>0</c> làm "chưa có": <c>signed_time: 0</c> nghĩa là gói chưa ai ký nhận,
    /// <c>cancel_time: 0</c> nghĩa là đơn chưa bị huỷ. Nhận 0 rồi trả 1970-01-01 phá ba chỗ cùng
    /// lúc: guard "còn gói chưa ký nhận" của <c>ExtractPackageMilestone</c> đếm gói đó là đã ký nên
    /// đơn bị báo Đã giao sớm; <c>DeliveredAt = 1970</c> làm <c>OrderFulfilTimeline.Normalize</c>
    /// coi mọi clock Track/Transit thật là "muộn hơn mốc Giao" và xoá sạch; và
    /// <c>addTime: 0</c> làm <c>IsPurchasedBeforePlatformOrder</c> chặn khớp vĩnh viễn.
    /// </summary>
    private static DateTime? FromUnix(double value)
    {
        if (!double.IsFinite(value) || value <= 0) return null;

        // SHEIN addTime is ~10 digits (seconds); millisecond payloads are ≥ 1e12.
        var seconds = value < 1e12 ? value : value / 1000;
        if (seconds < MinEpochSeconds || seconds > MaxEpochSeconds) return null;

        try
        {
            return DateTimeOffset.FromUnixTimeMilliseconds((long)(seconds * 1000)).UtcDateTime;
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static DateTime? CoerceTime(JsonObject? obj, params string[] keys) =>
        CoerceTime(SheinJson.Pick(obj, keys));

    /// <summary>First of the given keys that parses into a time.</summary>
    private static DateTime? DirectTime(JsonObject root, params string[] keys)
    {
        foreach (var key in keys)
        {
            var t = CoerceTime(SheinJson.Get(root, key));
            if (t.HasValue)
            {
                return t;
            }
        }

        return null;
    }

    /// <summary>Order create / place time.</summary>
    public static DateTime? ExtractOrderedAt(JsonObject root) =>
        DirectTime(
            root,
            "addTime", "add_time", "order_time", "orderTime", "order_time_stamp", "orderTimestamp",
            "createTime", "create_time", "orderCreateTime", "payment_time", "pay_time", "payTime",
            "order_place_time", "placeTime");

    /// <summary>
    /// The moment SHEIN actually charged the card.
    ///
    /// SHEIN lets an order exist before payment and auto-cancels it when <c>order_expire_time</c>
    /// passes. Those ghost orders carry a normal <c>addTime</c> but a zero pay time, so this is
    /// the only way to tell them from a real purchase. <c>IsUnpaid</c> stays null when the payload
    /// carries no pay key at all — a trimmed payload must not turn a paid order into a ghost.
    /// </summary>
    public static (DateTime? PaidAt, bool? IsUnpaid) ExtractPaidAt(JsonObject root)
    {
        var sawZero = false;
        foreach (var key in new[] { "pay_time", "paymentTime", "payment_time", "payTime" })
        {
            var raw = SheinJson.Get(root, key);
            if (raw is null || SheinJson.TextOf(raw).Length == 0)
            {
                continue;
            }

            var n = SheinJson.ToNumber(raw);
            if (n is <= 0)
            {
                sawZero = true;
                continue;
            }

            var iso = CoerceTime(raw);
            if (iso.HasValue)
            {
                return (iso, false);
            }
        }

        return (null, sawZero ? true : null);
    }

    /// <summary>Status text, group, and clock.</summary>
    public static SheinStatusInfo ExtractStatus(JsonObject root)
    {
        var packageStates = CollectPackageStates(root);
        var goodsSignals = CollectGoodsStatusSignals(root);
        var hasRefundBill = SheinJson.Items(root, "refund_bill_res_list", "refundBillResList").Any();

        // Trạng thái SHEIN thuần của billno (không liên quan TikTok):
        // 1) packageState từ get_order_info  2) `status` từ order/list
        // Không ưu tiên orderStatus (=10 gần như mọi đơn shipped/delivered).
        var rawCandidates = new List<string?>
        {
            packageStates.FirstOrDefault(),
            SheinJson.Text(root, "status"),
            SheinJson.Text(root, "order_status_name"),
            SheinJson.Text(root, "orderStatusName"),
            SheinJson.Text(root, "orderStatusTitle"),
            SheinJson.Text(root, "order_status_title"),
            SheinJson.Text(root, "status_name"),
            SheinJson.Text(root, "statusName"),
            SheinJson.Text(root, "status_desc"),
            SheinJson.Text(root, "statusDesc"),
            hasRefundBill ? "refunded" : null,
            goodsSignals.RefundScene,
            SheinJson.Text(root, "order_status"),
            SheinJson.Text(root, "orderStatus"),
            SheinJson.Text(root, "order_flag"),
            SheinJson.Text(root, "orderFlag")
        };

        var sheinStatus = rawCandidates.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c))?.Trim();

        var blobParts = new List<string?> { sheinStatus };
        blobParts.AddRange(packageStates);
        blobParts.Add(goodsSignals.Blob);
        blobParts.Add(SheinJson.Text(root, "refund_status"));
        blobParts.Add(SheinJson.Text(root, "refundStatus"));
        blobParts.Add(SheinJson.Text(root, "cancel_status"));
        blobParts.Add(SheinJson.Text(root, "cancelStatus"));
        blobParts.Add(SheinJson.Text(root, "return_status"));
        blobParts.Add(SheinJson.Text(root, "returnStatus"));
        blobParts.Add(SheinJson.Text(root, "is_cancel"));
        blobParts.Add(SheinJson.Text(root, "isCancel"));
        blobParts.Add(SheinJson.Text(root, "is_refund"));
        blobParts.Add(SheinJson.Text(root, "isRefund"));
        blobParts.Add(hasRefundBill ? "refunded" : null);

        var blob = string.Join(
            ' ',
            blobParts.Where(v => !string.IsNullOrEmpty(v)).Select(v => v!.Trim().ToLowerInvariant()));

        var group = ClassifyStatusGroup(blob, root, packageStates, hasRefundBill, goodsSignals);

        // update_time / last_update_time are deliberately excluded: they are the API's own sync
        // clock, not a fulfilment event.
        var statusAt = ExtractStatusAt(root, group)
                       ?? CoerceTime(root, "status_time", "statusTime", "order_status_time", "orderStatusTime");

        return new SheinStatusInfo(sheinStatus, group, statusAt);
    }

    /// <summary>
    /// Every milestone clock in one pass, independent of the current status group, so an order
    /// that shipped then delivered keeps both clocks rather than one shared status time. A
    /// missing clock stays null — the backend leaves that milestone empty rather than stamping
    /// the time of the sync run.
    /// </summary>
    public static SheinMilestones ExtractMilestones(JsonObject root)
    {
        var shippedAt = ExtractLogisticsStatusAt(root, "shipped");
        var deliveredAt = ExtractLogisticsStatusAt(root, "delivered");

        // SHEIN emits no clock for assigning a tracking number — the payload has a bare
        // shipping_no. Falling back to shippedAt would weld the Track column to the Transit
        // column; leaving it null lets 4Seller's orderShippedTime own that milestone, which
        // measured ~6.6h later and differed on 144 of 154 sampled orders.
        var trackingAt = DirectTime(root, "track_add_time", "trackAddTime", "tracking_time", "trackingTime", "track_time");

        var refundedAt = ExtractStatusAt(root, "refunded");
        var canceledAt = DirectTime(root, "cancel_time", "canceled_time", "cancelTime", "canceledTime", "cancel_order_time")
                         ?? ExtractStatusAt(root, "canceled");

        return new SheinMilestones(shippedAt, trackingAt, deliveredAt, canceledAt, refundedAt);
    }

    private static List<string> CollectPackageStates(JsonObject root)
    {
        var result = new List<string>();
        foreach (var listKey in new[] { "subOrderStatus", "sub_order_status", "packageStatusList", "package_status_list" })
        {
            foreach (var row in SheinJson.ObjectItems(root, listKey))
            {
                var state = SheinJson.Text(row, "packageState", "package_state", "state", "status");
                if (!string.IsNullOrEmpty(state))
                {
                    result.Add(state);
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Tín hiệu trạng thái gom từ <c>orderGoodsList</c>.
    /// <paramref name="AllRefunded"/> chỉ đúng khi <b>mọi</b> dòng hàng đều có dấu hoàn —
    /// hoàn một phần không được phép biến cả billno thành terminal.
    /// </summary>
    private readonly record struct GoodsStatusSignals(
        string Blob,
        string? RefundScene,
        bool AnyRefunded,
        bool AllRefunded);

    private static GoodsStatusSignals CollectGoodsStatusSignals(JsonObject root)
    {
        var goods = SheinJson.ObjectItems(root, "orderGoodsList", "order_goods_list", "orderGoods", "goodsList").ToList();
        if (goods.Count == 0)
        {
            return new GoodsStatusSignals(string.Empty, null, false, false);
        }

        var parts = new List<string>();
        string? refundScene = null;
        var refundedRows = 0;

        foreach (var row in goods)
        {
            var rowRefunded = false;

            var scene = SheinJson.Text(row, "refund_scene", "refundScene");
            if (!string.IsNullOrEmpty(scene))
            {
                rowRefunded = true;
                refundScene ??= scene;
                parts.Add(scene);
            }

            var refundType = SheinJson.Pick(row, "refund_type", "refundType");
            var refundTypeText = SheinJson.TextOf(refundType);
            if (refundTypeText.Length > 0 && refundTypeText != "0")
            {
                rowRefunded = true;
                parts.Add($"refund_type:{refundTypeText}");
            }

            var gs = SheinJson.Text(row, "goods_status", "goodsStatus", "status", "orderGoodsStauts");
            if (!string.IsNullOrEmpty(gs))
            {
                parts.Add(gs);
            }

            if (rowRefunded) refundedRows++;
        }

        return new GoodsStatusSignals(
            string.Join(' ', parts).ToLowerInvariant(),
            refundScene,
            refundedRows > 0,
            refundedRows == goods.Count);
    }

    private static DateTime? ExtractStatusAt(JsonObject root, string? group)
    {
        if (group is "delivered" or "shipped")
        {
            return ExtractLogisticsStatusAt(root, group);
        }

        if (group is not ("refunded" or "canceled"))
        {
            return null;
        }

        DateTime? latest = null;
        foreach (var bill in SheinJson.ObjectItems(root, "refund_bill_res_list", "refundBillResList"))
        {
            foreach (var log in SheinJson.ObjectItems(bill, "refund_log_list", "refundLogList"))
            {
                var t = CoerceTime(log, "add_time", "addTime");
                if (t.HasValue && (latest is null || t > latest))
                {
                    latest = t;
                }
            }
        }

        if (latest.HasValue)
        {
            return latest;
        }

        foreach (var g in SheinJson.ObjectItems(root, "orderGoodsList", "order_goods_list"))
        {
            foreach (var op in SheinJson.ObjectItems(g, "goods_operation_list", "goodsOperationList"))
            {
                var t = CoerceTime(op, "add_time", "addTime");
                if (t.HasValue && (latest is null || t > latest))
                {
                    latest = t;
                }
            }
        }

        return latest;
    }

    /// <summary>
    /// Ship / sign-off clock read per package.
    ///
    /// A multi-package order needs two different aggregations: it starts moving when the
    /// <em>first</em> package leaves the warehouse, but is only delivered when the <em>last</em>
    /// one is signed for. Taking the wrong end reports "delivered" while a package is still out.
    /// </summary>
    private static DateTime? ExtractPackageMilestone(JsonObject root, string group)
    {
        var packages = SheinJson.ObjectItems(root, "order_package_info_list", "orderPackageInfoList").ToList();
        if (packages.Count == 0)
        {
            return null;
        }

        var keys = group == "delivered"
            ? new[] { "signed_time", "signedTime", "sign_time" }
            : ["deliveryTime", "delivery_time", "ship_time", "shipTime"];

        DateTime? best = null;
        var seen = 0;
        foreach (var pkg in packages)
        {
            var t = DirectTime(pkg, keys);
            if (t is null)
            {
                continue;
            }

            seen++;
            if (best is null)
            {
                best = t;
            }
            else
            {
                best = group == "delivered" ? Max(best.Value, t.Value) : Min(best.Value, t.Value);
            }
        }

        // A package without a sign-off clock means the order is not fully delivered yet.
        if (group == "delivered" && seen < packages.Count)
        {
            return null;
        }

        return best;
    }

    /// <summary>
    /// Delivery clock derived from <c>track_info</c>. The status text must be checked first: for
    /// an in-flight order <c>track_info.time</c> is just the latest scan, so using it blindly
    /// would stamp "delivered" on a parcel still in transit.
    /// </summary>
    private static DateTime? ExtractDeliveredFromTrackInfo(JsonObject root)
    {
        var info = SheinJson.Obj(SheinJson.Pick(root, "track_info", "trackInfo"));
        if (info is null)
        {
            return null;
        }

        var desc = (SheinJson.Text(info, "status_desc", "statusDesc") ?? string.Empty).ToLowerInvariant();
        if (!Regex.IsMatch(desc, "deliver|signed|received"))
        {
            return null;
        }

        return CoerceTime(info, "time", "status_time");
    }

    /// <summary>
    /// Delivered / shipped clock from the SHEIN payload, preferred over the generic status time.
    ///
    /// The two SHEIN fields are easy to confuse; measured across 156 orders with raw payloads:
    /// <c>delivery_time</c> (and <c>order_package_info_list[].deliveryTime</c>) is when the parcel
    /// <em>left the warehouse</em> — 55 of 57 still-shipped orders already carry it. It is
    /// <c>signed_time</c> that marks successful delivery: absent on all 57 shipped orders, present
    /// on all 99 delivered ones, and always later than <c>delivery_time</c> (5.9 days on average,
    /// up to 9.5). So <c>delivery_time</c> belongs to the shipped group and must never feed the
    /// delivered group — mixing them makes the "Giao" column read almost six days early.
    /// </summary>
    private static DateTime? ExtractLogisticsStatusAt(JsonObject root, string group)
    {
        var directKeys = group == "delivered"
            ? new[]
            {
                "signed_time", "sign_time", "signedTime", "signTime", "signing_time", "signingTime",
                "received_time", "receivedTime", "receive_time", "receiveTime", "delivered_time", "deliveredTime"
            }
            : [
                // The real warehouse-exit clock SHEIN emits; the ship_* keys below only cover
                // payload variants not yet seen in production data.
                "delivery_time", "deliveryTime", "ship_time", "shipped_time", "shipping_time",
                "shipTime", "shippedTime", "shippingTime", "dispatch_time", "dispatchTime"
            ];

        // Packages come before order-level fields: on a multi-package order the order-level
        // delivery_time reports the last package out, but the order started moving with the first.
        var fromPackages = ExtractPackageMilestone(root, group);
        if (fromPackages.HasValue)
        {
            return fromPackages;
        }

        var direct = DirectTime(root, directKeys);
        if (direct.HasValue)
        {
            return direct;
        }

        if (group == "delivered")
        {
            var fromTrackInfo = ExtractDeliveredFromTrackInfo(root);
            if (fromTrackInfo.HasValue)
            {
                return fromTrackInfo;
            }
        }

        var stateRegex = group == "delivered"
            ? new Regex(@"\b(delivered|received|signed|completed|complete)\b", RegexOptions.IgnoreCase)
            : new Regex(@"\b(shipped|in_?transit|dispatched|shipping)\b", RegexOptions.IgnoreCase);

        DateTime? best = null;
        foreach (var listKey in new[] { "subOrderStatus", "sub_order_status", "packageStatusList", "package_status_list" })
        {
            foreach (var row in SheinJson.ObjectItems(root, listKey))
            {
                var state = SheinJson.Text(row, "packageState", "package_state", "state", "status");
                if (!string.IsNullOrEmpty(state) && !stateRegex.IsMatch(state))
                {
                    continue;
                }

                var t = group == "delivered"
                    ? CoerceTime(row, "signed_time", "sign_time", "delivered_time", "receive_time", "received_time")
                    : CoerceTime(row, "delivery_time", "deliveryTime", "ship_time", "shipped_time", "dispatch_time");
                if (t is null)
                {
                    continue;
                }

                best = best is null ? t : group == "delivered" ? Max(best.Value, t.Value) : Min(best.Value, t.Value);
            }
        }

        if (best.HasValue)
        {
            return best;
        }

        // goods_operation_list — ops with a delivered/shipped label plus add_time. Real payloads
        // carry bare numeric codes (1, 13, 16, 54…), so this branch rarely fires; only ops with a
        // clear textual label are accepted rather than guessing at unverified codes.
        var opRegex = group == "delivered"
            ? new Regex(@"\b(deliver|receiv|sign|signed|completed)\b", RegexOptions.IgnoreCase)
            : new Regex(@"\b(ship|dispatch|transit|warehouse\s*out|send)\b", RegexOptions.IgnoreCase);

        foreach (var g in SheinJson.ObjectItems(root, "orderGoodsList", "order_goods_list"))
        {
            foreach (var op in SheinJson.ObjectItems(g, "goods_operation_list", "goodsOperationList"))
            {
                var label = string.Join(
                    ' ',
                    new[] { "operation_desc", "operationDesc", "operate_desc", "operateDesc", "status", "type", "operation_type", "operationType" }
                        .Select(k => SheinJson.TextOf(SheinJson.Get(op, k)))
                        .Where(v => v.Length > 0));
                if (!opRegex.IsMatch(label))
                {
                    continue;
                }

                var t = CoerceTime(op, "add_time", "addTime", "time", "operate_time");
                if (t is null)
                {
                    continue;
                }

                best = best is null ? t : group == "delivered" ? Max(best.Value, t.Value) : Min(best.Value, t.Value);
            }
        }

        return best;
    }

    private static DateTime Max(DateTime a, DateTime b) => a > b ? a : b;

    private static DateTime Min(DateTime a, DateTime b) => a < b ? a : b;

    private static string? ClassifyStatusGroup(
        string blob,
        JsonObject root,
        List<string> packageStates,
        bool hasRefundBill,
        GoodsStatusSignals goodsSignals)
    {
        var packageBlob = string.Join(' ', packageStates).ToLowerInvariant();

        // List `status` (khác orderStatus) là mã lifecycle của storefront — 3=processing,
        // 5=shipped, 6=delivered, 13=refunded, 16=hủy. Phải đọc trước chrome
        // "Return&Refund" / leftover refund_type trên hàng, nếu không đơn đã gửi bị gắn
        // refunded rồi kẹt vì rank terminal không cho hạ (GSU1RR21T00NNAA).
        var listStatus = SheinJson.TextOf(SheinJson.Get(root, "status"));
        switch (listStatus)
        {
            case "13":
            case "14":
                return "refunded";
            case "16":
                return "canceled";
            case "6":
                return "delivered";
            case "5":
                return "shipped";
            case "3":
            case "7":
                return "processing";
        }

        // Explicit package / refund-bill / toàn bộ hàng đã hoàn. Không bắt chữ "refund"
        // trần (nút Return&Refund trên đơn shipped).
        if (hasRefundBill || goodsSignals.AllRefunded || Regex.IsMatch(packageBlob, @"\brefunded\b"))
        {
            return "refunded";
        }

        if (Regex.IsMatch(packageBlob, @"\b(cancel+ed|cancelled|canceled)\b")) return "canceled";
        // `common_delivered` không khớp `\bdelivered` vì `_` là ký tự từ — phải bắt tường minh.
        if (Regex.IsMatch(packageBlob, @"common_delivered|\b(delivered|received|signed)\b"))
        {
            return "delivered";
        }

        if (Regex.IsMatch(packageBlob, @"\b(shipped|in_?transit|dispatched)\b")) return "shipped";
        if (Regex.IsMatch(packageBlob, @"\b(processing|packing|preparing)\b")
            || packageBlob.Contains("waiting to be shipped", StringComparison.Ordinal))
        {
            return "processing";
        }

        // Explicit package / refund-bill already handled above.

        // Chỉ nhận chữ nói rõ đơn **đã** hoàn xong. Bản cũ có thêm một nhánh bắt mọi chữ `refund`
        // trong blob, mà blob gộp cả text của từng dòng hàng và mọi nhãn trạng thái — nên
        // "Refund in progress", "Refund failed", "Return requested" hay một dòng hàng hoàn lẻ đều
        // đủ để đẩy cả billno sang terminal, làm mất chi phí và stamp sai mốc Huỷ.
        if (SheinJson.Truthy(SheinJson.Get(root, "is_refund"))
            || SheinJson.Truthy(SheinJson.Get(root, "isRefund"))
            || Regex.IsMatch(blob, @"\b(refunded|full\s*refund|refund\s*(success|completed?|done))\b"))
        {
            return "refunded";
        }

        if (SheinJson.Truthy(SheinJson.Get(root, "is_cancel"))
            || SheinJson.Truthy(SheinJson.Get(root, "isCancel"))
            || Regex.IsMatch(blob, @"\b(cancel+ed|cancelled|canceled|closed|void)\b"))
        {
            return "canceled";
        }

        if (Regex.IsMatch(blob, @"\b(delivered|received|signed|completed|complete|đã\s*giao|đã\s*nhận)\b"))
        {
            return "delivered";
        }

        if (Regex.IsMatch(blob, @"\b(shipped|dispatched|in\s*transit|on\s*the\s*way|out\s*for\s*delivery|đã\s*gửi)\b"))
        {
            return "shipped";
        }

        // Storefront BFF numeric codes (US get_order_info), distinct from the Open API's 1–7.
        // Observed: orderStatus 11 + status "13" = refunded, alongside packageState=refunded.
        // orderStatus 10 + orderStatusTitle "Shipped" chiếm đa số payload đã lưu (không nằm dải 1–7).
        var orderStatusCode = SheinJson.TextOf(SheinJson.Pick(root, "orderStatus", "order_status"));
        var statusCode = SheinJson.TextOf(SheinJson.Get(root, "status"));
        if (RefundCodes.Contains(orderStatusCode) || RefundCodes.Contains(statusCode))
        {
            return "refunded";
        }

        // orderStatus=10 xuất hiện cả trên đơn đã common_delivered (list không có packageState).
        // Chỉ dùng làm shipped khi không có tín hiệu gói nào — và không được mạnh hơn detail.
        if (orderStatusCode == "10" && packageStates.Count == 0) return "shipped";
        if (orderStatusCode is "3" or "16") return "processing";

        var code = SheinJson.TextOf(SheinJson.Pick(root, "order_status", "orderStatus", "status"));
        if (Regex.IsMatch(code, @"^\d+$") && packageStates.Count == 0)
        {
            switch (code)
            {
                case "6": return "refunded";
                case "5": return "delivered";
                case "4":
                case "10": return "shipped";
                case "1":
                case "2":
                case "3":
                case "7":
                case "16": return "processing";
            }
        }

        if (Regex.IsMatch(blob, @"\b(pending|unpaid|paid|processing|preparing|to\s*be\s*shipped|awaiting|confirmed|placed)\b"))
        {
            return "processing";
        }

        if (blob.Length == 0
            && SheinJson.Get(root, "order_status") is null
            && SheinJson.Get(root, "orderStatus") is null
            && SheinJson.Get(root, "status") is null)
        {
            return null;
        }

        return StatusGroupFromLooseText(blob);
    }

    private static readonly HashSet<string> RefundCodes = ["11", "12", "13", "14"];

    private static string? StatusGroupFromLooseText(string blob)
    {
        if (string.IsNullOrEmpty(blob)) return null;
        if (Regex.IsMatch(blob, @"\b(refunded|full\s*refund)\b")) return "refunded";
        if (blob.Contains("cancel") || blob.Contains("closed")) return "canceled";
        if (blob.Contains("deliver") || blob.Contains("receiv")) return "delivered";
        if (blob.Contains("ship")) return "shipped";
        // Leftover bare numbers with no known mapping are unknown, not "processing".
        return Regex.IsMatch(blob.Trim(), @"^\d+(\s+\d+)*$") ? null : "processing";
    }
}
