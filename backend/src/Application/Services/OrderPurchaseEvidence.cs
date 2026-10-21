using Domain.Entities;

namespace Application.Services;

/// <summary>
/// Quy tắc duy nhất quyết định mốc "Mua" của đơn còn bằng chứng hay không.
///
/// Mốc này chỉ có nghĩa nhờ một lần mua thật. Khi billno SHEIN duy nhất của đơn là đơn ghost —
/// tạo rồi hết hạn trả tiền nên tự huỷ, <c>pay_time = 0</c> — thì mốc được stamp từ <c>addTime</c>
/// của nó là dương tính giả và phải gỡ, bằng không UI vẫn báo "Đã mua" cho đơn chưa từng mua.
///
/// Không dùng <c>FulfilAccountId</c> làm bằng chứng đã mua: sync SHEIN tự điền field đó từ email
/// billing của chính billno ghost, nên nó không phân biệt được người đi đơn ghi nhận thật với suy
/// diễn của sync (đúng ca 577494030896631910 — account được điền từ GSU18J247000TWB đã huỷ).
/// </summary>
public static class OrderPurchaseEvidence
{
    /// <summary>
    /// Gỡ mốc "Mua" khi và chỉ khi chứng minh được nó bắt nguồn từ một billno chưa thanh toán.
    ///
    /// Ba điều kiện đều bắt buộc, cố tình hẹp để không xoá mốc đúng: mốc không do người dùng nhập
    /// tay, đơn không còn billno nào đã thanh toán, và timestamp của mốc khớp đúng clock của một
    /// billno chưa thanh toán. Điều kiện thứ ba là mấu chốt — mốc đến từ nguồn khác (Mark Ship,
    /// đợt backfill cũ, đơn NCC khác) sẽ không khớp nên được giữ nguyên.
    /// </summary>
    /// <returns><c>true</c> khi mốc vừa bị gỡ.</returns>
    public static bool ClearPurchasedWhenOnlyGhostBillnosRemain(Order order)
    {
        if (order.FulfilPurchasedAt is null) return false;

        var links = order.SupplierPurchases;
        if (links is null || links.Count == 0) return false;
        if (links.Any(l => !l.SupplierUnpaid)) return false;

        if (OrderFulfilTimeline.SourceOf(order, OrderFulfilTimeline.Milestone.FulfilPurchased)
            == OrderFulfilTimeline.Source.Manual)
        {
            return false;
        }

        var stamped = order.FulfilPurchasedAt.Value;
        var cameFromGhost = links.Any(l =>
            SameInstant(l.OrderedAt, stamped) || SameInstant(l.SupplierPaidAt, stamped));
        if (!cameFromGhost) return false;

        OrderFulfilTimeline.Clear(order, OrderFulfilTimeline.Milestone.FulfilPurchased);
        OrderFulfilTimeline.Normalize(order);
        return true;
    }

    /// <summary>
    /// Gỡ quy kết tài khoản/thẻ khi đơn không còn dấu vết mua nào. Sync SHEIN tự điền
    /// <see cref="Order.FulfilAccountId"/> từ email billing của billno, nên gỡ billno mà để lại quy
    /// kết thì đơn vẫn hiện "do tài khoản X mua" dù không còn billno nào của X.
    /// </summary>
    public static void ClearAttributionWhenNothingPurchased(Order order)
    {
        // Dòng nhập tay giờ cũng nằm trong SupplierPurchases, nên không còn tổng phí nào tồn tại
        // ngoài bảng này để phải kiểm riêng.
        if ((order.SupplierPurchases?.Count ?? 0) > 0) return;

        order.FulfilAccountId = null;
        order.FulfilCardId = null;
    }

    /// <summary>
    /// Đồng bộ quy kết tài khoản/thẻ cấp đơn với tập lần mua đang thuộc đơn.
    ///
    /// <see cref="Order.FulfilAccountId"/> là bản sao phi chuẩn hoá phục vụ danh sách đơn (cột
    /// «TK mua») và các bộ lọc cũ; nguồn sự thật là <see cref="SupplierPurchase.FulfilAccountId"/>
    /// trên từng lần mua, vì một billno là một lần checkout riêng. Mọi chỗ đổi chủ lần mua phải gọi
    /// hàm này — thiếu một chỗ là đơn hiện đúng tiền trong sổ cái nhưng trống tên tài khoản (đúng ca
    /// gắn tay <c>GSU1SG240001UUP</c> → đơn <c>577590806038024769</c>).
    ///
    /// Không ghi đè một quy kết còn bằng chứng: tài khoản đang lưu mà vẫn có lần mua mang nó thì giữ
    /// nguyên, kể cả khi người xử lý đã chọn tay qua <c>UpdateOrderCostAsync</c>.
    /// </summary>
    public static void SyncAttributionWithPurchases(Order order)
    {
        var purchases = order.SupplierPurchases;
        if (purchases is null || purchases.Count == 0)
        {
            ClearAttributionWhenNothingPurchased(order);
            return;
        }

        // Quy kết đang lưu vẫn còn một lần mua đứng sau nó — không có gì để sửa.
        if (order.FulfilAccountId is { } current && purchases.Any(p => p.FulfilAccountId == current))
        {
            return;
        }

        var primary = purchases
            .Where(p => p.FulfilAccountId.HasValue)
            // Dòng được tính tiền là dòng thật sự trừ thẻ; trong đó lấy lần mua sớm nhất để hai lần
            // chạy trên cùng dữ liệu cho cùng kết quả.
            .OrderByDescending(p => p.IsCharged)
            .ThenBy(p => p.SupplierPaidAt ?? p.OrderedAt ?? p.CreatedAt)
            .ThenBy(p => p.Id)
            .FirstOrDefault();

        // Không lần mua nào biết tài khoản: giữ nguyên quy kết đang lưu. Nó là thứ duy nhất còn lại
        // và `ReconcileCostUpdateTransactionsAsync` dùng nó làm nguồn dự phòng cho dòng chi — xoá đi
        // là biến một khoản đã quẹt thẻ thành "chưa rõ tài khoản mua hàng" mà không thu lại được gì.
        if (primary is null) return;

        // Thẻ đang lưu thuộc tài khoản cũ, nên khi **đổi** tài khoản nó phải đi theo chứ không được
        // giữ lại: hiển thị thẻ của tài khoản khác là sai cả đối chiếu sao kê. Đơn chưa từng quy kết
        // thì chỉ điền vào chỗ trống.
        var replacingAnotherAccount = order.FulfilAccountId.HasValue;
        order.FulfilAccountId = primary.FulfilAccountId;
        if (replacingAnotherAccount)
        {
            order.FulfilCardId = primary.FulfilCardId;
        }
        else
        {
            order.FulfilCardId ??= primary.FulfilCardId;
        }
    }

    /// <summary>
    /// Gỡ mốc "Mua" sau khi lần mua đã chuyển sang đơn khác (Chuyển đơn / attach lại).
    ///
    /// Khác <see cref="ClearPurchasedWhenOnlyGhostBillnosRemain"/>: chấp nhận
    /// <c>SupplierPurchases.Count == 0</c> (đơn cũ vừa mất hết lần mua). Vẫn hẹp: provenance không
    /// phải Manual, không còn lần mua đã thanh toán nào, và timestamp mốc khớp clock của lần mua
    /// vừa dời — mốc từ nguồn khác được giữ.
    /// </summary>
    /// <returns><c>true</c> khi mốc vừa bị gỡ.</returns>
    public static bool ClearPurchasedWhenLinkMovedAway(Order order, SupplierPurchase movedAway)
    {
        if (order.FulfilPurchasedAt is null) return false;

        var remaining = order.SupplierPurchases;
        if (remaining is not null && remaining.Any(l => !l.SupplierUnpaid)) return false;

        if (OrderFulfilTimeline.SourceOf(order, OrderFulfilTimeline.Milestone.FulfilPurchased)
            == OrderFulfilTimeline.Source.Manual)
        {
            return false;
        }

        var stamped = order.FulfilPurchasedAt.Value;
        if (!SameInstant(movedAway.OrderedAt, stamped) && !SameInstant(movedAway.SupplierPaidAt, stamped))
        {
            return false;
        }

        OrderFulfilTimeline.Clear(order, OrderFulfilTimeline.Milestone.FulfilPurchased);
        OrderFulfilTimeline.Normalize(order);
        return true;
    }

    /// <summary>
    /// So mốc bỏ qua <see cref="DateTime.Kind"/>: cột DB đọc ra là UTC còn giá trị vừa stamp trong
    /// bộ nhớ có thể là Unspecified, so trực tiếp sẽ trượt.
    /// </summary>
    private static bool SameInstant(DateTime? candidate, DateTime stamped) =>
        candidate is { } value
        && DateTime.SpecifyKind(value, DateTimeKind.Utc)
            == DateTime.SpecifyKind(stamped, DateTimeKind.Utc);
}
