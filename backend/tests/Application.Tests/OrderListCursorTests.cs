using Application.Services;
using Xunit;

namespace Application.Tests;

public class OrderListCursorTests
{
    [Fact]
    public void RoundTrips_TimeKeyAndId()
    {
        var time = new DateTime(2026, 8, 20, 13, 45, 12, 345, DateTimeKind.Utc).AddTicks(6789);
        var id = Guid.Parse("3f2504e0-4f89-11d3-9a0c-0305e82c3301");

        var decoded = OrderListCursor.TryDecode(OrderListCursor.Encode(time, id));

        Assert.NotNull(decoded);
        Assert.Equal(time, decoded!.Value.TimeKey);
        Assert.Equal(id, decoded.Value.Id);
    }

    [Fact]
    public void Encode_IsUrlSafe()
    {
        for (var i = 0; i < 200; i++)
        {
            var cursor = OrderListCursor.Encode(DateTime.UtcNow.AddSeconds(i), Guid.NewGuid());
            Assert.DoesNotContain('+', cursor);
            Assert.DoesNotContain('/', cursor);
            Assert.DoesNotContain('=', cursor);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-cursor")]
    [InlineData("!!!!")]
    // Shapes an older or newer build could produce: a bad cursor must fall back to page one
    // rather than page the caller to a wrong position.
    [InlineData("MTIz")]
    [InlineData("MTIzLg")]
    [InlineData("Lm5vdGFndWlk")]
    [InlineData("OTk5OTk5OTk5OTk5OTk5OTk5OTk5LjNmMjUwNGUwNGY4OTExZDM5YTBjMDMwNWU4MmMzMzAx")]
    public void TryDecode_RejectsAnythingItDidNotWrite(string? cursor)
    {
        Assert.Null(OrderListCursor.TryDecode(cursor));
    }
}
