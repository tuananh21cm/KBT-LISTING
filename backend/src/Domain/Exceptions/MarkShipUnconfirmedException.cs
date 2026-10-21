namespace Domain.Exceptions;

/// <summary>
/// Lệnh Mark Ship <b>đã</b> được gửi lên 4Seller nhưng không lấy được kết quả trong thời gian chờ.
///
/// Khác hẳn một lỗi thường: 4Seller có thể đã đẩy mã vận đơn lên sàn thành công rồi, chỉ là trả kết
/// quả chậm hơn vòng poll. Coi nó là lỗi tạm rồi thử lại sau cooldown nghĩa là đẩy lần hai cùng một
/// mã vận đơn cho cùng một đơn, và không có idempotency key nào ở phía 4Seller để chặn. Phải để
/// người xử lý kiểm tra trên sàn trước khi đẩy lại.
/// </summary>
public class MarkShipUnconfirmedException : BusinessException
{
    /// <summary>Mã request 4Seller đã nhận — dùng để tra lại kết quả.</summary>
    public string? RequestId { get; }

    /// <summary>Initializes the exception.</summary>
    public MarkShipUnconfirmedException(string message, string? requestId = null) : base(message)
    {
        RequestId = requestId;
    }
}
