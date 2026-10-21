namespace Application.DTOs.Fulfil;

/// <summary>Fulfil transaction ledger row.</summary>
public class FulfilTransactionDto
{
    /// <summary>Id.</summary>
    public Guid Id { get; set; }

    /// <summary>Account id (null when card used outside purchase accounts).</summary>
    public Guid? FulfilAccountId { get; set; }

    /// <summary>Account name.</summary>
    public string? FulfilAccountName { get; set; }

    /// <summary>Card id (null when manual txn has no card).</summary>
    public Guid? FulfilCardId { get; set; }

    /// <summary>Card name.</summary>
    public string? FulfilCardName { get; set; }

    /// <summary>Masked card number.</summary>
    public string? FulfilCardNumberMasked { get; set; }

    /// <summary>Card provider source name (PingPongX, WorldFirst, …).</summary>
    public string? FulfilCardSourceName { get; set; }

    /// <summary>Card provider source account name.</summary>
    public string? FulfilCardSourceAccountName { get; set; }

    /// <summary>Card billing currency.</summary>
    public string? FulfilCardCurrency { get; set; }

    /// <summary>Order id.</summary>
    public Guid? OrderId { get; set; }

    /// <summary>Platform order id for display.</summary>
    public string? PlatformOrderId { get; set; }

    /// <summary>4Seller order id.</summary>
    public long? FourSellerOrderId { get; set; }

    /// <summary>Supplier purchase this charge paid for (null for manual / order-level rows).</summary>
    public Guid? OrderSupplierLinkId { get; set; }

    /// <summary>
    /// Mốc người xử lý tự chọn đơn cho lần mua của dòng chi này (UTC). Có mốc = luồng sync không tự
    /// chuyển billno sang đơn khác nữa. <c>null</c> với dòng nhập tay và link do máy khớp.
    /// </summary>
    public DateTime? ManualLinkedAt { get; set; }

    /// <summary>
    /// Supplier platform order number (SHEIN billno / Temu / …). Taken from the purchase this
    /// row paid for, so it is the billno actually charged — falls back to the order's primary
    /// billno for legacy order-level rows.
    /// </summary>
    public string? SupplierOrderNo { get; set; }

    /// <summary>Supplier order detail URL matching <see cref="SupplierOrderNo"/>.</summary>
    public string? SupplierOrderUrl { get; set; }

    /// <summary>
    /// Trạng thái NCC đã chuẩn hóa (<c>processing</c>/<c>shipped</c>/<c>delivered</c>/<c>canceled</c>/<c>refunded</c>)
    /// — lấy từ lần mua gắn với dòng tiền.
    /// </summary>
    public string? SupplierStatus { get; set; }

    /// <summary>
    /// Trạng thái thô từ SHEIN (ưu tiên <c>packageState</c>, vd. <c>common_delivered</c> / <c>shipped</c>).
    /// </summary>
    public string? SupplierStatusRaw { get; set; }

    /// <summary>
    /// Mã vận đơn của lần mua gắn dòng tiền (warehouse C…/N… hoặc last-mile USPS/FedEx/…).
    /// </summary>
    public string? TrackingNo { get; set; }

    /// <summary>Tên carrier từ SHEIN <c>track_info.name</c> (USPS, FedEx, UPS, …).</summary>
    public string? Carrier { get; set; }

    /// <summary><c>none</c> | <c>warehouse</c> | <c>carrier</c>.</summary>
    public string? TrackingKind { get; set; }

    /// <summary>Order seller / owner id.</summary>
    public Guid? SellerId { get; set; }

    /// <summary>Order seller / owner display name.</summary>
    public string? SellerName { get; set; }

    /// <summary>Snapshot coupon theo lần mua (CostUpdate). Manual = null.</summary>
    public decimal? CouponAmount { get; set; }

    /// <summary>Snapshot ví Shein theo lần mua (CostUpdate). Manual = null.</summary>
    public decimal? WalletAmount { get; set; }

    /// <summary>Snapshot giảm membership/club theo lần mua (CostUpdate). Manual = null.</summary>
    public decimal? MembershipAmount { get; set; }

    /// <summary>Snapshot giảm điểm SHEIN Points theo lần mua (CostUpdate). Manual = null.</summary>
    public decimal? PointsAmount { get; set; }

    /// <summary>Doanh thu đơn (SellerNet → TotalPaid → Amount).</summary>
    public decimal? RevenueAmount { get; set; }

    /// <summary>Lợi nhuận cấp đơn = Revenue − Cost (gross NCC + BuyTrack + ThankCard) = Orders.totalCost.</summary>
    public decimal? ProfitAmount { get; set; }

    /// <summary>
    /// Giá mua gộp + phí xử lý theo dòng CostUpdate = gross NCC + BuyTrack + ThankCard.
    /// Gross NCC ưu tiên breakdown SupplierPurchase; fallback ledger snapshots.
    /// Manual = null.
    /// </summary>
    public decimal? PurchaseCostAmount { get; set; }

    /// <summary>
    /// Chi thật (TrueSpend) theo dòng CostUpdate = Amount (thẻ) + WalletAmount + MembershipAmount (club).
    /// Club cộng vào vì giảm membership chỉ có nhờ đã mua SHEIN Club.
    /// Read-model — khớp Orders.ChargedTrueSpend khi cộng Σ cấp đơn. Manual = null.
    /// Preview Mua hàng — không dùng cho Profit (Profit = Revenue − totalCost gross).
    /// </summary>
    public decimal? TrueSpendAmount { get; set; }

    /// <summary>
    /// Phí BuyTrack (USPS) cấp đơn — từ <c>Orders.BuyTrackAmount</c>. Cùng giá trị trên mọi dòng của đơn.
    /// Không phải quẹt thẻ; không cộng vào KPI Thẻ.
    /// </summary>
    public decimal BuyTrackAmount { get; set; }

    /// <summary>
    /// Phí ThankCard cấp đơn — từ <c>Orders.ThankCardAmount</c>. Cùng giá trị trên mọi dòng của đơn.
    /// Không phải quẹt thẻ; không cộng vào KPI Thẻ.
    /// </summary>
    public decimal ThankCardAmount { get; set; }

    /// <summary>Phí ship lần mua gắn dòng chi (CostUpdate read-model từ SupplierPurchase). Manual = null.</summary>
    public decimal? SupplierShippingFee { get; set; }

    /// <summary>Thuế lần mua gắn dòng chi (CostUpdate read-model từ SupplierPurchase). Manual = null.</summary>
    public decimal? SupplierTax { get; set; }

    /// <summary>Amount (absolute, &gt;= 0). CostUpdate = SupplierTotal (totalPrice hóa đơn / thẻ).</summary>
    public decimal Amount { get; set; }

    /// <summary>Debit (trừ tiền) or Credit (nạp tiền).</summary>
    public string Direction { get; set; } = "Debit";

    /// <summary>Currency.</summary>
    public string Currency { get; set; } = "USD";

    /// <summary>Note.</summary>
    public string? Note { get; set; }

    /// <summary>CostUpdate or Manual.</summary>
    public string Source { get; set; } = "Manual";

    /// <summary>Order handler user id (sales attribution).</summary>
    public Guid? PaidByUserId { get; set; }

    /// <summary>Order handler display name.</summary>
    public string? PaidByUserName { get; set; }

    /// <summary>Creator user id.</summary>
    public Guid CreatedByUserId { get; set; }

    /// <summary>Creator name.</summary>
    public string? CreatedByUserName { get; set; }

    /// <summary>Occurred at.</summary>
    public DateTime OccurredAt { get; set; }

    /// <summary>Created.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>Updated.</summary>
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>Create manual fulfil transaction.</summary>
public class CreateFulfilTransactionDto
{
    /// <summary>Account id (optional — card may pay for other services).</summary>
    public Guid? FulfilAccountId { get; set; }

    /// <summary>Card id (optional for manual entries).</summary>
    public Guid? FulfilCardId { get; set; }

    /// <summary>Optional order id.</summary>
    public Guid? OrderId { get; set; }

    /// <summary>Amount (absolute, &gt;= 0).</summary>
    public decimal Amount { get; set; }

    /// <summary>Debit (trừ tiền) or Credit (nạp tiền). Default Debit.</summary>
    public string Direction { get; set; } = "Debit";

    /// <summary>Currency.</summary>
    public string Currency { get; set; } = "USD";

    /// <summary>Note.</summary>
    public string? Note { get; set; }

    /// <summary>Order handler user id (sales attribution).</summary>
    public Guid? PaidByUserId { get; set; }

    /// <summary>Occurred at (UTC).</summary>
    public DateTime? OccurredAt { get; set; }
}

/// <summary>Update manual fulfil transaction (or note/amount on CostUpdate).</summary>
public class UpdateFulfilTransactionDto
{
    /// <summary>Account id (optional — card may pay for other services).</summary>
    public Guid? FulfilAccountId { get; set; }

    /// <summary>Card id (optional for manual entries; CostUpdate should keep a card).</summary>
    public Guid? FulfilCardId { get; set; }

    /// <summary>Optional order id.</summary>
    public Guid? OrderId { get; set; }

    /// <summary>Amount (absolute, &gt;= 0).</summary>
    public decimal Amount { get; set; }

    /// <summary>Debit (trừ tiền) or Credit (nạp tiền).</summary>
    public string Direction { get; set; } = "Debit";

    /// <summary>Currency.</summary>
    public string Currency { get; set; } = "USD";

    /// <summary>Note.</summary>
    public string? Note { get; set; }

    /// <summary>Order handler user id (sales attribution).</summary>
    public Guid? PaidByUserId { get; set; }

    /// <summary>Occurred at (UTC).</summary>
    public DateTime OccurredAt { get; set; }
}

/// <summary>Paged fulfil transaction list.</summary>
public class FulfilTransactionListPageDto
{
    /// <summary>Rows for the current page.</summary>
    public List<FulfilTransactionDto> Items { get; set; } = [];

    /// <summary>Total matching rows.</summary>
    public int Total { get; set; }

    /// <summary>Current page (1-based).</summary>
    public int PageCurrent { get; set; }

    /// <summary>Page size.</summary>
    public int PageSize { get; set; }

    /// <summary>
    /// Scalar KPI for the same filter as <see cref="Items"/> (not the current page).
    /// Profit is unique per order so joint-shipment rows are not summed twice.
    /// </summary>
    public FulfilTransactionListSummaryDto Summary { get; set; } = new();
}

/// <summary>Header KPI for Lịch sử mua hàng — same filters as the paged list.</summary>
public class FulfilTransactionListSummaryDto
{
    /// <summary>Distinct <c>OrderId</c> in the filtered set (Manual without order = 0).</summary>
    public int OrderCount { get; set; }

    /// <summary>Σ cột Thẻ = Debit − Credit của <c>Amount</c> (phần thẻ / totalPrice hóa đơn).</summary>
    public decimal CardAmount { get; set; }

    /// <summary>Σ Cost column = CostUpdate gross NCC (thẻ+ví+coupon+club+points) + unique-order BuyTrack/ThankCard.</summary>
    public decimal CostAmount { get; set; }

    /// <summary>
    /// Σ <c>Orders.BuyTrackAmount</c> 1 lần / đơn trong filter — phí USPS scan cấp đơn, không phải quẹt thẻ.
    /// </summary>
    public decimal BuyTrackAmount { get; set; }

    /// <summary>
    /// Σ <c>Orders.ThankCardAmount</c> 1 lần / đơn trong filter — phí ThankCard cấp đơn, không phải quẹt thẻ.
    /// </summary>
    public decimal ThankCardAmount { get; set; }

    /// <summary>Σ <c>CouponAmount</c> snapshot.</summary>
    public decimal CouponAmount { get; set; }

    /// <summary>Σ <c>WalletAmount</c> snapshot.</summary>
    public decimal WalletAmount { get; set; }

    /// <summary>Σ <c>MembershipAmount</c> (Club) snapshot.</summary>
    public decimal MembershipAmount { get; set; }

    /// <summary>Σ <c>PointsAmount</c> snapshot.</summary>
    public decimal PointsAmount { get; set; }

    /// <summary>
    /// Σ unique-order profit (Revenue − totalCost), matching per-row <c>profitAmount</c>
    /// without adding the same joint-shipment order twice.
    /// </summary>
    public decimal ProfitAmount { get; set; }
}

/// <summary>Filter for transaction list.</summary>
public class FulfilTransactionQuery
{
    /// <summary>Filter by account.</summary>
    public Guid? AccountId { get; set; }

    /// <summary>Filter by card.</summary>
    public Guid? CardId { get; set; }

    /// <summary>Filter by card provider source (PingPongX / WorldFirst / …).</summary>
    public Guid? SourceId { get; set; }

    /// <summary>Filter by provider source account (tài khoản nguồn).</summary>
    public Guid? SourceAccountId { get; set; }

    /// <summary>Filter by order.</summary>
    public Guid? OrderId { get; set; }

    /// <summary>Filter by order shop (via linked order).</summary>
    public Guid? ShopId { get; set; }

    /// <summary>Filter by order seller / owner (via linked order <c>OwnerId</c>).</summary>
    public Guid? SellerId { get; set; }

    /// <summary>
    /// Seller portal scope: rows whose order is owned by this user, or Manual rows with no order
    /// created by this user. When set, overrides <see cref="SellerId"/>.
    /// </summary>
    public Guid? OwnerUserId { get; set; }

    /// <summary>Filter by order handler (người đi đơn / xử lý đơn).</summary>
    public Guid? PaidByUserId { get; set; }

    /// <summary>
    /// Free-text search across note, order ids, supplier order no, tracking/carrier,
    /// seller, account/card names, handler/creator.
    /// </summary>
    public string? Keyword { get; set; }

    /// <summary>From (inclusive, UTC).</summary>
    public DateTime? From { get; set; }

    /// <summary>To (inclusive, UTC).</summary>
    public DateTime? To { get; set; }

    /// <summary>Page number (1-based, default 1).</summary>
    public int PageCurrent { get; set; } = 1;

    /// <summary>Page size (default 100, max 200).</summary>
    public int PageSize { get; set; } = 100;
}
