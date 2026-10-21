namespace Shared.Constants;

/// <summary>
/// Lý do một billno SHEIN không được gắn vào đơn sàn trong một lượt đồng bộ.
///
/// Trước đây extension đếm lý do bằng cách dò chuỗi tiếng Việt trong <c>Notes</c>, nên chỉ hai
/// nhánh có regex mới hiện lên thông báo còn sáu nhánh còn lại biến mất — tổng "khớp + bỏ qua"
/// không bao giờ bằng số đơn đã lấy. Mã ổn định ở đây là hợp đồng giữa backend và extension để
/// mọi lần bỏ qua đều đếm được.
/// </summary>
public static class SheinSkipReasons
{
    /// <summary>SHEIN Club / Saver / Prime / hàng ảo — không phải một lần mua hàng thật.</summary>
    public const string MembershipVirtual = "membership_virtual";

    /// <summary>SHEIN báo chưa thanh toán (<c>pay_time = 0</c>) và chưa từng có mốc trừ tiền.</summary>
    public const string UnpaidGhost = "unpaid_ghost";

    /// <summary>
    /// Billno đã huỷ hoặc hoàn trên SHEIN. Tiền đã về tài khoản mua hàng nên không gắn vào đơn sàn
    /// và nếu đang gắn thì gỡ ra — để người đi đơn mua lại từ tài khoản SHEIN khác (SKU không còn bị
    /// chiếm). Cùng một mã cho billno chưa từng gắn lẫn billno vừa bị gỡ.
    /// </summary>
    public const string TerminalUnlinked = "terminal_unlinked";

    /// <summary>Alias tường minh của <see cref="TerminalUnlinked"/> — huỷ/hoàn đã gỡ khỏi đơn.</summary>
    public const string SupplierTerminal = TerminalUnlinked;

    /// <summary>Payload thiếu <c>shipping_telephone</c> hoặc <c>shipping_postcode</c>.</summary>
    public const string MissingAddress = "missing_address";

    /// <summary>Đơn đã gắn billno này nhưng phone/zip trên payload mâu thuẫn với đơn local.</summary>
    public const string AddressConflict = "address_conflict";

    /// <summary>Nhiều đơn cùng phone+zip và sku không phân giải được đơn nào đúng.</summary>
    public const string AmbiguousMatch = "ambiguous_match";

    /// <summary>Đơn cùng khách đã gắn billno khác và không đủ căn cứ gắn thêm link.</summary>
    public const string SiblingLinked = "sibling_linked";

    /// <summary>Có đủ phone+zip nhưng không đơn nào trong pool khớp chính xác cả hai.</summary>
    public const string NoLocalMatch = "no_local_match";

    /// <summary>
    /// Billno lần đầu: đã ghi nhận nhưng <b>hoãn khớp</b> đến phase detail (list thiếu
    /// <c>order_type</c>/breakdown — khớp sớm dễ gắn Club rồi gỡ, hoặc gắn sổ cái khi giá còn sparse).
    /// </summary>
    public const string AwaitingDetail = "awaiting_detail";

    /// <summary>
    /// Billno đang thuộc một đơn khác. Cột <c>SupplierOrderNo</c> có unique index toàn DB nên chế độ
    /// <c>new</c> không được gắn thêm; chỉ chế độ <c>all</c> mới thu hồi và chuyển sang đơn vừa khớp.
    /// </summary>
    public const string DuplicateBillno = "duplicate_billno";

    /// <summary>
    /// Legacy: từng dùng khi xoá dòng chi sổ cái chỉ gỡ về <c>ignored</c>. Hiện xoá CostUpdate xóa
    /// hẳn lần mua; mã còn lại cho dữ liệu cũ / nhật ký skip.
    /// </summary>
    public const string LedgerDeleted = "ledger_deleted";

    /// <summary>
    /// Dòng đang ở trạng thái <c>ignored</c> mà không có lý do cụ thể nào được ghi trước đó — người xử
    /// lý bấm "Bỏ qua" ở trang hàng đợi.
    /// </summary>
    public const string OperatorIgnored = "operator_ignored";

    /// <summary>Mua trước khi đơn sàn được đặt — không thể là hàng của đơn đó.</summary>
    public const string PurchasedBeforeOrder = "purchased_before_order";

    /// <summary>Người xử lý tự gỡ khỏi đơn ở trang hàng đợi.</summary>
    public const string ManualDetach = "manual_detach";

    /// <summary>
    /// Đơn đang giữ nhiều lần mua hơn số dòng hàng của nó nên billno này được chuyển sang đơn anh em
    /// cùng khách còn trống. Dùng làm lý do gỡ tạm ở bước chuyển — dòng mua được gắn lại ngay trong
    /// cùng lượt nên không dừng ở hàng đợi.
    /// </summary>
    public const string RebalancedToSibling = "rebalanced_to_sibling";

    /// <summary>
    /// Người xử lý đã tự chọn đơn cho billno này (<c>SupplierPurchase.ManualLinkedAt</c>) nên lượt
    /// sync không khớp lại. Chỉ báo cáo khi đơn đang giữ nằm ngoài pool lượt này — đơn trong pool
    /// vẫn được refresh bình thường trên chính nó.
    /// </summary>
    public const string ManualLinkLocked = "manual_link_locked";

    /// <summary>
    /// Ứng viên duy nhất là đơn sàn đã hủy/hoàn và mốc mua SHEIN xảy ra <b>sau</b> mốc hủy — không
    /// gắn (tiền không thuộc đơn đã đóng). Dòng mua vào hàng đợi Đơn NCC chưa gắn.
    /// </summary>
    public const string PlatformCanceled = "platform_canceled";

    /// <summary>
    /// Billno đang gắn trên đơn sàn đã hủy/hoàn trong khi còn đơn anh em còn hiệu lực — chuyển sang
    /// đơn sống trong cùng lượt (không nằm lại hàng đợi).
    /// </summary>
    public const string MovedOffCanceledOrder = "moved_off_canceled_order";
}

/// <summary>
/// Lý do phase <c>list</c> vẫn phải gọi <c>get_order_info</c> cho một billno.
/// Detail là phần đắt nhất của một lượt sync (một request SHEIN mỗi billno), nên phải biết điều
/// kiện nào bật thì mới đánh giá được gate có siết đúng hay không.
/// </summary>
public static class SheinNeedsDetailReasons
{
    /// <summary>
    /// Billno lần đầu vào <c>SupplierPurchases</c> — cần full detail (kể cả chưa khớp đơn sàn),
    /// trừ unpaid/terminal đã rõ từ list.
    /// </summary>
    public const string FirstSeen = "first_seen";

    /// <summary>Link mới (hoặc link joint mới) — cần breakdown giá.</summary>
    public const string NewLink = "new_link";

    /// <summary>Sau khi merge list, link vẫn thiếu subtotal hoặc total.</summary>
    public const string CostIncomplete = "cost_incomplete";

    /// <summary>Total trên payload khác total đã lưu — cần detail để dựng lại breakdown.</summary>
    public const string TotalDelta = "total_delta";

    /// <summary>Đã rời kho nhưng chưa có mã vận đơn chặng cuối.</summary>
    public const string NoCarrierTracking = "no_carrier_tracking";

    /// <summary>Trạng thái kết thúc/đã gửi nhưng thiếu clock mốc mà list không cung cấp.</summary>
    public const string MissingMilestoneClock = "missing_milestone_clock";

    /// <summary>
    /// Chế độ <c>all</c>: mọi billno đã khớp đều phải lấy lại <c>get_order_info</c> vì mục đích của
    /// chế độ này là dựng lại breakdown giá từ dữ liệu mới, không phải merge lên số cũ.
    /// </summary>
    public const string ModeAll = "mode_all";

    /// <summary>
    /// Nghi ngờ ví cashback bị đổ vào coupon (Total=0, Coupon&gt;0, Wallet=0) — cần detail để đọc
    /// <c>usedWalletPrice</c> và tách lại.
    /// </summary>
    public const string WalletCostStale = "wallet_cost_stale";

    /// <summary>
    /// Auto Mark Ship bị hoãn theo ngân sách của request (xem
    /// <c>OrderAutoMarkShipPolicy.MaxAttemptsPerSyncRequest</c>). Không phải cần dữ liệu detail:
    /// billno chỉ cần thêm một lượt POST trong cùng run để đẩy nốt mã lên sàn — nếu chờ lượt sync
    /// sau thì fingerprint list đã đứng yên (mã vận đơn không đổi nữa) nên Auto Sync bỏ qua cả lượt.
    /// </summary>
    public const string PendingAutoMarkShip = "pending_auto_mark_ship";
}
