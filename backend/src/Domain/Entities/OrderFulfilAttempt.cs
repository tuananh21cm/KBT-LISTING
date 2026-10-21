using Domain.Common;

namespace Domain.Entities;

/// <summary>
/// Một lượt fulfil đi mua một đơn <b>trong một phiên extension (Chrome profile)</b>: mở khi phiên đó
/// click vào đơn (bắt đầu đếm thời gian), đóng khi mua xong / nhả / hết hạn. Append-only — nguồn
/// KPI thời gian mua, không phải trạng thái hot path (trạng thái hiện tại nằm ở cột lease trên Orders).
///
/// Cùng một user mở nhiều profile để xử lý chung một đơn là bình thường ⇒ một đơn có thể có
/// nhiều attempt mở song song, nhưng chúng phải cùng <see cref="UserId"/> (lease đảm bảo điều đó).
/// </summary>
public class OrderFulfilAttempt : BaseEntity
{
    /// <summary>Row identifier.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Parent order.</summary>
    public Guid OrderId { get; set; }

    /// <summary>Parent order navigation.</summary>
    public Order? Order { get; set; }

    /// <summary>Fulfil thực hiện lượt mua này.</summary>
    public Guid UserId { get; set; }

    /// <summary>User navigation.</summary>
    public ApplicationUser? User { get; set; }

    /// <summary>
    /// Phiên extension (một Chrome profile) — từ header <c>X-Fulfil-Session</c>.
    /// Mốc bắt đầu / kết thúc và <see cref="DurationSeconds"/> tính riêng cho phiên này.
    /// </summary>
    public Guid SessionId { get; set; }

    /// <summary>Nhãn ngắn giúp người dùng nhận ra profile nào (optional, extension gửi lên).</summary>
    public string? SessionLabel { get; set; }

    /// <summary>Gom các attempt được tạo cùng một lần click nhóm khách (phone+zip).</summary>
    public Guid BatchId { get; set; }

    /// <summary>Khoá nhóm khách <c>phone|zip</c> lúc claim (SheinMatchKeys), null khi thiếu dữ liệu.</summary>
    public string? CustomerKey { get; set; }

    /// <summary>Lúc phiên này click vào đơn — mốc bắt đầu đếm của riêng phiên.</summary>
    public DateTime StartedAt { get; set; }

    /// <summary>Heartbeat cuối của phiên này — biết profile đó còn đang làm hay đã bỏ dở.</summary>
    public DateTime LastRenewedAt { get; set; }

    /// <summary>Hạn của <b>phiên này</b> tại lần renew cuối (lease trên Orders lấy MAX các phiên).</summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>Null = lượt đang mở. Có giá trị = đã chốt theo <see cref="Outcome"/>.</summary>
    public DateTime? ClosedAt { get; set; }

    /// <summary>
    /// <see cref="FulfilAttemptOutcomes"/>: Purchased / Released / Expired / TakenOver / Superseded / Reverted.
    /// <c>Superseded</c> = phiên khác của cùng user đã bấm «Đã mua» cho đơn này.
    /// </summary>
    public string? Outcome { get; set; }

    /// <summary>Thời điểm mua xong (= <c>Order.FulfilPurchasedAt</c>) khi Outcome = Purchased.</summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>
    /// Thời gian phiên này đi đơn = ClosedAt − StartedAt, tính sẵn để KPI không phải tính lại.
    /// Null với lượt mua không qua claim (stamp tay / SHEIN sync) — 0 giây sẽ làm nhiễu trung bình.
    /// </summary>
    public int? DurationSeconds { get; set; }
}

/// <summary>Known <see cref="OrderFulfilAttempt.Outcome"/> values.</summary>
public static class FulfilAttemptOutcomes
{
    /// <summary>Phiên bấm «Đã mua» — lượt mua thật.</summary>
    public const string Purchased = "Purchased";

    /// <summary>Phiên chủ động nhả đơn.</summary>
    public const string Released = "Released";

    /// <summary>TTL hết mà không renew / nhả.</summary>
    public const string Expired = "Expired";

    /// <summary>Người khác force-stamp mua khi phiên này còn giữ.</summary>
    public const string TakenOver = "TakenOver";

    /// <summary>Phiên khác của cùng user đã stamp mua — đóng các phiên còn lại.</summary>
    public const string Superseded = "Superseded";

    /// <summary>Gỡ stamp mua (clearFulfilPurchasedAt) — KPI bỏ qua.</summary>
    public const string Reverted = "Reverted";
}
