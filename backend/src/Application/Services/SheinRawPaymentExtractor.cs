using System.Text.Json;

namespace Application.Services;

/// <summary>
/// Suy trạng thái thanh toán của một billno SHEIN từ payload gốc đã lưu trong
/// <c>OrderSupplierLinks.RawPayload</c>, không cần gọi lại SHEIN.
///
/// SHEIN cho tạo đơn trước rồi mới thu tiền, và tự huỷ khi hết <c>order_expire_time</c> (mặc định
/// 4 giờ) nếu không ai trả. Đơn ghost đó có <c>addTime</c> bình thường nhưng mốc trả tiền bằng 0:
/// <c>pay_time</c> ở list tính bằng giây, <c>paymentTime</c> ở detail tính bằng millisecond. Khảo
/// sát 50 billno đã thu payload cho thấy hai nhóm tách bạch tuyệt đối — cả 50 đơn thật đều có mốc
/// trả tiền &gt; 0, chỉ đơn ghost GSU18J247000TWB (đơn TikTok 577494030896631910) bằng 0.
///
/// Quy tắc ở đây phải khớp <c>extractSheinPaidAt</c> bên extension để dữ liệu backfill và dữ liệu
/// sync mới không mâu thuẫn nhau.
/// </summary>
public static class SheinRawPaymentExtractor
{
    /// <summary>
    /// <c>PaidAt</c> có giá trị ⇒ đã trả tiền. <c>Unpaid</c> chỉ đúng khi payload nói rõ mốc trả
    /// tiền bằng 0. Cả hai rỗng ⇒ payload không đủ dữ kiện để kết luận.
    /// </summary>
    public sealed record Result(DateTime? PaidAt, bool Unpaid);

    private static readonly Result Empty = new(null, false);

    private static readonly string[] PaymentKeys =
        ["pay_time", "paymentTime", "payment_time", "payTime"];

    /// <summary>Đọc phong bì <c>{capturedAt, listItem, detail}</c> rồi suy trạng thái thanh toán.</summary>
    public static Result Extract(string? envelopeJson)
    {
        if (string.IsNullOrWhiteSpace(envelopeJson)) return Empty;

        string? listItem = null;
        string? detail = null;
        try
        {
            using var envelope = JsonDocument.Parse(envelopeJson);
            var root = envelope.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return Empty;
            if (root.TryGetProperty("listItem", out var li) && li.ValueKind == JsonValueKind.String)
            {
                listItem = li.GetString();
            }
            if (root.TryGetProperty("detail", out var dt) && dt.ValueKind == JsonValueKind.String)
            {
                detail = dt.GetString();
            }
        }
        catch (JsonException)
        {
            return Empty;
        }

        var fromDetail = ExtractFragment(detail, unwrapOrder: true);
        var fromList = ExtractFragment(listItem, unwrapOrder: false);

        // Một mốc trả tiền thật ở bất kỳ mảnh nào cũng thắng: mảnh bị cắt hoặc rút gọn thiếu khoá
        // không được phép biến đơn đã trả thành đơn ghost.
        var paidAt = fromDetail.PaidAt ?? fromList.PaidAt;
        if (paidAt is not null) return new Result(paidAt, false);

        return new Result(null, fromDetail.Unpaid || fromList.Unpaid);
    }

    private static Result ExtractFragment(string? json, bool unwrapOrder)
    {
        if (string.IsNullOrWhiteSpace(json)) return Empty;

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return Empty;
            if (unwrapOrder && root.TryGetProperty("order", out var order)
                && order.ValueKind == JsonValueKind.Object)
            {
                root = order;
            }

            var sawZero = false;
            foreach (var key in PaymentKeys)
            {
                if (!root.TryGetProperty(key, out var value)) continue;

                var raw = value.ValueKind switch
                {
                    JsonValueKind.Number => value.ToString(),
                    JsonValueKind.String => value.GetString(),
                    _ => null
                };
                if (string.IsNullOrWhiteSpace(raw)) continue;
                if (!long.TryParse(raw.Trim(), out var epoch)) continue;

                if (epoch <= 0)
                {
                    sawZero = true;
                    continue;
                }

                // List trả giây, detail trả millisecond.
                if (epoch > 100_000_000_000L) epoch /= 1000;
                // Ngoài khoảng 2000..2100 là rác, không phải mốc trả tiền.
                if (epoch < 946_684_800L || epoch > 4_102_444_800L) continue;

                return new Result(DateTimeOffset.FromUnixTimeSeconds(epoch).UtcDateTime, false);
            }

            return new Result(null, sawZero);
        }
        catch (JsonException)
        {
            // Payload bị cắt giữa chừng: coi như không kết luận được, không làm hỏng cả đợt backfill.
            return Empty;
        }
    }
}
