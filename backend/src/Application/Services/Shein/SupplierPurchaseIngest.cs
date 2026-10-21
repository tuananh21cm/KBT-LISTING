using System.Text.Json;
using Application.DTOs.Order;
using Application.Interfaces;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Shared.Constants;

namespace Application.Services.Shein;

/// <summary>
/// Bước 1 của luồng SHEIN: <b>ghi nhận</b>. Mọi billno SHEIN trả về đều được upsert vào
/// <c>SupplierPurchases</c> trước khi chạy khớp, nên một lần khớp sai hay pool hụt không còn làm mất
/// dấu vết lần mua nào.
///
/// Trước đây chỗ duy nhất trong DB chứa được billno là bảng con của <c>Orders</c>: không khớp đơn ⇒
/// không ghi gì cả. Dấu vết duy nhất là <c>SheinSyncRunLogs</c>, vốn tự xoá sau 7 ngày và chỉ dùng
/// để chẩn đoán — đó là lý do 20/169 billno "đã mua mà không có trong sổ cái".
///
/// Đây cũng là chỗ <b>duy nhất</b> ghi các trường phía NCC (tiền, trạng thái, tracking, mốc thời
/// gian, người nhận, payload gốc). Phần khớp chỉ đặt <c>OrderId</c>/<c>MatchState</c>.
/// </summary>
internal static class SupplierPurchaseIngest
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Cửa sổ gộp mốc <see cref="SupplierPurchase.LastSeenAt"/>.
    ///
    /// Mỗi lượt SHEIN trả lại **toàn bộ** billno trong khoảng lookback, và lượt chạy 30–60 s một
    /// lần: stamp mốc "thấy lại" vô điều kiện là ~2.900 UPDATE mỗi dòng mỗi ngày trong khi không có
    /// field nghiệp vụ nào đổi. Bảng có 13 index, nên mỗi UPDATE không HOT được ghi lại ở cả 13 —
    /// đó là lý do heap chỉ 1,6 MB mà index tới 46 MB.
    ///
    /// Trong cửa sổ này, dòng không đổi gì không sinh UPDATE nào; ngoài cửa sổ thì mốc được làm mới
    /// đúng một lần — 24 lượt ghi/ngày thay vì ~2.900. Mọi chỗ đọc mốc này (xếp ứng viên sweep
    /// detail, chọn dòng mua mới nhất để hiển thị) chỉ cần độ chính xác "gần đây", không cần từng
    /// giây; một giờ giữ mốc vẫn đủ sát để đọc như trước.
    /// </summary>
    private static readonly TimeSpan LastSeenCoalesceWindow = TimeSpan.FromHours(1);

    /// <summary>
    /// Nạp sẵn (tracked) mọi dòng đã có của lô billno này, kể cả dòng chưa gắn đơn nào. Không nạp
    /// trước thì mỗi billno là một round-trip, và dòng <c>OrderId = null</c> sẽ không bao giờ được
    /// tìm thấy qua <c>order.SupplierPurchases</c>.
    /// </summary>
    public static async Task<Dictionary<string, SupplierPurchase>> LoadByBillnoAsync(
        IApplicationDbContext db,
        IReadOnlyCollection<string> billnos,
        CancellationToken cancellationToken)
    {
        var map = new Dictionary<string, SupplierPurchase>(StringComparer.OrdinalIgnoreCase);
        if (billnos.Count == 0) return map;

        var lower = billnos.Select(b => b.Trim().ToLowerInvariant()).Distinct().ToList();
        var rows = await db.SupplierPurchases
            .Where(p => p.SupplierOrderNo != null && lower.Contains(p.SupplierOrderNo.ToLower()))
            .ToListAsync(cancellationToken);

        foreach (var row in rows)
        {
            map[row.SupplierOrderNo!] = row;
        }

        return map;
    }

    /// <summary>
    /// Upsert idempotent theo billno. Merge kiểu sparse: field mà payload không mang theo không được
    /// xoá dữ liệu đã có — phase <c>list</c> trả ít trường hơn phase <c>detail</c>, nên ghi đè thẳng
    /// sẽ làm mỗi lượt list xoá sạch breakdown mà detail vừa lấy được.
    /// </summary>
    public static SupplierPurchase Upsert(
        IApplicationDbContext db,
        Dictionary<string, SupplierPurchase> byBillno,
        SheinOrderDto shein,
        string billno,
        string phase,
        string? runId,
        DateTime now)
    {
        var purchase = Upsert(byBillno, shein, billno, phase, runId, now, out var created);
        if (created) db.SupplierPurchases.Add(purchase);
        return purchase;
    }

    /// <summary>
    /// Phần thuần của <see cref="Upsert(IApplicationDbContext, Dictionary{string, SupplierPurchase},
    /// SheinOrderDto, string, string, string?, DateTime)"/>: mọi quy tắc merge nằm ở đây, việc gắn
    /// dòng mới vào change tracker thì không. Tách ra để kiểm được tính idempotent qua nhiều lượt và
    /// hai phase mà không cần dựng database — chính chỗ hồi quy đắt nhất của luồng này.
    /// </summary>
    internal static SupplierPurchase Upsert(
        Dictionary<string, SupplierPurchase> byBillno,
        SheinOrderDto shein,
        string billno,
        string phase,
        string? runId,
        DateTime now,
        out bool created)
    {
        created = !byBillno.TryGetValue(billno, out var purchase);
        if (created)
        {
            purchase = new SupplierPurchase
            {
                Id = Guid.NewGuid(),
                SupplierOrderNo = billno,
                Source = SupplierPurchaseSources.Shein,
                MatchState = SupplierPurchaseMatchStates.Unmatched,
                FirstSeenAt = now,
                CreatedAt = now
            };
            byBillno[billno] = purchase;
        }

        var signatureBefore = created ? 0 : FieldSignature(purchase!);

        purchase!.SupplierOrderUrl = !string.IsNullOrWhiteSpace(shein.OrderUrl)
            ? shein.OrderUrl.Trim()
            : purchase.SupplierOrderUrl ?? $"https://us.shein.com/user/orders/detail/{billno}";

        if (!string.IsNullOrWhiteSpace(shein.RelationBillno))
            purchase.RelationBillno = shein.RelationBillno.Trim();
        if (!string.IsNullOrWhiteSpace(shein.ConfluenceBatchNo))
            purchase.ConfluenceBatchNo = shein.ConfluenceBatchNo.Trim();

        ApplyTracking(purchase, shein);
        ApplyClocks(purchase, shein);
        ApplyReceiver(purchase, shein);
        ApplyStatus(purchase, shein);
        ApplyCosts(purchase, shein, phase);
        ApplyRawPayload(purchase, shein, now);

        // Cờ dính, nhưng chỉ dính với **sự vắng mặt** của bằng chứng. Payload order/list không có
        // order_type nên phase list không kết luận được "không phải Club"; gỡ cờ ở đó thì mỗi lượt
        // list lại gắn billno Club vào một đơn sàn rồi phase detail gỡ ra, mỗi vòng ghi rồi xoá một
        // dòng sổ cái. Nhưng payload có order_type và order_type đó **không** phải 5/6 là một phát
        // biểu tường minh ngược lại, và nó phải thắng kết luận cũ — bằng không một lần nhận diện sai
        // là vĩnh viễn: GSU18V24X001CSQ/GSU18G24X001XC3 mang order_type=1, đã shipped/delivered kèm
        // mã vận đơn, mà vẫn bị loại khỏi giá mua ở mọi lượt vì `virtual_inventory_flag=1` từng làm
        // extension gửi cờ true lên.
        if (IsMembershipOrVirtual(shein)) purchase.IsMembershipOrVirtual = true;
        else if (shein.OrderType.HasValue) purchase.IsMembershipOrVirtual = false;
        if (shein.OrderType.HasValue) purchase.SupplierOrderType = shein.OrderType;

        var productKeys = OrderSyncService.BuildSheinProductIdKeys(shein);
        if (productKeys.Count > 0)
        {
            purchase.ProductIdsJson = JsonSerializer.Serialize(
                productKeys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList(), JsonOptions);
        }

        StampPresence(purchase, runId, now, created, signatureBefore);
        return purchase;
    }

    /// <summary>
    /// Ghi mốc lượt sync — <b>chỉ khi có gì để ghi</b>.
    ///
    /// <c>UpdatedAt</c> giữ nghĩa "lần cuối dữ liệu nghiệp vụ đổi", nên một
    /// lượt sync thấy lại dòng y nguyên không được đẩy mốc đó lên. Còn
    /// <see cref="SupplierPurchase.LastSeenAt"/> / <see cref="SupplierPurchase.LastRunId"/> được gộp
    /// theo <see cref="LastSeenCoalesceWindow"/>: dòng không đổi gì và vừa được thấy trong cửa sổ thì
    /// hàm thoát mà không chạm field nào, nên EF không sinh UPDATE và 13 index của bảng không bị ghi
    /// lại. Ngoài cửa sổ, hai field đó là hai cột <b>không index</b> — Postgres cập nhật được bằng
    /// HOT update, không chạm index nào (fillfactor 85 chừa sẵn chỗ trong trang cho việc này).
    /// </summary>
    private static void StampPresence(
        SupplierPurchase purchase,
        string? runId,
        DateTime now,
        bool created,
        int signatureBefore)
    {
        if (created)
        {
            purchase.LastSeenAt = now;
            purchase.LastRunId = Truncate(runId, 64);
            purchase.UpdatedAt = now;
            return;
        }

        if (FieldSignature(purchase) != signatureBefore)
        {
            purchase.UpdatedAt = now;
        }
        else if (now - purchase.LastSeenAt < LastSeenCoalesceWindow)
        {
            return;
        }

        purchase.LastSeenAt = now;
        purchase.LastRunId = Truncate(runId, 64);
    }

    /// <summary>
    /// Dấu vết của mọi field mà bước ghi nhận có quyền ghi, dùng để biết lượt này có đổi gì không.
    ///
    /// Dùng hash thay vì so từng field để danh sách còn đọc được, và một lần trùng hash (xác suất
    /// ~2⁻³² mỗi dòng mỗi lượt) cũng không làm mất dữ liệu: các field nghiệp vụ **đã** được gán lên
    /// entity phía trên, EF vẫn phát hiện và UPDATE bình thường — chỉ mốc thời gian là bị hoãn tới
    /// lượt sau.
    ///
    /// <c>RawPayload</c> không vào hash: nó dài hàng chục KB, và
    /// <see cref="ApplyRawPayload"/> chỉ đổi <c>RawPayloadAt</c> khi nội dung payload thật sự khác —
    /// nên mốc đó đã là đại diện đủ cho cột kia.
    /// </summary>
    private static int FieldSignature(SupplierPurchase p) => HashCode.Combine(
        HashCode.Combine(
            p.SupplierOrderUrl, p.RelationBillno, p.ConfluenceBatchNo, p.TrackingNo,
            p.WarehouseTrackingNo, p.WarehouseTrackingSeenAt, p.Carrier, p.TrackingKind),
        HashCode.Combine(
            p.AutoMarkShipTrackingNo, p.AutoMarkShipStatus, p.AutoMarkShipAttemptedAt,
            p.AutoMarkShipError, p.AutoMarkShipRequestId, p.OrderedAt, p.SupplierPaidAt,
            p.SupplierUnpaid),
        HashCode.Combine(
            p.ReceiverName, p.ReceiverPhone, p.ReceiverPostalCode, p.PhoneKey, p.ZipKey, p.NameKey,
            p.Currency, p.SupplierStatus),
        HashCode.Combine(
            p.SupplierStatusRaw, p.SupplierStatusAt, p.IsMembershipOrVirtual, p.SupplierOrderType,
            p.ProductIdsJson, p.RawPayloadAt),
        HashCode.Combine(
            p.SupplierRetailPrice, p.SupplierSubtotal, p.SupplierShippingFee,
            p.SupplierRetailDeliveryFee, p.SupplierOnTimeGuarantee, p.SupplierCouponPrice,
            p.SupplierMembershipDiscount, p.SupplierPointsDiscount),
        HashCode.Combine(p.SupplierWalletPrice, p.SupplierTax, p.SupplierTotal));

    /// <summary>
    /// SHEIN Club / Saver / Prime / SKU ảo — không phải một lần mua hàng thật. Ví dụ: Quarterly
    /// membership (<c>order_type=5</c>), SHEIN Saver (<c>order_type=6</c>, <c>is_xtra_goods</c>).
    /// Chúng vẫn có địa chỉ giao hàng nên khớp phone+zip sẽ gắn sai vào một đơn TikTok.
    /// </summary>
    internal static bool IsMembershipOrVirtual(SheinOrderDto shein)
    {
        if (shein.IsMembershipOrVirtual == true) return true;
        // 5 = Club/Prime; 6 = SHEIN Saver / xtra virtual service.
        if (shein.OrderType is 5 or 6) return true;

        foreach (var raw in shein.ProductIds ?? [])
        {
            var id = raw?.Trim();
            if (string.IsNullOrEmpty(id)) continue;
            // Virtual catalog goods_id (Saver 500000433, Quarterly 500000983, Club 500001073…) —
            // đúng 9 chữ số. ProductIds gộp cả goods_id, goods_sn, sku_code nên nới độ dài ra
            // "≥ 6" sẽ khớp cả id hàng thật kiểu 500001 hay 500009876543.
            if (id.Length == 9
                && id.StartsWith("50000", StringComparison.Ordinal)
                && id.All(char.IsDigit))
            {
                return true;
            }
            if (id.StartsWith("VPP", StringComparison.OrdinalIgnoreCase)) return true; // Club SKU
            if (id.StartsWith("SQN", StringComparison.OrdinalIgnoreCase)) return true; // Saver SKU
        }

        return false;
    }

    /// <summary>Gắn dòng mua vào một đơn sàn.</summary>
    public static void Attach(
        SupplierPurchase purchase,
        Order order,
        DateTime now,
        Guid? matchedByUserId = null)
    {
        order.SupplierPurchases ??= new List<SupplierPurchase>();

        purchase.OrderId = order.Id;
        purchase.MatchState = SupplierPurchaseMatchStates.Matched;
        purchase.SkipReason = null;
        purchase.SkipEvidenceJson = null;
        purchase.MatchedAt ??= now;
        purchase.MatchedByUserId ??= matchedByUserId;
        purchase.UpdatedAt = now;

        if (!order.SupplierPurchases.Any(p => p.Id == purchase.Id))
        {
            order.SupplierPurchases.Add(purchase);
        }
    }

    /// <summary>
    /// Gỡ dòng mua khỏi đơn sàn nhưng <b>không xoá</b> nó: một lần mua đã xảy ra thì bằng chứng phải
    /// còn lại. Dòng quay về hàng đợi chưa gắn kèm lý do để người xử lý quyết định.
    /// </summary>
    /// <param name="purchase">Dòng mua cần gỡ.</param>
    /// <param name="order">Đơn sàn đang giữ nó, <c>null</c> khi không nạp kèm navigation.</param>
    /// <param name="reason">Mã lý do (<see cref="SheinSkipReasons"/>).</param>
    /// <param name="now">Mốc ghi <c>UpdatedAt</c>.</param>
    /// <param name="markIgnored">
    /// <c>true</c> khi việc gỡ là **quyết định của người dùng** (vd. bấm Bỏ qua / gỡ kèm khóa khớp)
    /// chứ không phải kết luận của máy — dòng vào <c>ignored</c> để luồng sync không tự gắn lại.
    /// Kết luận của máy (Club/ghost/nhân quả) để mặc định <c>false</c> vì payload mới phải sửa được.
    /// Xoá dòng chi trên sổ cái <b>không</b> dùng nhánh này: lần mua bị xoá hẳn để sync gắn lại sạch.
    /// </param>
    public static void Detach(
        SupplierPurchase purchase,
        Order? order,
        string reason,
        DateTime now,
        bool markIgnored = false)
    {
        order?.SupplierPurchases?.Remove(purchase);

        purchase.OrderId = null;
        purchase.MatchState = markIgnored
            ? SupplierPurchaseMatchStates.Ignored
            : SupplierPurchaseMatchStates.Unmatched;
        purchase.SkipReason = reason;
        purchase.MatchedAt = null;
        purchase.MatchedByUserId = null;
        // Dòng về hàng đợi thì không còn quyết định tay nào để khoá — giữ lại thì lần khớp tự động
        // kế tiếp sinh ra một link "đã gắn tay" mà không ai từng chọn.
        purchase.ManualLinkedAt = null;
        purchase.IsCharged = false;
        purchase.UpdatedAt = now;
    }

    /// <summary>Ghi lý do chưa gắn được kèm bằng chứng near-miss cho dòng đang ở hàng đợi.</summary>
    public static void MarkUnmatched(
        SupplierPurchase purchase,
        string reason,
        string? evidenceJson,
        DateTime now)
    {
        if (purchase.OrderId is not null) return;

        purchase.MatchState = purchase.MatchState == SupplierPurchaseMatchStates.Ignored
            ? SupplierPurchaseMatchStates.Ignored
            : SupplierPurchaseMatchStates.Unmatched;
        purchase.SkipReason = reason;
        purchase.SkipEvidenceJson = evidenceJson;
        purchase.IsCharged = false;
        purchase.UpdatedAt = now;
    }

    private static void ApplyTracking(SupplierPurchase purchase, SheinOrderDto shein)
    {
        // Preserve warehouse code before TrackingNo may upgrade to carrier.
        RememberWarehouseTracking(purchase, purchase.TrackingNo);
        RememberWarehouseTracking(purchase, shein.WarehouseTrackingNo);
        RememberWarehouseTracking(purchase, shein.TrackingNo);

        if (!string.IsNullOrWhiteSpace(shein.Carrier))
        {
            purchase.Carrier = shein.Carrier.Trim();
        }

        if (!string.IsNullOrWhiteSpace(shein.TrackingNo))
        {
            purchase.TrackingNo = shein.TrackingNo.Trim();
        }

        // Legacy rows / sparse payloads: fill Carrier from method_real + tracking prefix.
        if (string.IsNullOrWhiteSpace(purchase.Carrier))
        {
            var inferred = SheinCarrierResolver.Infer(
                null,
                shein.ShippingMethodReal,
                purchase.TrackingNo ?? shein.TrackingNo);
            if (!string.IsNullOrWhiteSpace(inferred))
            {
                purchase.Carrier = inferred;
            }
        }

        purchase.TrackingKind = string.IsNullOrWhiteSpace(purchase.TrackingNo)
            ? SupplierTrackingKinds.None
            // Phân loại lại mỗi lượt: một mã N… từng bị lưu sai thành carrier phải tự sửa được.
            : OrderFourSellerStatusApply.ClassifyTracking(purchase.TrackingNo, purchase.Carrier);

        // Legacy: shipping_no arrived before track_info.name → permanent Block. Reopen so we wait
        // for the name (or Mark Ship immediately when Carrier is already filled).
        OrderAutoMarkShipPolicy.TryReopenMissingCarrierWait(purchase);

        if (purchase.TrackingKind == SupplierTrackingKinds.Carrier
            && !string.IsNullOrWhiteSpace(purchase.TrackingNo)
            && !string.Equals(
                purchase.AutoMarkShipTrackingNo, purchase.TrackingNo, StringComparison.OrdinalIgnoreCase))
        {
            purchase.AutoMarkShipTrackingNo = purchase.TrackingNo;
            purchase.AutoMarkShipStatus = AutoMarkShipStatuses.Pending;
            purchase.AutoMarkShipAttemptedAt = null;
            purchase.AutoMarkShipError = null;
            purchase.AutoMarkShipRequestId = null;
        }
    }

    /// <summary>
    /// Records C…/N… warehouse codes without clearing them when last-mile tracking arrives.
    /// </summary>
    private static void RememberWarehouseTracking(SupplierPurchase purchase, string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return;
        }

        var trimmed = candidate.Trim();
        if (OrderFourSellerStatusApply.ClassifyTracking(trimmed, carrier: null)
            != SupplierTrackingKinds.Warehouse)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(purchase.WarehouseTrackingNo))
        {
            purchase.WarehouseTrackingNo = trimmed;
            purchase.WarehouseTrackingSeenAt ??= DateTime.UtcNow;
            return;
        }

        // Keep first-seen code; only replace if it changed to another warehouse number.
        if (!string.Equals(purchase.WarehouseTrackingNo, trimmed, StringComparison.OrdinalIgnoreCase))
        {
            purchase.WarehouseTrackingNo = trimmed;
        }
    }

    private static void ApplyClocks(SupplierPurchase purchase, SheinOrderDto shein)
    {
        if (shein.OrderedAt.HasValue)
        {
            purchase.OrderedAt = OrderSyncService.NormalizeUtc(shein.OrderedAt.Value);
        }

        // Mốc trừ tiền đã lưu là bằng chứng một chiều: một payload sau đó báo pay_time = 0 (rút gọn,
        // cache cũ, đọc lỗi) không được phép xoá một lần mua thật.
        if (shein.SheinPaidAt.HasValue)
        {
            purchase.SupplierPaidAt = OrderSyncService.NormalizeUtc(shein.SheinPaidAt.Value);
            purchase.SupplierUnpaid = false;
        }
        else if (shein.SheinUnpaid == true && purchase.SupplierPaidAt is null)
        {
            purchase.SupplierUnpaid = true;
        }
    }

    private static void ApplyReceiver(SupplierPurchase purchase, SheinOrderDto shein)
    {
        if (!string.IsNullOrWhiteSpace(shein.BuyerName)) purchase.ReceiverName = shein.BuyerName.Trim();
        if (!string.IsNullOrWhiteSpace(shein.BuyerPhone)) purchase.ReceiverPhone = shein.BuyerPhone.Trim();
        if (!string.IsNullOrWhiteSpace(shein.PostalCode)) purchase.ReceiverPostalCode = shein.PostalCode.Trim();

        var phoneKey = SheinMatchKeys.PhoneKey(purchase.ReceiverPhone);
        var zipKey = SheinMatchKeys.ZipKey(purchase.ReceiverPostalCode);
        var nameKey = SheinMatchKeys.NameKey(purchase.ReceiverName);
        purchase.PhoneKey = phoneKey.Length == 0 ? null : phoneKey;
        purchase.ZipKey = zipKey.Length == 0 ? null : zipKey;
        purchase.NameKey = nameKey.Length == 0 ? null : nameKey;

        if (!string.IsNullOrWhiteSpace(shein.Currency))
        {
            purchase.Currency = shein.Currency.Trim().ToUpperInvariant();
        }
    }

    /// <summary>
    /// <c>SupplierStatusAt</c> chỉ nhận clock thật từ payload.
    ///
    /// <c>SupplierStatusRaw</c> giữ nguyên tín hiệu SHEIN của billno:
    /// <c>packageState</c> (detail) hoặc <c>status</c> (list) — không phải orderStatus (=10 gần như
    /// mọi đơn). Không hạ raw đã tốt (vd. <c>common_delivered</c>) bằng mã list kém hơn.
    /// <c>SupplierStatus</c> (group) chỉ phục vụ luật nghiệp vụ (terminal / sổ cái), không phải nhãn UI.
    /// </summary>
    private static void ApplyStatus(SupplierPurchase purchase, SheinOrderDto shein)
    {
        if (!string.IsNullOrWhiteSpace(shein.SheinStatus))
        {
            var incomingRaw = Truncate(shein.SheinStatus.Trim(), 128)!;
            var storedRawTerminal = RawStatusRank(purchase.SupplierStatusRaw) >= 40;
            if (storedRawTerminal && SheinLiveShipment.LooksLive(shein))
            {
                purchase.SupplierStatusRaw = incomingRaw;
            }
            else if (RawStatusRank(incomingRaw) >= RawStatusRank(purchase.SupplierStatusRaw))
            {
                purchase.SupplierStatusRaw = incomingRaw;
            }
        }

        var incomingGroup = OrderSyncService.NormalizeSheinStatusGroup(shein.SheinStatusGroup);
        // Terminal không được kẹt một chiều: chrome "Return&Refund" từng gắn refunded lên đơn
        // đã shipped, rồi rank 4 > 2 nên list status=5 không bao giờ ghi đè. Phục hồi khi
        // payload hiện tại là đơn sống (5/6/shipped/delivered), kể cả incoming group vẫn sai.
        if (OrderSupplierCost.IsTerminalStatus(purchase.SupplierStatus)
            && SheinLiveShipment.LooksLive(shein))
        {
            purchase.SupplierStatus = SheinLiveShipment.RecoverGroup(shein);
        }
        else if (incomingGroup is not null
            && StatusRank(incomingGroup) >= StatusRank(purchase.SupplierStatus))
        {
            purchase.SupplierStatus = incomingGroup;
        }

        if (shein.SheinStatusAt.HasValue)
        {
            purchase.SupplierStatusAt = OrderSyncService.NormalizeUtc(shein.SheinStatusAt.Value);
        }
    }

    /// <summary>processing &lt; shipped &lt; delivered &lt; terminal.</summary>
    private static int StatusRank(string? group) =>
        group?.Trim().ToLowerInvariant() switch
        {
            SupplierStatusGroups.Processing => 1,
            SupplierStatusGroups.Shipped => 2,
            SupplierStatusGroups.Delivered => 3,
            SupplierStatusGroups.Canceled or "cancelled" => 4,
            SupplierStatusGroups.Refunded => 4,
            _ => 0
        };

    /// <summary>
    /// Cấp bậc raw SHEIN để không ghi đè packageState/detail bằng mã list yếu hơn.
    /// Survey: status 3→processing, 5→shipped, 6→delivered, 13→refunded, 16→canceled/ghost.
    /// </summary>
    private static int RawStatusRank(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return 0;
        var s = raw.Trim().ToLowerInvariant();
        return s switch
        {
            "refunded" or "13" or "14" => 40,
            "canceled" or "cancelled" or "16" => 40,
            "common_delivered" or "delivered" or "received" or "signed" or "6" => 30,
            "shipped" or "in_transit" or "dispatched" or "5" => 20,
            "processing" or "packing" or "preparing" or "3" or "7" => 10,
            // orderStatus=10 và mã số lạ: rất yếu — không được đè packageState/status đã biết.
            _ when System.Text.RegularExpressions.Regex.IsMatch(s, @"^\d+$") => 1,
            _ => 5
        };
    }

    /// <summary>
    /// Detail payload đủ breakdown (retail/tax/coupon/club/points hoặc subtotal+total &gt; 0).
    /// List không gán Subtotal = Total, nên sub≈total (free ship, không giảm) vẫn rich.
    /// Payload chỉ có total → không full-replace (tránh xóa breakdown cũ bằng 0).
    /// </summary>
    internal static bool IsRichCostBreakdown(SheinOrderDto shein)
    {
        if ((shein.RetailPrice ?? 0m) > 0m) return true;
        if ((shein.Tax ?? 0m) > 0m) return true;
        if ((shein.CouponPrice ?? 0m) > 0m) return true;
        if ((shein.ClubDiscount ?? 0m) > 0m) return true;
        if ((shein.PointsDiscount ?? 0m) > 0m) return true;
        if ((shein.WalletPrice ?? 0m) > 0m) return true;
        if ((shein.RetailDeliveryFee ?? 0m) > 0m) return true;
        var sub = shein.Subtotal ?? 0m;
        var total = shein.Total ?? 0m;
        return sub > 0m && total > 0m;
    }

    /// <summary>
    /// Free-order rõ ràng: breakdown rich-signal nhưng total = 0 (ví dụ coupon phủ hết).
    /// Không dùng để ghi 0 khi payload thiếu total.
    /// </summary>
    internal static bool IsExplicitZeroTotal(SheinOrderDto shein)
    {
        if (shein.Total is not 0m) return false;
        return (shein.RetailPrice ?? 0m) > 0m
            || (shein.Subtotal ?? 0m) > 0m
            || (shein.CouponPrice ?? 0m) > 0m
            || (shein.ClubDiscount ?? 0m) > 0m
            || (shein.PointsDiscount ?? 0m) > 0m
            || (shein.WalletPrice ?? 0m) > 0m
            || (shein.RetailDeliveryFee ?? 0m) > 0m
            || (shein.Tax ?? 0m) > 0m;
    }

    private static void ApplyCosts(SupplierPurchase purchase, SheinOrderDto shein, string phase)
    {
        // Full replace chỉ khi detail + breakdown đủ **và** payload có tổng dùng được. Thay hết
        // breakdown mà giữ lại tổng cũ sẽ tạo một bản ghi không cộng ra được: tám thành phần của
        // payload mới đứng cạnh tổng của payload trước.
        var replaceTotal = shein.Total is > 0m
            ? shein.Total
            : IsExplicitZeroTotal(shein) ? 0m : null;

        if (phase == "detail" && IsRichCostBreakdown(shein) && replaceTotal.HasValue)
        {
            purchase.SupplierRetailPrice = shein.RetailPrice ?? 0m;
            purchase.SupplierSubtotal = shein.Subtotal ?? 0m;
            purchase.SupplierShippingFee = shein.ShippingFee ?? 0m;
            purchase.SupplierRetailDeliveryFee = shein.RetailDeliveryFee ?? 0m;
            purchase.SupplierOnTimeGuarantee = shein.OnTimeGuarantee ?? 0m;
            purchase.SupplierCouponPrice = shein.CouponPrice ?? 0m;
            purchase.SupplierMembershipDiscount = shein.ClubDiscount ?? 0m;
            purchase.SupplierPointsDiscount = shein.PointsDiscount ?? 0m;
            purchase.SupplierWalletPrice = shein.WalletPrice ?? 0m;
            purchase.SupplierTax = shein.Tax ?? 0m;
            purchase.SupplierTotal = replaceTotal;
            return;
        }

        // Merge từng field; bỏ qua 0 cho retail/subtotal/coupon/club/tax (placeholder sparse)
        // để không xóa breakdown đã có. Ship / on-time / total vẫn nhận 0 hợp lệ khi có giá trị.
        // Khi payload mang wallet > 0: coupon/club/points = 0 là có nghĩa (tách ví khỏi coupon cũ)
        // — phải ghi đè, không thì sparse giữ coupon đã bị đổ số ví.
        if (shein.RetailPrice is > 0m) purchase.SupplierRetailPrice = shein.RetailPrice;
        if (shein.Subtotal is > 0m) purchase.SupplierSubtotal = shein.Subtotal;
        if (shein.ShippingFee.HasValue) purchase.SupplierShippingFee = shein.ShippingFee;
        if (shein.RetailDeliveryFee is > 0m) purchase.SupplierRetailDeliveryFee = shein.RetailDeliveryFee;
        if (shein.OnTimeGuarantee.HasValue) purchase.SupplierOnTimeGuarantee = shein.OnTimeGuarantee;
        if ((shein.WalletPrice ?? 0m) > 0m)
        {
            purchase.SupplierWalletPrice = shein.WalletPrice;
            if (shein.CouponPrice.HasValue) purchase.SupplierCouponPrice = shein.CouponPrice;
            if (shein.ClubDiscount.HasValue) purchase.SupplierMembershipDiscount = shein.ClubDiscount;
            if (shein.PointsDiscount.HasValue) purchase.SupplierPointsDiscount = shein.PointsDiscount;
        }
        else
        {
            if (shein.CouponPrice is > 0m) purchase.SupplierCouponPrice = shein.CouponPrice;
            if (shein.ClubDiscount is > 0m) purchase.SupplierMembershipDiscount = shein.ClubDiscount;
            if (shein.PointsDiscount is > 0m) purchase.SupplierPointsDiscount = shein.PointsDiscount;
        }
        if (shein.Tax is > 0m) purchase.SupplierTax = shein.Tax;
        if (shein.Total is > 0m) purchase.SupplierTotal = shein.Total;
    }

    /// <summary>
    /// Bọc JSON gốc SHEIN vào <c>RawPayload</c>. Hai phase gửi hai mảnh khác nhau (list gửi
    /// <c>rawListItem</c>, detail gửi <c>rawDetail</c>) nên phải gộp thay vì ghi đè, không thì phase
    /// sau luôn xoá mảnh của phase trước.
    ///
    /// Payload y nguyên thì không ghi lại gì: <c>CapturedAt</c> nằm bên trong JSON nên serialize lại
    /// mỗi lượt tạo ra một chuỗi khác dù nội dung SHEIN không đổi, và mỗi lượt như vậy là một UPDATE
    /// kéo theo cả TOAST của cột <c>text</c> này.
    /// </summary>
    private static void ApplyRawPayload(SupplierPurchase purchase, SheinOrderDto shein, DateTime now)
    {
        var listItem = string.IsNullOrWhiteSpace(shein.RawListItem) ? null : shein.RawListItem;
        var detail = string.IsNullOrWhiteSpace(shein.RawDetail) ? null : shein.RawDetail;
        if (listItem is null && detail is null) return;

        var previous = DeserializeRawPayload(purchase.RawPayload);
        var envelope = new RawPayloadEnvelope
        {
            CapturedAt = now,
            ListItem = listItem ?? previous?.ListItem,
            Detail = detail ?? previous?.Detail
        };

        if (previous is not null
            && string.Equals(previous.ListItem, envelope.ListItem, StringComparison.Ordinal)
            && string.Equals(previous.Detail, envelope.Detail, StringComparison.Ordinal))
        {
            return;
        }

        purchase.RawPayload = JsonSerializer.Serialize(envelope, JsonOptions);
        purchase.RawPayloadAt = now;
    }

    private static RawPayloadEnvelope? DeserializeRawPayload(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<RawPayloadEnvelope>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? Truncate(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var text = value.Trim();
        return text.Length <= max ? text : text[..max];
    }

    /// <summary>
    /// Vỏ chứa JSON gốc SHEIN. Hai mảnh giữ dạng chuỗi đã escape chứ không nhúng thẳng làm JSON
    /// lồng, vì extension cắt payload quá dài và bản cắt không còn parse được.
    /// </summary>
    private sealed class RawPayloadEnvelope
    {
        public DateTime CapturedAt { get; set; }
        public string? ListItem { get; set; }
        public string? Detail { get; set; }
    }
}
