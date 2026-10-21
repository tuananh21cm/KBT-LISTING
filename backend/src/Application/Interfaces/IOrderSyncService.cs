using Application.DTOs.FourSeller;
using Application.DTOs.Order;

namespace Application.Interfaces;

/// <summary>
/// Admin order list/detail and 4Seller order sync.
/// </summary>
public interface IOrderSyncService
{
    /// <summary>Lists orders from local DB.</summary>
    Task<OrderListPageDto> ListAsync(OrderListQuery query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Aggregate counts by platformOrderStatus using the same filters as list (excluding platformOrderStatus).
    /// </summary>
    Task<OrderStatusCountsDto> GetStatusCountsAsync(
        OrderListQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>Gets order detail with items.</summary>
    Task<OrderDetailDto> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lightweight hover preview (line items + sell/cost totals). No listing overlay / buyer / notes.
    /// </summary>
    Task<OrderHoverPreviewDto> GetPreviewAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Pulls orders from 4Seller for one or all sellers with cookies and upserts locally.
    /// Multi-account runs fan out in parallel (one DI scope / cookie / proxy per seller).
    /// </summary>
    Task<OrderSyncResultDto> SyncAsync(OrderSyncRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Syncs orders for a single seller (no global SyncGate). Used by parallel multi-account <see cref="SyncAsync"/>.
    /// </summary>
    Task<(int Inserted, int Updated)> SyncOwnerAsync(
        Guid ownerUserId,
        IReadOnlyList<string> orderStatuses,
        DateTime? dateFrom,
        DateTime? dateTo,
        CancellationToken cancellationToken = default,
        bool openStatusesIgnoreDateFilter = false,
        bool skipStalePricingSweep = false,
        bool skipNewOrderTelegram = false);

    /// <summary>
    /// Lists 4Seller shipping carriers for a platform, using the seller's cookie.
    /// </summary>
    Task<IReadOnlyList<FourSellerShippingServiceDto>> ListShippingServicesAsync(
        Guid ownerUserId,
        string? platform,
        bool refresh = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks a local order as shipped on 4Seller (async poll) and updates local DB on success.
    /// </summary>
    Task<MarkShipResultDto> MarkShipAsync(
        Guid orderId,
        MarkShipRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Refreshes one order's commerce status from 4Seller by exact <c>orderNo</c> search
    /// (bypasses create-date lookback of bulk sync). Does not touch after-sales REFUND latch.
    /// </summary>
    Task<OrderPlatformStatusSyncResultDto> SyncPlatformStatusAsync(
        Guid orderId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates supplier (NCC) cost breakdown and optional fulfil payment link.
    /// Upserts a CostUpdate ledger transaction when account/card + SupplierTotal are set. Does not call 4Seller.
    /// </summary>
    Task<OrderDetailDto> UpdateCostAsync(
        Guid orderId,
        UpdateOrderCostRequest request,
        Guid actingUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates local fulfil pipeline milestones (purchase / tracking / in-transit / delivered / refund).
    /// Partial: only non-null request fields are applied. Does not call 4Seller.
    /// When <c>FulfilPurchasedAt</c> is set for the first time, notifies the seller (OrderChat + SignalR).
    /// </summary>
    /// <param name="sessionId">Optional Chrome-profile session from <c>X-Fulfil-Session</c>.</param>
    Task<OrderDetailDto> UpdateMilestonesAsync(
        Guid orderId,
        UpdateOrderMilestonesRequest request,
        Guid actingUserId,
        CancellationToken cancellationToken = default,
        Guid? sessionId = null);

    /// <summary>
    /// Claims (or renews) an order for the acting fulfil user in the given Chrome-profile session.
    /// Optionally claims siblings sharing the same phone+zip. Throws <c>ConflictException</c> (409)
    /// when another user currently holds the primary order.
    /// </summary>
    Task<FulfilClaimResultDto> ClaimAsync(
        Guid orderId,
        Guid sessionId,
        string? sessionLabel,
        IReadOnlyList<Guid>? siblingOrderIds,
        Guid actingUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Releases the claim for one session (or all sessions when <paramref name="allSessions"/>).
    /// Lease clears only when no open sessions remain.
    /// </summary>
    Task<FulfilClaimResultDto> ReleaseClaimAsync(
        Guid orderId,
        Guid? sessionId,
        Guid actingUserId,
        bool isAdmin,
        bool allSessions,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Releases every open fulfil attempt of <paramref name="sessionId"/> owned by
    /// <paramref name="actingUserId"/> (logout / orphaned SignalR session). Lease clears
    /// only when no other sessions remain on each order. Publishes <c>orderClaimChanged</c>.
    /// </summary>
    Task<FulfilActiveClaimsDto> ReleaseSessionClaimsAsync(
        Guid sessionId,
        Guid actingUserId,
        CancellationToken cancellationToken = default);

    /// <summary>Lists orders currently held (lease not expired).</summary>
    Task<FulfilActiveClaimsDto> ListActiveClaimsAsync(CancellationToken cancellationToken = default);

    /// <summary>Paged fulfil attempt history for KPI.</summary>
    Task<FulfilAttemptListPageDto> ListAttemptsAsync(
        FulfilAttemptListQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Closes expired session attempts and clears leases when no sessions remain.
    /// Used by the expiry sweep background job.
    /// </summary>
    Task<int> CloseExpiredAttemptsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Matches SHEIN account orders (from the fulfil-extension) against purchased/untracked
    /// local orders by buyer name + zip + street (phone fallback), then fills tracking and the
    /// supplier cost breakdown (Supplier*). Does not call 4Seller.
    /// </summary>
    Task<SheinOrderSyncResultDto> SyncFromSheinAsync(
        SheinOrderSyncRequest request,
        Guid actingUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves the extension SHEIN auto-sync config (enabled + interval seconds) from Settings.
    /// </summary>
    Task<SheinSyncConfigDto> GetSheinSyncConfigAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks shop payout received (or unpaid) on the receiving-bank card linked to the order's shop.
    /// Upserts/deletes a SettlementPaid ledger row. Does not call 4Seller.
    /// </summary>
    Task<OrderDetailDto> UpdateSettlementAsync(
        Guid orderId,
        UpdateOrderSettlementRequest request,
        Guid actingUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks or clears USPS last-mile extra handling (<c>UspsProcessedAt</c>).
    /// When <c>ApplyFee</c> is true, sets BuyTrack from Settings; when false, stamps only.
    /// Does not call 4Seller.
    /// </summary>
    Task<OrderDetailDto> UpdateUspsProcessedAsync(
        Guid orderId,
        UpdateOrderUspsProcessedRequest request,
        Guid actingUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks or clears ThankCard purchase (<c>ThankCardProcessedAt</c>).
    /// When <c>ApplyFee</c> is true, sets ThankCard amount from Settings; when false, stamps only.
    /// Does not call 4Seller.
    /// </summary>
    Task<OrderDetailDto> UpdateThankCardProcessedAsync(
        Guid orderId,
        UpdateOrderThankCardProcessedRequest request,
        Guid actingUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks or clears ops REFUND handling (<c>RefundProcessedAt</c>). Does not call 4Seller or touch ledger.
    /// </summary>
    Task<OrderDetailDto> UpdateRefundProcessedAsync(
        Guid orderId,
        UpdateOrderRefundProcessedRequest request,
        Guid actingUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates buyer/seller/internal notes on 4Seller then local DB.
    /// </summary>
    Task<OrderDetailDto> UpdateNotesAsync(
        Guid orderId,
        UpdateOrderNoteRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates local operational notice (<c>Orders.Message</c>). Does not call 4Seller.
    /// Also appends to the order chat thread when non-empty (inbox + Telegram + realtime).
    /// </summary>
    Task<OrderDetailDto> UpdateMessageAsync(
        Guid orderId,
        UpdateOrderMessageRequest request,
        Guid actingUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes one local order (and cascade items). Does not call 4Seller.
    /// </summary>
    Task DeleteByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes local orders matching the same filters as <see cref="ListAsync"/> (ignores paging).
    /// Does not call 4Seller.
    /// </summary>
    Task<OrderDeleteResultDto> DeleteByFilterAsync(
        OrderListQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Relinks local orders to shops using <c>Order.FourSellerShopId</c>.
    /// Only sets <see cref="Domain.Entities.Order.ShopId"/> when a match is found (never clears).
    /// </summary>
    /// <param name="ownerUserId">Limit to one seller; null = all owners. Ignored when <paramref name="shopId"/> is set.</param>
    /// <param name="shopId">
    /// Rematch only the 4Seller ids carried by this shop (current + previous). Orders stay
    /// unscoped by owner so foreign-attributed rows are relinked too.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Number of orders whose <c>ShopId</c> changed.</returns>
    Task<int> RematchOrderShopLinksAsync(
        Guid? ownerUserId = null,
        Guid? shopId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Hàng đợi đơn NCC: mọi lần mua đã ghi nhận, kể cả lần chưa gắn được đơn sàn nào.
    /// </summary>
    Task<SupplierPurchaseListPageDto> ListSupplierPurchasesAsync(
        SupplierPurchaseQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gắn một lần mua vào đơn sàn rồi tính lại giá mua và sổ cái cho cả đơn cũ lẫn đơn mới.
    /// </summary>
    /// <param name="fulfilAccountId">
    /// Tài khoản mua hàng do người xử lý chọn, chỉ dùng khi lần mua chưa quy kết được tài khoản nào
    /// (email billing SHEIN không map tới <c>FulfilAccount</c>). Truyền vào một lần mua đã có tài
    /// khoản khác là lỗi nghiệp vụ — quy kết tiền không được đổi âm thầm qua thao tác gắn đơn.
    /// </param>
    Task<SupplierPurchaseDto> AttachSupplierPurchaseAsync(
        Guid id,
        Guid orderId,
        Guid actingUserId,
        Guid? fulfilAccountId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gỡ một lần mua khỏi đơn sàn. Dòng sync quay về hàng đợi (bằng chứng được giữ), dòng nhập tay
    /// bị xoá vì không còn nguồn nào ngoài người nhập.
    /// </summary>
    Task<SupplierPurchaseDto> DetachSupplierPurchaseAsync(
        Guid id,
        Guid actingUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Dựng lại dòng chi CostUpdate cho những đơn có lần mua được tính tiền mà sổ cái đang trống.
    /// </summary>
    /// <returns>Số đơn đã được ghi thêm dòng chi.</returns>
    Task<int> RebuildCostUpdateLedgerAsync(
        Guid actingUserId,
        CancellationToken cancellationToken = default);

    /// <summary>Đánh dấu (hoặc bỏ đánh dấu) một lần mua là đã xem và quyết định bỏ qua.</summary>
    Task<SupplierPurchaseDto> IgnoreSupplierPurchaseAsync(
        Guid id,
        bool ignored,
        Guid actingUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Xóa hẳn một lần mua NCC (và dòng chi CostUpdate gắn với nó). Sync sau có thể ghi nhận lại
    /// cùng billno nếu SHEIN vẫn trả về trong cửa sổ list.
    /// </summary>
    Task DeleteSupplierPurchaseAsync(
        Guid id,
        Guid actingUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Xóa hàng loạt lần mua NCC (cùng ngữ nghĩa với
    /// <see cref="DeleteSupplierPurchaseAsync"/>). Tối đa 200 id / lần.
    /// </summary>
    /// <returns>Số lần mua đã xóa.</returns>
    Task<int> BulkDeleteSupplierPurchasesAsync(
        IReadOnlyList<Guid> ids,
        Guid actingUserId,
        CancellationToken cancellationToken = default);
}
