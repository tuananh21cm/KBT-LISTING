using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Application.DTOs.Order;

namespace Application.Services.Shein;

/// <summary>
/// Turns raw SHEIN storefront payloads into the <see cref="SheinOrderDto"/> the sync pipeline
/// already consumes, so server-side realtime sync reuses <c>SyncFromSheinAsync</c> unchanged.
///
/// This is a port of the extension's <c>frontend/fulfil-extension/src/shared/sheinOrders.ts</c>.
/// The two must agree field for field: with the header source set to <c>local</c> the extension
/// produces this payload, with <c>local</c> the backend does, and a divergence would show up
/// as costs silently changing when an operator flips that setting.
/// </summary>
public static partial class SheinOrderNormalizer
{
    [GeneratedRegex(@"^[a-z0-9._%+-]+@[a-z0-9.-]+\.[a-z]{2,}$", RegexOptions.IgnoreCase)]
    private static partial Regex EmailRegex();

    [GeneratedRegex(@"^[CN]\d{10,}$", RegexOptions.IgnoreCase)]
    private static partial Regex WarehouseTrackingRegex();

    [GeneratedRegex(@"quarterly|free gifts\+extra|shein\s*club|shein\s*saver|prime\s*membership|vip\s*membership", RegexOptions.IgnoreCase)]
    private static partial Regex MembershipNameRegex();

    /// <summary>
    /// Id catalog ảo (Club/Saver/membership) đúng 9 chữ số: 500000433, 500000983, 500001073.
    /// Không để mở đuôi <c>\d+</c> — id hàng thật cũng có thể bắt đầu bằng 50000, và khớp nhầm ở đây
    /// dẫn tới gỡ link và xoá dòng sổ cái của một đơn hàng thật.
    /// </summary>
    [GeneratedRegex(@"^50000\d{4}$")]
    private static partial Regex VirtualGoodsIdRegex();

    [GeneratedRegex(@"-p-(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex PdpProductIdRegex();

    /// <summary>Product id / sku keys on a goods row; SHEIN renames these between API versions.</summary>
    private static readonly string[] ProductIdKeys =
    [
        "goods_id", "goodsId", "product_id", "productId", "productRelationID", "product_relation_id",
        "goods_sn", "goodsSn", "sku_code", "skuCode"
    ];

    /// <summary>
    /// Normalizes one element of <c>order/list</c>.
    /// </summary>
    /// <param name="order">A single order object from the list response.</param>
    /// <param name="origin">Storefront origin used to build the order detail URL.</param>
    /// <param name="captureRaw">When true, the raw list item is attached for diagnostics.</param>
    public static SheinOrderDto NormalizeListOrder(JsonObject order, string origin, bool captureRaw = false)
    {
        var billAddr = SheinJson.Obj(SheinJson.Pick(order, "billaddr_info", "billingaddr_info", "bill_addr_info"));
        // Shipping Information only — never Billing phone/zip.
        var shippingAddr = new SheinOrderDto();
        ApplyShippingAddress(shippingAddr, order, overwrite: true);
        var billno = SheinJson.Text(order, "billno", "billNo") ?? string.Empty;
        var host = origin.TrimEnd('/');

        // On the list page shipping/total/coupon are usually present, but subtotal/tax/club are
        // only trustworthy in get_order_info. Prefer a literal subtotal and reconcile the
        // discounts against it; with neither, leave the subtotal null rather than claim it equals
        // the total.
        var coupon = ReadOrderCoupon(order);
        var shipping = SheinJson.Money(order, "shippingPrice", "shipping_price");
        var retailDelivery = ReadOrderRetailDeliveryFee(order);
        var onTime = SumInsurance(order);
        var tax = ReadOrderTax(order);
        var total = SheinJson.Money(order, "totalPrice", "total_price");
        var literalSubtotal = ReadOrderSubtotal(order);
        var club = ReadOrderClub(order);
        var points = ReadOrderPoints(order);
        var wallet = ReadOrderWallet(order);

        decimal? subtotal;
        if (literalSubtotal is not null)
        {
            subtotal = literalSubtotal;
            var reconciled = ReconcileDiscounts(
                subtotal, shipping, onTime, tax, total, coupon, club, points, wallet, retailDelivery);
            coupon = reconciled.Coupon;
            club = reconciled.Club;
            points = reconciled.Points;
        }
        else if (CanInvertSubtotalFromTotal(tax, club, coupon, onTime, points, wallet, retailDelivery))
        {
            subtotal = ComputeSubtotalFromTotal(
                total, shipping, onTime, tax, club, coupon, points, wallet, retailDelivery);
        }
        else
        {
            subtotal = null;
        }

        var status = ExtractStatus(order);
        var milestones = ExtractMilestones(order);
        var (paidAt, isUnpaid) = ExtractPaidAt(order);
        var trackingNo = FirstTrackingNo(order);
        var methodReal = FirstShippingMethodReal(order);
        var trackInfoName = SheinJson.Text(SheinJson.Obj(SheinJson.Get(order, "track_info")), "name");

        return new SheinOrderDto
        {
            Billno = billno,
            AccountEmail = NormalizeAccountEmail(SheinJson.Pick(billAddr, "email", "Email")),
            BuyerName = shippingAddr.BuyerName ?? string.Empty,
            BuyerPhone = shippingAddr.BuyerPhone ?? string.Empty,
            Address = shippingAddr.Address ?? string.Empty,
            City = shippingAddr.City ?? string.Empty,
            State = shippingAddr.State ?? string.Empty,
            PostalCode = shippingAddr.PostalCode ?? string.Empty,
            CountryCode = shippingAddr.CountryCode ?? string.Empty,
            TrackingNo = trackingNo,
            WarehouseTrackingNo = FirstWarehouseTrackingNo(order),
            ShippingMethodReal = methodReal,
            Carrier = SheinCarrierResolver.Infer(trackInfoName, methodReal, trackingNo) ?? string.Empty,
            IsJointShipment = DetectJointShipment(order),
            RelationBillno = ExtractRelationBillno(order),
            ConfluenceBatchNo = ExtractConfluenceBatchNo(order),
            OrderType = ExtractOrderType(order),
            IsMembershipOrVirtual = DetectMembershipOrVirtual(order),
            Currency = SheinJson.Text(order, "currency_code") ?? "USD",
            OrderedAt = ExtractOrderedAt(order),
            SheinPaidAt = paidAt,
            SheinUnpaid = isUnpaid,
            OrderUrl = billno.Length > 0 ? $"{host}/user/orders/detail/{billno}" : null,
            RetailPrice = ReadOrderRetail(order),
            Subtotal = subtotal,
            ShippingFee = shipping,
            RetailDeliveryFee = retailDelivery,
            OnTimeGuarantee = onTime,
            CouponPrice = coupon,
            ClubDiscount = club,
            PointsDiscount = points,
            WalletPrice = wallet,
            Tax = tax,
            Total = total,
            ProductIds = CollectProductIds(order),
            SheinStatus = status.Status,
            SheinStatusGroup = status.Group,
            SheinStatusAt = status.StatusAt,
            SheinShippedAt = milestones.ShippedAt,
            SheinTrackingAt = milestones.TrackingAt,
            SheinDeliveredAt = milestones.DeliveredAt,
            SheinCanceledAt = milestones.CanceledAt,
            SheinRefundedAt = milestones.RefundedAt,
            RawListItem = captureRaw ? SheinJson.ToJson(order) : null
        };
    }

    /// <summary>
    /// A stub payload so <c>get_order_info</c> can run for billnos outside the list page window.
    /// </summary>
    public static SheinOrderDto StubOrder(string billno) => new()
    {
        Billno = billno.Trim(),
        BuyerName = string.Empty,
        BuyerPhone = string.Empty,
        Address = string.Empty,
        City = string.Empty,
        State = string.Empty,
        PostalCode = string.Empty,
        CountryCode = string.Empty,
        TrackingNo = string.Empty,
        Carrier = string.Empty,
        Currency = "USD"
    };

    /// <summary>
    /// Folds a <c>get_order_info</c> response into an order already built from the list page (or
    /// a stub). Detail wins for cost and tracking; list-only fields are kept when detail is silent.
    /// </summary>
    public static void ApplyDetail(JsonNode? detail, SheinOrderDto target, bool captureRaw = false)
    {
        if (captureRaw)
        {
            // Captured before parsing: the order that makes this method misbehave is exactly the
            // one whose raw payload needs looking at.
            target.RawDetail = SheinJson.ToJson(detail);
        }

        // Storefront get_order_info envelope is `{ code, info: { order: … } }`.
        // Extension unwraps `info` first (`detail?.info ?? detail`); without that step the
        // wrapper becomes "root" and every Money/tracking read is null — Sync Track then
        // reports cost +0 while the UI stays on "Thiếu order detail".
        var detailObj = SheinJson.Obj(detail);
        var unwrapped = SheinJson.Obj(SheinJson.Get(detailObj, "info")) ?? detailObj;
        var root = SheinJson.Obj(SheinJson.Get(unwrapped, "order")) ?? unwrapped;

        decimal? apiSubtotal = null;
        decimal? uiSubtotal = null;
        decimal? literalSubtotal = null;

        if (root is not null)
        {
            apiSubtotal = SheinJson.Money(root, "subTotalPrice", "subtotalPrice", "subtotal");
            uiSubtotal = ReadPriceRowByLabel(root, SubtotalLabelRegex());
            literalSubtotal = ReadOrderSubtotal(root);

            var shipping = SheinJson.Money(root, "shippingPrice", "shipping_price");
            if (shipping.HasValue) target.ShippingFee = Math.Abs(shipping.Value);

            var retailDelivery = ReadOrderRetailDeliveryFee(root);
            if (retailDelivery.HasValue) target.RetailDeliveryFee = retailDelivery;

            var coupon = ReadOrderCoupon(root);
            if (coupon.HasValue) target.CouponPrice = coupon;

            var club = ReadOrderClub(root);
            if (club.HasValue) target.ClubDiscount = club;

            var points = ReadOrderPoints(root);
            if (points.HasValue) target.PointsDiscount = points;

            var wallet = ReadOrderWallet(root);
            if (wallet.HasValue) target.WalletPrice = wallet;

            var tax = ReadOrderTax(root);
            if (tax.HasValue) target.Tax = tax;

            var total = SheinJson.Money(root, "totalPrice", "total_price");
            if (total.HasValue) target.Total = total;

            var retail = ReadOrderRetail(root) ?? ReadPriceRowByLabel(root, RetailLabelRegex());
            if (retail.HasValue) target.RetailPrice = retail;

            var insurance = SumInsurance(root);
            if (insurance.HasValue) target.OnTimeGuarantee = insurance;

            // get_order_info is authoritative for last-mile tracking; the list page often still
            // carries an older SHEIN warehouse C…/N… reference.
            var detailTracking = FirstTrackingNo(root);
            if (detailTracking.Length > 0) target.TrackingNo = detailTracking;

            var warehouseTracking = FirstWarehouseTrackingNo(root);
            if (warehouseTracking.Length > 0) target.WarehouseTrackingNo = warehouseTracking;

            var methodReal = FirstShippingMethodReal(root);
            if (!string.IsNullOrEmpty(methodReal)) target.ShippingMethodReal = methodReal;

            var trackInfoName = SheinJson.Text(SheinJson.Obj(SheinJson.Get(root, "track_info")), "name");
            var inferred = SheinCarrierResolver.Infer(
                trackInfoName,
                target.ShippingMethodReal,
                target.TrackingNo);
            if (!string.IsNullOrEmpty(inferred)) target.Carrier = inferred;

            var milestones = ExtractMilestones(root);
            if (milestones.ShippedAt.HasValue) target.SheinShippedAt = milestones.ShippedAt;
            if (milestones.TrackingAt.HasValue) target.SheinTrackingAt = milestones.TrackingAt;
            if (milestones.DeliveredAt.HasValue) target.SheinDeliveredAt = milestones.DeliveredAt;
            if (milestones.CanceledAt.HasValue) target.SheinCanceledAt = milestones.CanceledAt;
            if (milestones.RefundedAt.HasValue) target.SheinRefundedAt = milestones.RefundedAt;

            var detailOrderedAt = ExtractOrderedAt(root);
            if (detailOrderedAt.HasValue) target.OrderedAt = detailOrderedAt;

            target.IsJointShipment ??= DetectJointShipment(root);

            if (string.IsNullOrEmpty(target.RelationBillno))
            {
                target.RelationBillno = ExtractRelationBillno(root) ?? target.RelationBillno;
            }

            if (string.IsNullOrEmpty(target.ConfluenceBatchNo))
            {
                target.ConfluenceBatchNo = ExtractConfluenceBatchNo(root) ?? target.ConfluenceBatchNo;
            }

            target.OrderType ??= ExtractOrderType(root);

            if (target.IsMembershipOrVirtual != true)
            {
                target.IsMembershipOrVirtual = DetectMembershipOrVirtual(root);
            }

            if (target.ProductIds.Count == 0)
            {
                var ids = CollectProductIds(root);
                if (ids.Count > 0) target.ProductIds = ids;
            }

            var status = ExtractStatus(root);
            if (!string.IsNullOrEmpty(status.Status)) target.SheinStatus = status.Status;
            if (!string.IsNullOrEmpty(status.Group)) target.SheinStatusGroup = status.Group;
            if (status.StatusAt.HasValue) target.SheinStatusAt = status.StatusAt;

            // Detail uses paymentTime (milliseconds) while the list uses pay_time (seconds); a
            // payment clock found in either piece wins, and the unpaid flag is only written when
            // no payment clock has ever been seen.
            var (paidAt, isUnpaid) = ExtractPaidAt(root);
            if (paidAt.HasValue)
            {
                target.SheinPaidAt = paidAt;
                target.SheinUnpaid = false;
            }
            else if (isUnpaid.HasValue && target.SheinPaidAt is null)
            {
                target.SheinUnpaid = isUnpaid;
            }

            // Detail: flat shipping_* on root (or nested shippingaddr_info) — overwrite list.
            ApplyShippingAddress(target, root, overwrite: true);

            if (string.IsNullOrEmpty(target.AccountEmail))
            {
                var billAddr = SheinJson.Obj(SheinJson.Pick(root, "billaddr_info", "billingaddr_info", "bill_addr_info"));
                target.AccountEmail = NormalizeAccountEmail(SheinJson.Pick(billAddr, "email", "Email"));
            }

            // Tiền tệ thật của lần mua. Phase list mặc định "USD" khi payload không nói, nên detail
            // là cơ hội duy nhất để sửa lại: mọi số ở đây đều được cộng dồn như cùng một đơn vị, và
            // một đơn EUR bị coi là USD sẽ làm lệch giá vốn mà không có dấu vết nào.
            var detailCurrency = SheinJson.Text(root, "currency_code")
                ?? SheinJson.Text(root, "currencyCode")
                ?? SheinJson.Text(root, "payment_currency");
            if (!string.IsNullOrWhiteSpace(detailCurrency))
            {
                target.Currency = detailCurrency.Trim().ToUpperInvariant();
            }
        }

        // Walk the whole unwrapped tree (info/order), not the outer {code,info} envelope alone.
        var uiSubtotalFromWalk = WalkPriceLabels(unwrapped ?? detail, target);
        if (uiSubtotalFromWalk.HasValue) uiSubtotal = uiSubtotalFromWalk;

        var hadLiteralSubtotal = literalSubtotal is not null || uiSubtotal is not null;
        var formulaSubtotal = CanInvertSubtotalFromTotal(
            target.Tax, target.ClubDiscount, target.CouponPrice, target.OnTimeGuarantee,
            target.PointsDiscount, target.WalletPrice, target.RetailDeliveryFee)
            ? ComputeSubtotalFromTotal(
                target.Total, target.ShippingFee, target.OnTimeGuarantee, target.Tax,
                target.ClubDiscount, target.CouponPrice, target.PointsDiscount,
                target.WalletPrice, target.RetailDeliveryFee)
            : null;

        var resolvedSubtotal = literalSubtotal ?? uiSubtotal ?? formulaSubtotal ?? apiSubtotal;
        if (resolvedSubtotal.HasValue) target.Subtotal = resolvedSubtotal;

        // With an independent subtotal (literal or UI), force the discount identity so a FREE
        // shipping credit folded into both coupon and club is split back out.
        if (hadLiteralSubtotal)
        {
            var reconciled = ReconcileDiscounts(
                target.Subtotal, target.ShippingFee, target.OnTimeGuarantee, target.Tax,
                target.Total, target.CouponPrice, target.ClubDiscount, target.PointsDiscount,
                target.WalletPrice, target.RetailDeliveryFee);
            target.CouponPrice = reconciled.Coupon;
            target.ClubDiscount = reconciled.Club;
            target.PointsDiscount = reconciled.Points;
        }

        if (target.RetailPrice is null && root is not null)
        {
            var goodsRetail = SumGoodsField(root, GoodsRetailKeys);
            if (goodsRetail.HasValue) target.RetailPrice = goodsRetail;
        }

        // A sparse detail (total only) stays null so the backend does not wipe a stored breakdown.
        DefaultMissingBreakdownToZero(target);
    }

    /// <summary>
    /// Walks every labelled amount in the payload, applying it to <paramref name="target"/>.
    /// Returns the UI "Subtotal" row when one was seen.
    /// </summary>
    private static decimal? WalkPriceLabels(JsonNode? node, SheinOrderDto target)
    {
        decimal? uiSubtotal = null;
        Walk(node, 0);
        return uiSubtotal;

        void Walk(JsonNode? current, int depth)
        {
            if (depth > 8 || current is null)
            {
                return;
            }

            if (current is JsonArray array)
            {
                foreach (var item in array) Walk(item, depth + 1);
                return;
            }

            if (current is not JsonObject obj)
            {
                return;
            }

            var label = RowLabel(obj);
            if (label is not null)
            {
                foreach (var priceKey in PriceAmountKeys)
                {
                    if (!obj.ContainsKey(priceKey))
                    {
                        continue;
                    }

                    var amount = SheinJson.ReadMoney(SheinJson.Get(obj, priceKey));
                    if (amount.HasValue)
                    {
                        MapLabel(label, amount.Value);
                        break;
                    }
                }
            }

            foreach (var (_, value) in obj) Walk(value, depth + 1);
        }

        void MapLabel(string label, decimal amount)
        {
            var l = label.Trim();
            if (RetailLabelRegex().IsMatch(l) && target.RetailPrice is null)
            {
                target.RetailPrice = amount;
            }
            else if (SubtotalLabelRegex().IsMatch(l))
            {
                uiSubtotal = amount;
            }
            else if (IsRetailDeliveryLabel(l))
            {
                target.RetailDeliveryFee = Math.Abs(amount);
            }
            else if (l.Contains("shipping", StringComparison.OrdinalIgnoreCase) && target.ShippingFee is null)
            {
                target.ShippingFee = Math.Abs(amount);
            }
            else if (Regex.IsMatch(l, @"(on-?time|guarantee|insurance)", RegexOptions.IgnoreCase) && target.OnTimeGuarantee is null)
            {
                target.OnTimeGuarantee = Math.Abs(amount);
            }
            else if (IsWalletLabel(l))
            {
                target.WalletPrice = Math.Abs(amount);
            }
            else if (IsPointsDiscountLabel(l))
            {
                target.PointsDiscount = Math.Abs(amount);
            }
            else if (IsClubDiscountLabel(l))
            {
                // An explicit CLUB exclusive label is more trustworthy than prime_discount, which
                // is often the Total Savings figure.
                if (target.ClubDiscount is null || Regex.IsMatch(l, "exclusive|membership", RegexOptions.IgnoreCase))
                {
                    target.ClubDiscount = Math.Abs(amount);
                }
            }
            else if (IsCouponDiscountLabel(l))
            {
                // A label containing "coupon" beats the API couponPrice, which often folds in the
                // free-shipping credit.
                if (target.CouponPrice is null || l.Contains("coupon", StringComparison.OrdinalIgnoreCase))
                {
                    target.CouponPrice = Math.Abs(amount);
                }
            }
            else if (l.Contains("tax", StringComparison.OrdinalIgnoreCase) && target.Tax is null)
            {
                target.Tax = amount;
            }
            else if (IsPaymentTotalLabel(l) && target.Total is null)
            {
                target.Total = amount;
            }
        }
    }

    /// <summary>Best tracking number on the order, preferring a carrier over a warehouse code.</summary>
    private static string FirstTrackingNo(JsonObject order)
    {
        var numbers = CollectShippingNos(order);
        // Prefer the last-mile carrier over SHEIN warehouse C…/N… references.
        var carrier = numbers.FirstOrDefault(no => !WarehouseTrackingRegex().IsMatch(no));
        if (carrier is not null) return carrier;
        if (numbers.Count > 0) return numbers[0];

        // Warehouse / joint shipments often expose the C… code here before a carrier number exists.
        return SheinJson.Text(order, "reference_number", "referenceNumber") ?? string.Empty;
    }

    /// <summary>First SHEIN warehouse C…/N… on the order (including reference_number).</summary>
    private static string FirstWarehouseTrackingNo(JsonObject order)
    {
        var numbers = CollectShippingNos(order);
        var warehouse = numbers.FirstOrDefault(no => WarehouseTrackingRegex().IsMatch(no));
        if (warehouse is not null) return warehouse;

        var reference = SheinJson.Text(order, "reference_number", "referenceNumber");
        if (!string.IsNullOrEmpty(reference) && WarehouseTrackingRegex().IsMatch(reference))
        {
            return reference;
        }

        return string.Empty;
    }

    private static List<string> CollectShippingNos(JsonObject order)
    {
        var numbers = new List<string>();
        foreach (var goods in SheinJson.ObjectItems(order, "orderGoodsList"))
        {
            foreach (var rel in SheinJson.ObjectItems(goods, "goods_pkg_rel_list"))
            {
                var no = SheinJson.Text(rel, "shipping_no");
                if (!string.IsNullOrEmpty(no)) numbers.Add(no);
            }
        }

        return numbers;
    }

    /// <summary>First non-empty <c>shipping_method_real</c> on goods package relations.</summary>
    private static string FirstShippingMethodReal(JsonObject order)
    {
        foreach (var goods in SheinJson.ObjectItems(order, "orderGoodsList"))
        {
            foreach (var rel in SheinJson.ObjectItems(goods, "goods_pkg_rel_list", "goodsPkgRelList"))
            {
                var method = SheinJson.Text(rel, "shipping_method_real", "shippingMethodReal");
                if (!string.IsNullOrEmpty(method)) return method;
            }
        }

        return string.Empty;
    }

    /// <summary>SHEIN <c>relation_billno</c> — the primary joint-shipment group key.</summary>
    private static string? ExtractRelationBillno(JsonObject order) =>
        SheinJson.Text(order, "relation_billno", "relationBillno", "relation_bill_no", "relationBillNo");

    /// <summary>Warehouse <c>confluence_batch_no</c> from <c>order_package_info_list</c>.</summary>
    private static string? ExtractConfluenceBatchNo(JsonObject order)
    {
        foreach (var pkg in SheinJson.ObjectItems(order, "order_package_info_list", "orderPackageInfoList"))
        {
            var v = SheinJson.Text(pkg, "confluence_batch_no", "confluenceBatchNo");
            if (!string.IsNullOrEmpty(v)) return v;
        }

        return SheinJson.Text(order, "confluence_batch_no", "confluenceBatchNo", "confluence_batch");
    }

    /// <summary>SHEIN <c>order_type</c> (5 = Club/Prime, 6 = Saver/xtra virtual).</summary>
    private static int? ExtractOrderType(JsonObject order)
    {
        var raw = SheinJson.Pick(order, "order_type", "orderType");
        if (raw is null || SheinJson.TextOf(raw).Length == 0) return null;
        var n = SheinJson.ToNumber(raw);
        return n is null || !double.IsFinite(n.Value) ? null : (int)n.Value;
    }

    /// <summary>
    /// Detects SHEIN Club / Saver / Quarterly / virtual orders, which are charges rather than
    /// merchandise to fulfil. Signals: order_type 5|6, is_virtual_order, xtra_order_scene,
    /// is_xtra_goods, goods_id 500000…, SKU VPP…/SQN…, Saver/Club product names.
    /// </summary>
    private static bool DetectMembershipOrVirtual(JsonObject order)
    {
        var orderType = ExtractOrderType(order);
        if (orderType is 5 or 6) return true;

        if (SheinJson.Truthy(SheinJson.Get(order, "is_virtual_order"))
            || SheinJson.Truthy(SheinJson.Get(order, "isVirtualOrder")))
        {
            return true;
        }

        var xtraScene = SheinJson.TextOf(SheinJson.Pick(order, "xtra_order_scene", "xtraOrderScene"));
        if (xtraScene.Length > 0 && xtraScene != "0") return true;

        var goods = SheinJson.ObjectItems(order, "orderGoodsList").ToList();
        if (goods.Count == 0) return false;

        var allXtra = true;
        foreach (var g in goods)
        {
            var product = SheinJson.Obj(SheinJson.Get(g, "product"));
            var name = SheinJson.Text(product, "goods_name")
                       ?? SheinJson.Text(g, "goods_name")
                       ?? SheinJson.Text(product, "goods_url_name")
                       ?? string.Empty;
            if (MembershipNameRegex().IsMatch(name)) return true;

            var goodsId = SheinJson.Text(g, "goods_id") ?? SheinJson.Text(product, "goods_id") ?? string.Empty;
            // Virtual catalog ids (Club/Saver/membership): 500000433, 500000983, 500001073…
            if (VirtualGoodsIdRegex().IsMatch(goodsId)) return true;

            var sku = SheinJson.Text(g, "sku_code", "goods_sn")
                      ?? SheinJson.Text(product, "goods_sn", "sku_code")
                      ?? string.Empty;
            // VPP = Club, SQN = Saver.
            if (sku.StartsWith("VPP", StringComparison.OrdinalIgnoreCase)
                || sku.StartsWith("SQN", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Cố tình không dùng is_prime_goods **và** virtual_inventory_flag: cả hai nói về cách
            // định giá / cách trữ hàng của món, không nói "món này là gói thành viên". Đo trên dữ
            // liệu thật: GSU18V24X001CSQ (goods_id 499132132) và GSU18G24X001XC3 (goods_id
            // 502358797) đều có `virtual_inventory_flag=1`, `is_prime_goods=false`, `order_type=1`,
            // SKU `sz2604…` — và cả hai đã **shipped/delivered kèm mã vận đơn**. Đó là hàng vật lý.
            // Nhánh membership là nhánh **phá huỷ** (gỡ khỏi đơn, xoá dòng sổ cái) nên nó không được
            // chạy trên một cờ merchandising. Gói Club/Saver thật đã bị bắt ở trên bằng
            // order_type 5|6, is_virtual_order, xtra_order_scene, goods_id 500000…, SKU VPP/SQN và
            // tên sản phẩm.
            var isXtra = SheinJson.Truthy(SheinJson.Get(g, "is_xtra_goods"))
                         || SheinJson.Truthy(SheinJson.Get(product, "is_xtra_goods"));
            if (!isXtra) allXtra = false;
        }

        // The whole order is xtra (Saver) SKUs — no physical merchandise.
        return allXtra;
    }

    /// <summary>Joint shipment / multi-package detection from the list or detail payload.</summary>
    private static bool? DetectJointShipment(JsonObject order)
    {
        var combinedFlag = SheinJson.Pick(order, "combined_flag", "combinedFlag");
        var combinedNumber = SheinJson.ToNumber(combinedFlag);
        if (combinedNumber is > 0) return true;

        if (SheinJson.Truthy(SheinJson.Pick(order, "combined_shipped_flag", "combinedShippedFlag", "combined_shipped")))
        {
            return true;
        }

        if (SheinJson.Truthy(SheinJson.Pick(order, "cross_order_flag", "crossOrderFlag", "cross_order")))
        {
            return true;
        }

        if (!string.IsNullOrEmpty(ExtractRelationBillno(order))) return true;

        foreach (var key in new[]
                 {
                     "is_joint_shipment", "isJointShipment", "joint_shipment", "jointShipment",
                     "is_combine_package", "isCombinePackage", "combine_package", "combinePackage"
                 })
        {
            var v = SheinJson.Get(order, key);
            if (v is null) continue;
            if (SheinJson.Truthy(v)) return true;

            var text = SheinJson.TextOf(v).ToLowerInvariant();
            if (text is "0" or "false") return false;
        }

        var pkgNos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var goods in SheinJson.ObjectItems(order, "orderGoodsList"))
        {
            foreach (var rel in SheinJson.ObjectItems(goods, "goods_pkg_rel_list"))
            {
                var no = SheinJson.Text(rel, "package_no", "shipping_no");
                if (!string.IsNullOrEmpty(no)) pkgNos.Add(no);
            }
        }

        return pkgNos.Count > 1 ? true : null;
    }

    /// <summary>
    /// Collects product ids / skus of the items in the order, including ids embedded in PDP URLs
    /// (<c>/a-p-86020798.html</c>) because the field names change between API versions.
    /// </summary>
    private static List<string> CollectProductIds(JsonObject order)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        void Push(JsonNode? value)
        {
            var s = SheinJson.TextOf(value);
            if (s.Length == 0 || s.Length > 40) return;
            if (seen.Add(s)) result.Add(s);
        }

        void Walk(JsonNode? node, int depth)
        {
            if (depth > 4 || node is null) return;

            if (node is JsonArray array)
            {
                foreach (var item in array) Walk(item, depth + 1);
                return;
            }

            if (node is not JsonObject obj) return;

            foreach (var key in ProductIdKeys)
            {
                if (obj.ContainsKey(key)) Push(SheinJson.Get(obj, key));
            }

            foreach (var (_, value) in obj)
            {
                if (value is JsonValue v && v.TryGetValue<string>(out var s) && s.Contains("-p-"))
                {
                    var match = PdpProductIdRegex().Match(s);
                    if (match.Success) Push(JsonValue.Create(match.Groups[1].Value));
                }
            }

            foreach (var (_, value) in obj) Walk(value, depth + 1);
        }

        Walk(SheinJson.Get(order, "orderGoodsList"), 0);
        return result;
    }


        /// <summary>
    /// Applies SHEIN Shipping fields onto the DTO.
    /// List: nested <c>shippingaddr_info.shipping_*</c>.
    /// Detail (<c>get_order_info</c>): flat <c>shipping_telephone</c>/<c>shipping_postcode</c>/
    /// <c>shipping_firstname</c>/<c>shipping_lastname</c> on the order root (no nested object).
    /// Never uses Billing (<c>billaddr_info</c> or flat <c>telephone</c>/<c>postcode</c>/<c>firstname</c>).
    /// </summary>
    internal static void ApplyShippingAddress(SheinOrderDto target, JsonObject? addrOrRoot, bool overwrite)
    {
        if (addrOrRoot is null) return;

        var nested = SheinJson.Obj(SheinJson.Get(addrOrRoot, "shippingaddr_info"));
        var addr = nested ?? addrOrRoot;

        // Exact SHEIN Shipping keys only — no telephone/postcode/firstname aliases (Billing).
        var phone = SheinJson.Text(addr, "shipping_telephone");
        var postal = SheinJson.Text(addr, "shipping_postcode");
        var name = JoinNonEmpty(" ", SheinJson.Text(addr, "shipping_firstname"), SheinJson.Text(addr, "shipping_lastname"));
        var address = JoinNonEmpty(" ", SheinJson.Text(addr, "shipping_address_1"), SheinJson.Text(addr, "shipping_address_2"));
        var city = SheinJson.Text(addr, "shipping_city");
        var state = SheinJson.Text(addr, "shipping_province");
        var country = SheinJson.Text(addr, "shipping_country_code");

        if (!string.IsNullOrEmpty(phone) && (overwrite || string.IsNullOrEmpty(target.BuyerPhone)))
            target.BuyerPhone = phone;
        if (!string.IsNullOrEmpty(postal) && (overwrite || string.IsNullOrEmpty(target.PostalCode)))
            target.PostalCode = postal;
        if (name.Length > 0 && (overwrite || string.IsNullOrEmpty(target.BuyerName)))
            target.BuyerName = name;
        if (address.Length > 0 && (overwrite || string.IsNullOrEmpty(target.Address)))
            target.Address = address;
        if (!string.IsNullOrEmpty(city) && (overwrite || string.IsNullOrEmpty(target.City)))
            target.City = city;
        if (!string.IsNullOrEmpty(state) && (overwrite || string.IsNullOrEmpty(target.State)))
            target.State = state;
        if (!string.IsNullOrEmpty(country) && (overwrite || string.IsNullOrEmpty(target.CountryCode)))
            target.CountryCode = country;
    }

/// <summary>Trims and lowercases an email, rejecting anything that is not one.</summary>
    private static string? NormalizeAccountEmail(JsonNode? node)
    {
        var s = SheinJson.TextOf(node).ToLowerInvariant();
        return s.Length > 0 && EmailRegex().IsMatch(s) ? s : null;
    }

    private static string JoinNonEmpty(string separator, params string?[] parts) =>
        string.Join(separator, parts.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!.Trim()));
}
