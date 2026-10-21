using System.Text.Json;
using Application.Services;
using Xunit;

namespace Application.Tests;

/// <summary>
/// Quy tắc suy trạng thái thanh toán từ payload SHEIN gốc. Fixture dựng theo đúng hình dạng payload
/// thật đã thu qua cờ <c>capture_raw_payload</c>: phong bì <c>{listItem, detail}</c> chứa JSON dạng
/// chuỗi, list dùng <c>pay_time</c> tính giây còn detail dùng <c>paymentTime</c> tính millisecond.
/// </summary>
public class SheinRawPaymentExtractorTests
{
    private static string Envelope(object? listItem, object? detail)
    {
        var payload = new Dictionary<string, object?>
        {
            ["capturedAt"] = "2026-08-02T10:01:02Z"
        };
        if (listItem is not null) payload["listItem"] = JsonSerializer.Serialize(listItem);
        if (detail is not null) payload["detail"] = JsonSerializer.Serialize(detail);
        return JsonSerializer.Serialize(payload);
    }

    [Fact]
    public void Extract_MarksUnpaid_WhenListPayTimeIsZero()
    {
        // GSU18J247000TWB thật: tạo 26/07 08:59:53, hạn trả tiền 4 giờ sau, pay_time = 0.
        var json = Envelope(
            new { billno = "GSU18J247000TWB", addTime = 1785056393, pay_time = 0, order_expire_time = 1785070793 },
            null);

        var result = SheinRawPaymentExtractor.Extract(json);

        Assert.Null(result.PaidAt);
        Assert.True(result.Unpaid);
    }

    [Fact]
    public void Extract_MarksUnpaid_WhenDetailPaymentTimeIsZero()
    {
        var json = Envelope(null, new { order = new { billno = "GSU18J247000TWB", paymentTime = 0 } });

        Assert.True(SheinRawPaymentExtractor.Extract(json).Unpaid);
    }

    [Fact]
    public void Extract_ReadsSecondsFromListItem()
    {
        var json = Envelope(new { billno = "GSU18F24400W3W3", pay_time = 1784598661 }, null);

        var result = SheinRawPaymentExtractor.Extract(json);

        Assert.Equal(new DateTime(2026, 7, 21, 1, 51, 1, DateTimeKind.Utc), result.PaidAt);
        Assert.False(result.Unpaid);
    }

    [Fact]
    public void Extract_ReadsMillisecondsFromDetail_AsSameInstant()
    {
        var json = Envelope(null, new { order = new { paymentTime = 1784598661000L } });

        Assert.Equal(
            new DateTime(2026, 7, 21, 1, 51, 1, DateTimeKind.Utc),
            SheinRawPaymentExtractor.Extract(json).PaidAt);
    }

    [Fact]
    public void Extract_PrefersRealClock_WhenOneFragmentIsMissingTheKey()
    {
        // Mảnh list bị rút gọn (pay_time = 0) nhưng detail có mốc thật: không được kết luận ghost.
        var json = Envelope(
            new { pay_time = 0 },
            new { order = new { paymentTime = 1784598661000L } });

        var result = SheinRawPaymentExtractor.Extract(json);

        Assert.Equal(new DateTime(2026, 7, 21, 1, 51, 1, DateTimeKind.Utc), result.PaidAt);
        Assert.False(result.Unpaid);
    }

    [Fact]
    public void Extract_StaysUndecided_WhenPaymentKeyIsAbsent()
    {
        var json = Envelope(new { billno = "GSU-STUB", addTime = 1785056393 }, null);

        var result = SheinRawPaymentExtractor.Extract(json);

        Assert.Null(result.PaidAt);
        Assert.False(result.Unpaid);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{ not json")]
    public void Extract_StaysUndecided_OnMissingOrBrokenEnvelope(string? envelopeJson)
    {
        var result = SheinRawPaymentExtractor.Extract(envelopeJson);

        Assert.Null(result.PaidAt);
        Assert.False(result.Unpaid);
    }

    [Fact]
    public void Extract_StaysUndecided_WhenFragmentWasTruncated()
    {
        // Extension cắt payload ở 256 KB nên mảnh không còn parse được; không được suy bừa.
        var json = "{\"listItem\":\"{\\\"billno\\\":\\\"GSU-CUT\\\",\\\"pay_ti\"}";

        var result = SheinRawPaymentExtractor.Extract(json);

        Assert.Null(result.PaidAt);
        Assert.False(result.Unpaid);
    }

    [Fact]
    public void Extract_IgnoresGarbageEpoch_OutsideSaneRange()
    {
        var json = Envelope(new { pay_time = 12345 }, null);

        var result = SheinRawPaymentExtractor.Extract(json);

        Assert.Null(result.PaidAt);
        Assert.False(result.Unpaid);
    }
}
