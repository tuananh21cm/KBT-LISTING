namespace Application.DTOs.Order;

/// <summary>
/// Một lần mua hàng NCC như hàng đợi gắn tay nhìn thấy.
///
/// Khác <c>OrderSupplierLinkDto</c> (nằm bên trong một đơn sàn), DTO này đứng độc lập vì phần lớn
/// dòng ở hàng đợi <b>chưa</b> thuộc đơn nào — đó chính là lý do bảng này tồn tại.
/// </summary>
public class SupplierPurchaseDto
{
    /// <summary>Row id.</summary>
    public Guid Id { get; set; }

    /// <summary>Đơn sàn đã gắn, <c>null</c> khi còn trong hàng đợi.</summary>
    public Guid? OrderId { get; set; }

    /// <summary>Mã đơn sàn của đơn đã gắn (TikTok order id).</summary>
    public string? PlatformOrderId { get; set; }

    /// <summary>Mã 4Seller của đơn đã gắn.</summary>
    public long? FourSellerOrderId { get; set; }

    /// <summary>Tên shop của đơn đã gắn.</summary>
    public string? ShopName { get; set; }

    /// <summary>Mã đơn NCC (SHEIN billno), <c>null</c> với dòng nhập tay.</summary>
    public string? SupplierOrderNo { get; set; }

    /// <summary>Link mở trang đơn bên NCC.</summary>
    public string? SupplierOrderUrl { get; set; }

    /// <summary><c>shein</c> | <c>manual</c>.</summary>
    public string Source { get; set; } = string.Empty;

    /// <summary><c>matched</c> | <c>unmatched</c> | <c>ignored</c>.</summary>
    public string MatchState { get; set; } = string.Empty;

    /// <summary>Mã lý do chưa gắn được (<c>Shared.Constants.SheinSkipReasons</c>).</summary>
    public string? SkipReason { get; set; }

    /// <summary>JSON bằng chứng near-miss cho <see cref="SkipReason"/>.</summary>
    public string? SkipEvidenceJson { get; set; }

    /// <summary>Dòng này có được cộng vào giá mua của đơn hay không.</summary>
    public bool IsCharged { get; set; }

    /// <summary>Tổng tiền NCC thu.</summary>
    public decimal? SupplierTotal { get; set; }

    /// <summary>Giá nhập.</summary>
    public decimal? SupplierSubtotal { get; set; }

    /// <summary>Tiền tệ NCC thu.</summary>
    public string Currency { get; set; } = "USD";

    /// <summary>Mốc đặt hàng bên NCC (UTC).</summary>
    public DateTime? OrderedAt { get; set; }

    /// <summary>Mốc NCC trừ tiền (UTC).</summary>
    public DateTime? SupplierPaidAt { get; set; }

    /// <summary>NCC xác nhận chưa thanh toán.</summary>
    public bool SupplierUnpaid { get; set; }

    /// <summary>Gói SHEIN Club / Saver / hàng ảo.</summary>
    public bool IsMembershipOrVirtual { get; set; }

    /// <summary>Trạng thái vòng đời chuẩn hoá bên NCC.</summary>
    public string? SupplierStatus { get; set; }

    /// <summary>Mã vận đơn.</summary>
    public string? TrackingNo { get; set; }

    /// <summary>Last SHEIN warehouse C…/N… kept after carrier upgrade.</summary>
    public string? WarehouseTrackingNo { get; set; }

    /// <summary>UTC when warehouse tracking was first recorded.</summary>
    public DateTime? WarehouseTrackingSeenAt { get; set; }

    /// <summary><c>none</c> | <c>warehouse</c> | <c>carrier</c>.</summary>
    public string TrackingKind { get; set; } = string.Empty;

    /// <summary>Tên hãng vận chuyển từ NCC (vd. USPS), khi đã nhận diện.</summary>
    public string? Carrier { get; set; }

    /// <summary>Tên người nhận trên đơn NCC.</summary>
    public string? ReceiverName { get; set; }

    /// <summary>Số điện thoại người nhận trên đơn NCC.</summary>
    public string? ReceiverPhone { get; set; }

    /// <summary>Mã bưu chính người nhận trên đơn NCC.</summary>
    public string? ReceiverPostalCode { get; set; }

    /// <summary>Tài khoản mua hàng đã trả tiền cho lần mua này.</summary>
    public Guid? FulfilAccountId { get; set; }

    /// <summary>Tên tài khoản mua hàng.</summary>
    public string? FulfilAccountName { get; set; }

    /// <summary>Người mua hàng hiệu lực (lần mua, fallback đơn sàn).</summary>
    public Guid? PaidByUserId { get; set; }

    /// <summary>Tên người mua hàng — cột Fulfill trên UI.</summary>
    public string? PaidByUserName { get; set; }

    /// <summary>Lần đầu ghi nhận (UTC).</summary>
    public DateTime FirstSeenAt { get; set; }

    /// <summary>Lần gần nhất thấy lại trong một lượt sync (UTC).</summary>
    public DateTime LastSeenAt { get; set; }

    /// <summary>Mã lượt sync gần nhất đã chạm dòng này.</summary>
    public string? LastRunId { get; set; }

    /// <summary>Thời điểm gắn vào đơn sàn (UTC).</summary>
    public DateTime? MatchedAt { get; set; }

    /// <summary>
    /// Mốc người xử lý tự chọn đơn (UTC), <c>null</c> khi do máy khớp. Có mốc này thì luồng sync
    /// không được tự chuyển lần mua sang đơn khác.
    /// </summary>
    public DateTime? ManualLinkedAt { get; set; }
}

/// <summary>Bộ lọc hàng đợi đơn NCC.</summary>
public class SupplierPurchaseQuery
{
    /// <summary>Lọc theo trạng thái khớp; bỏ trống là lấy tất cả.</summary>
    public string? MatchState { get; set; }

    /// <summary>Lọc theo mã lý do chưa gắn được.</summary>
    public string? SkipReason { get; set; }

    /// <summary>Lọc theo tài khoản mua hàng.</summary>
    public Guid? FulfilAccountId { get; set; }

    /// <summary>Lọc theo một đơn sàn cụ thể.</summary>
    public Guid? OrderId { get; set; }

    /// <summary>Lọc theo người mua hàng hiệu lực (lần mua, fallback đơn sàn).</summary>
    public Guid? PaidByUserId { get; set; }

    /// <summary>Chặn dưới theo mốc mua (UTC).</summary>
    public DateTime? From { get; set; }

    /// <summary>Chặn trên theo mốc mua (UTC).</summary>
    public DateTime? To { get; set; }

    /// <summary>Tìm theo billno, tên/điện thoại/zip người nhận, hoặc mã vận đơn.</summary>
    public string? Keyword { get; set; }

    /// <summary>Trang hiện tại (1-based).</summary>
    public int PageCurrent { get; set; } = 1;

    /// <summary>Số dòng mỗi trang.</summary>
    public int PageSize { get; set; } = 50;
}

/// <summary>Một trang hàng đợi đơn NCC kèm số đếm theo trạng thái.</summary>
public class SupplierPurchaseListPageDto
{
    /// <summary>Rows for the current page.</summary>
    public List<SupplierPurchaseDto> Items { get; set; } = [];

    /// <summary>Total matching rows.</summary>
    public int Total { get; set; }

    /// <summary>Current page (1-based).</summary>
    public int PageCurrent { get; set; }

    /// <summary>Page size.</summary>
    public int PageSize { get; set; }

    /// <summary>Số dòng chưa gắn được (không phụ thuộc bộ lọc trang).</summary>
    public int UnmatchedCount { get; set; }

    /// <summary>Số dòng người xử lý đã quyết định bỏ qua.</summary>
    public int IgnoredCount { get; set; }
}

/// <summary>Body của thao tác gắn một lần mua vào đơn sàn.</summary>
public class AttachSupplierPurchaseRequest
{
    /// <summary>Đơn sàn nhận lần mua này.</summary>
    public Guid OrderId { get; set; }

    /// <summary>
    /// Tài khoản mua hàng người xử lý chỉ định, chỉ cần khi lần mua chưa quy kết được tài khoản nào.
    /// Bỏ trống thì giữ nguyên quy kết sẵn có.
    /// </summary>
    public Guid? FulfilAccountId { get; set; }
}

/// <summary>Body xóa nhiều lần mua NCC cùng lúc.</summary>
public class BulkDeleteSupplierPurchasesRequest
{
    /// <summary>Id các lần mua cần xóa (tối đa 200).</summary>
    public List<Guid> Ids { get; set; } = [];
}
