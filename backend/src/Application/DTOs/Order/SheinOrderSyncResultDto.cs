namespace Application.DTOs.Order;

/// <summary>Summary of a SHEIN Sync Order run (matching + updates).</summary>
public class SheinOrderSyncResultDto
{
    /// <summary>Number of SHEIN orders received.</summary>
    public int Fetched { get; set; }

    /// <summary>Number of SHEIN orders matched to a local order.</summary>
    public int Matched { get; set; }

    /// <summary>Number of SupplierPurchases whose local tracking number changed this run (warehouse or carrier).</summary>
    public int UpdatedTracking { get; set; }

    /// <summary>Number of carrier trackings successfully published via 4Seller Mark Ship (incl. reconcile).</summary>
    public int AutoShipped { get; set; }

    /// <summary>Number of transient automatic Mark Ship failures (retryable after cooldown).</summary>
    public int AutoShipFailed { get; set; }

    /// <summary>Number blocked because no exact safe Shipping Service mapping was found.</summary>
    public int AutoShipBlocked { get; set; }

    /// <summary>Number of local orders that got cost breakdown filled.</summary>
    public int UpdatedCost { get; set; }

    /// <summary>Number of SHEIN orders that did not match any local order.</summary>
    public int Unmatched { get; set; }

    /// <summary>
    /// Billno đã gắn sẵn và không có delta nào để ghi (chỉ xảy ra ở chế độ <c>new</c>).
    /// Không phải khớp mới cũng không phải bỏ qua, nên phải đếm riêng thì tổng mới bằng
    /// <see cref="Fetched"/>.
    /// </summary>
    public int Unchanged { get; set; }

    /// <summary>
    /// Billnos that may improve after Shipping detail: missing
    /// <c>shipping_telephone</c>/<c>shipping_postcode</c>, or phone/zip conflict on an existing
    /// link. Not populated for “no local order” when phone+zip already present (detail cannot
    /// create a TikTok order). Extension rematch only — never Billing phone/zip.
    /// Only on phase <c>list</c>.
    /// </summary>
    public List<string> UnmatchedBillnos { get; set; } = [];

    /// <summary>
    /// Matched billnos that still need get_order_info (new link, incomplete cost, total delta,
    /// missing carrier after ship, or missing terminal milestone clocks).
    /// Only populated on phase <c>list</c>.
    /// </summary>
    public List<string> NeedsDetailBillnos { get; set; } = [];

    /// <summary>
    /// Matched billnos whose cost breakdown is still incomplete after this sync pass
    /// (null subtotal/total — extension should retry get_order_info).
    /// </summary>
    public List<string> IncompleteCostBillnos { get; set; } = [];

    /// <summary>
    /// Fulfil account resolved from request email / explicit id (nếu có).
    /// </summary>
    public Guid? ResolvedFulfilAccountId { get; set; }

    /// <summary>Tên tài khoản mua hàng đã resolve (hiển thị extension).</summary>
    public string? ResolvedFulfilAccountName { get; set; }

    /// <summary>
    /// Mọi billno bị bỏ qua trong lượt này, kèm mã lý do ổn định. Bất biến của phase
    /// <c>list</c>: <c>Matched + SkipOutcomes.Count == Fetched</c> — lệch nghĩa là có nhánh
    /// <c>continue</c> nào đó quên khai báo lý do.
    /// </summary>
    public List<SheinSkipOutcomeDto> SkipOutcomes { get; set; } = [];

    /// <summary>Số billno bị bỏ qua theo từng mã lý do (<c>Shared.Constants.SheinSkipReasons</c>).</summary>
    public Dictionary<string, int> SkipCounts { get; set; } = [];

    /// <summary>
    /// Billno đã khớp → mã lý do phải gọi <c>get_order_info</c>
    /// (<c>Shared.Constants.SheinNeedsDetailReasons</c>). Chỉ có ở phase <c>list</c>.
    /// </summary>
    public Dictionary<string, string> NeedsDetailReasons { get; set; } = [];

    /// <summary>
    /// <c>true</c> khi lô này quyết định xong nhưng <b>không ghi được</b> xuống DB (xung đột unique
    /// index). Mọi con số đếm phần đã ghi (<see cref="UpdatedTracking"/>, <see cref="UpdatedCost"/>,
    /// <see cref="AutoShipped"/>) đã được đưa về 0, và client không được coi lô này là đã xử lý —
    /// nếu ghi dấu vết "đã sync" cho các billno trong lô thì chúng sẽ không bao giờ được gửi lại.
    /// </summary>
    public bool SaveFailed { get; set; }

    /// <summary>
    /// Số lần mua đã được chuyển từ một đơn ôm quá số dòng hàng của nó sang đơn anh em cùng khách
    /// còn trống.
    ///
    /// Đếm riêng chứ không vào <see cref="SkipOutcomes"/>: đây không phải billno của lô hiện tại bị
    /// bỏ qua mà là dữ liệu cũ được sửa, và bất biến <c>Matched + SkipOutcomes.Count == Fetched</c>
    /// của phase <c>list</c> phải giữ nguyên.
    /// </summary>
    public int Rebalanced { get; set; }

    /// <summary>Per-order human-readable notes (matched / skipped reasons).</summary>
    public List<string> Notes { get; set; } = [];
}

/// <summary>Một billno SHEIN bị bỏ qua, kèm mã lý do và chi tiết đối chiếu.</summary>
public class SheinSkipOutcomeDto
{
    /// <summary>Mã đơn SHEIN bị bỏ qua.</summary>
    public string Billno { get; set; } = string.Empty;

    /// <summary>Mã lý do ổn định — xem <c>Shared.Constants.SheinSkipReasons</c>.</summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>Chi tiết đủ để lần lại (đơn nào, billno nào đã gắn, thiếu field gì).</summary>
    public string? Detail { get; set; }
}
