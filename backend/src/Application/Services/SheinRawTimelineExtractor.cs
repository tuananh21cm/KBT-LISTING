using System.Text.Json;

namespace Application.Services;

/// <summary>
/// Suy lại mốc rời kho và mốc giao từ payload SHEIN gốc đã lưu trong
/// <c>OrderSupplierLinks.RawPayload</c>, không cần gọi lại SHEIN.
///
/// Dùng cho đợt sửa dữ liệu cũ: bản extension trước đây lấy <c>delivery_time</c> làm mốc giao,
/// trong khi đó là mốc rời kho. Đối chiếu 156 payload gốc cho thấy hai trường tách bạch tuyệt đối:
/// <c>signed_time</c> vắng ở toàn bộ 57 đơn còn <c>shipped</c> nhưng có đủ ở 99/99 đơn đã giao, và
/// luôn muộn hơn <c>delivery_time</c> trung bình 5,9 ngày. Vì vậy mốc "Giao" cũ sớm hơn thực tế
/// gần 6 ngày và phải dựng lại từ <c>signed_time</c>.
///
/// Quy tắc ở đây phải khớp với <c>extractSheinMilestones</c> bên extension để dữ liệu backfill và
/// dữ liệu sync mới không mâu thuẫn nhau.
/// </summary>
public static class SheinRawTimelineExtractor
{
    /// <summary>Mốc suy được từ payload gốc; <c>null</c> nghĩa là payload không có clock thật.</summary>
    public sealed record Result(DateTime? ShippedAt, DateTime? DeliveredAt);

    private static readonly Result Empty = new(null, null);

    /// <summary>
    /// Đọc phong bì <c>{capturedAt, listItem, detail}</c> rồi suy mốc. Mảnh nào hỏng hoặc bị cắt
    /// thì bỏ qua mảnh đó thay vì ném lỗi — payload dài bị extension cắt bớt vẫn còn giá trị ở
    /// mảnh còn lại.
    /// </summary>
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

        // Detail giàu thông tin hơn nên xét trước, thiếu mốc nào mới lấy từ listItem.
        var fromDetail = ExtractFragment(detail, unwrapOrder: true);
        var fromList = ExtractFragment(listItem, unwrapOrder: false);

        return new Result(
            fromDetail.ShippedAt ?? fromList.ShippedAt,
            fromDetail.DeliveredAt ?? fromList.DeliveredAt);
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

            return new Result(ExtractShipped(root), ExtractDelivered(root, doc.RootElement));
        }
        catch (JsonException)
        {
            // Payload bị cắt giữa chừng: coi như không có dữ liệu, không làm hỏng cả đợt backfill.
            return Empty;
        }
    }

    private static DateTime? ExtractShipped(JsonElement root)
    {
        // Xét danh sách gói trước trường cấp đơn: với đơn nhiều gói, <c>delivery_time</c> ở cấp đơn
        // báo gói rời kho **muộn nhất**, trong khi đơn đã bắt đầu di chuyển từ gói **đầu tiên**.
        DateTime? earliest = null;
        foreach (var pkg in Packages(root))
        {
            var at = ReadUnix(pkg, "deliveryTime") ?? ReadUnix(pkg, "delivery_time");
            if (at is null) continue;
            if (earliest is null || at < earliest) earliest = at;
        }
        if (earliest is not null) return earliest;

        return ReadUnix(root, "delivery_time") ?? ReadUnix(root, "deliveryTime");
    }

    private static DateTime? ExtractDelivered(JsonElement root, JsonElement fragmentRoot)
    {
        DateTime? latest = null;
        var total = 0;
        var signed = 0;
        foreach (var pkg in Packages(root))
        {
            total++;
            var at = ReadUnix(pkg, "signed_time") ?? ReadUnix(pkg, "signedTime");
            if (at is null) continue;
            signed++;
            if (latest is null || at > latest) latest = at;
        }

        // Còn gói chưa ký nhận thì đơn chưa giao xong.
        if (total > 0 && signed == total && latest is not null) return latest;

        return ExtractDeliveredFromTrackInfo(root) ?? ExtractDeliveredFromTrackInfo(fragmentRoot);
    }

    /// <summary>
    /// <c>track_info.time</c> là mốc của sự kiện vận chuyển gần nhất, chỉ là mốc giao khi
    /// <c>status_desc</c> nói vậy. Với đơn đang trên đường, trường này trỏ tới thời điểm gần hiện
    /// tại nên đọc vô điều kiện sẽ đánh dấu đã giao nhầm.
    /// </summary>
    private static DateTime? ExtractDeliveredFromTrackInfo(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object) return null;
        if (!root.TryGetProperty("track_info", out var info)
            || info.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var desc = info.TryGetProperty("status_desc", out var d) ? d.ToString() : string.Empty;
        if (desc.IndexOf("deliver", StringComparison.OrdinalIgnoreCase) < 0
            && desc.IndexOf("signed", StringComparison.OrdinalIgnoreCase) < 0
            && desc.IndexOf("received", StringComparison.OrdinalIgnoreCase) < 0)
        {
            return null;
        }

        return ReadUnix(info, "time");
    }

    private static IEnumerable<JsonElement> Packages(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object) yield break;
        if (!root.TryGetProperty("order_package_info_list", out var list)
            || list.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var pkg in list.EnumerateArray())
        {
            if (pkg.ValueKind == JsonValueKind.Object) yield return pkg;
        }
    }

    /// <summary>Đọc Unix giây (SHEIN trả cả dạng số lẫn chuỗi) thành UTC.</summary>
    private static DateTime? ReadUnix(JsonElement node, string name)
    {
        if (node.ValueKind != JsonValueKind.Object) return null;
        if (!node.TryGetProperty(name, out var value)) return null;

        long seconds;
        switch (value.ValueKind)
        {
            case JsonValueKind.Number:
                if (!value.TryGetInt64(out seconds)) return null;
                break;
            case JsonValueKind.String:
                if (!long.TryParse(value.GetString(), out seconds)) return null;
                break;
            default:
                return null;
        }

        if (seconds <= 0) return null;
        if (seconds > 100_000_000_000L) seconds /= 1000;
        // Chặn giá trị vô lý (2000-01-01 .. 2100-01-01) để rác không thành mốc timeline.
        if (seconds < 946_684_800L || seconds > 4_102_444_800L) return null;
        return DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime;
    }
}
