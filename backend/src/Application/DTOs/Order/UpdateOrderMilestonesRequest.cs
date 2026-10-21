namespace Application.DTOs.Order;

/// <summary>
/// Partial update of local fulfil pipeline milestones (UTC). Null timestamp fields are left unchanged.
/// Pass an explicit value to set; use <see cref="ClearFulfilPurchasedAt"/> to undo a purchase stamp.
/// Chỉ chấp nhận <see cref="FulfilPurchasedAt"/> / <see cref="TrackingAddedAt"/> / clear mua.
/// <see cref="InTransitAt"/> / <see cref="DeliveredAt"/> / <see cref="RefundedAt"/> bị từ chối
/// (mốc đó thuộc Sync Fulfil / Sync 4Seller).
/// </summary>
public class UpdateOrderMilestonesRequest
{
    /// <summary>When the order was purchased / cost recorded locally.</summary>
    public DateTime? FulfilPurchasedAt { get; set; }

    /// <summary>
    /// Undo accidental «Đã mua»: clear <see cref="FulfilPurchasedAt"/> (+ provenance).
    /// Cannot be combined with a non-null <see cref="FulfilPurchasedAt"/>.
    /// </summary>
    public bool ClearFulfilPurchasedAt { get; set; }

    /// <summary>
    /// When true, stamp purchase even if another fulfil currently holds the lease
    /// (closes their attempts as TakenOver). Extension prompts before sending.
    /// </summary>
    public bool Force { get; set; }

    /// <summary>When tracking first appeared (rarely edited).</summary>
    public DateTime? TrackingAddedAt { get; set; }

    /// <summary>Obsolete — rejected by API (Sync Fulfil owns Transit).</summary>
    public DateTime? InTransitAt { get; set; }

    /// <summary>Obsolete — rejected by API (Sync Fulfil owns Delivered).</summary>
    public DateTime? DeliveredAt { get; set; }

    /// <summary>Obsolete — rejected by API (Sync 4Seller owns Refund).</summary>
    public DateTime? RefundedAt { get; set; }
}
