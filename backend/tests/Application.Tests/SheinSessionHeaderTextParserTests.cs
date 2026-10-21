using Shared.Constants;
using Shared.Helpers;
using Xunit;

namespace Application.Tests;

/// <summary>
/// Parser khối header dán tay. Nguồn thực tế là DevTools → Copy request headers, nên nó phải chịu
/// được cả pseudo-header HTTP/2, dòng request, và header mà HttpClient không cho set.
/// </summary>
public class SheinSessionHeaderTextParserTests
{
    [Fact]
    public void ParsesNameValueLines()
    {
        var parsed = SheinSessionHeaderTextParser.Parse(
            "armortoken: abc123\nx-csrf-token: def456\nsmdeviceid: ghi789");

        Assert.Equal(3, parsed.Count);
        Assert.Equal("abc123", parsed["armortoken"]);
        Assert.Equal("def456", parsed["x-csrf-token"]);
    }

    [Fact]
    public void LowercasesNamesAndTrimsValues()
    {
        var parsed = SheinSessionHeaderTextParser.Parse("  ArmorToken :   abc123   ");

        Assert.Equal("abc123", parsed["armortoken"]);
        Assert.Contains("armortoken", parsed.Keys);
    }

    [Fact]
    public void KeepsValueColons()
    {
        // Chỉ dấu hai chấm đầu tiên là dấu tách; URL trong value phải giữ nguyên.
        var parsed = SheinSessionHeaderTextParser.Parse("referer-hint: https://us.shein.com/user/orders");

        Assert.Equal("https://us.shein.com/user/orders", parsed["referer-hint"]);
    }

    [Fact]
    public void DropsBlankLinesAndComments()
    {
        var parsed = SheinSessionHeaderTextParser.Parse(
            "\n# dán từ DevTools\n\narmortoken: abc\n\n");

        Assert.Single(parsed);
        Assert.Equal("abc", parsed["armortoken"]);
    }

    [Fact]
    public void DropsHttp2PseudoHeaders()
    {
        var parsed = SheinSessionHeaderTextParser.Parse(
            ":authority: us.shein.com\n:method: GET\n:path: /api/orders\narmortoken: abc");

        Assert.Single(parsed);
        Assert.Equal("abc", parsed["armortoken"]);
    }

    [Fact]
    public void DropsRequestLine()
    {
        var parsed = SheinSessionHeaderTextParser.Parse(
            "GET /api/orders/base/get_order_info/get HTTP/2\narmortoken: abc");

        Assert.Single(parsed);
        Assert.Equal("abc", parsed["armortoken"]);
    }

    [Fact]
    public void DropsDeniedHeaders()
    {
        // Cookie đi ở field riêng; content-* thuộc HttpContent nên HttpClient từ chối.
        var parsed = SheinSessionHeaderTextParser.Parse(
            "cookie: a=1; b=2\ncontent-length: 42\nhost: us.shein.com\narmortoken: abc");

        Assert.Single(parsed);
        Assert.Equal("abc", parsed["armortoken"]);
    }

    [Fact]
    public void LastLineWinsOnDuplicate()
    {
        var parsed = SheinSessionHeaderTextParser.Parse("armortoken: old\narmortoken: new");

        Assert.Equal("new", parsed["armortoken"]);
    }

    [Fact]
    public void DropsOversizedValue()
    {
        var huge = new string('x', SheinSessionKeys.MaxHeaderValueLength + 1);
        var parsed = SheinSessionHeaderTextParser.Parse($"armortoken: {huge}\nsmdeviceid: ok");

        Assert.Single(parsed);
        Assert.Equal("ok", parsed["smdeviceid"]);
    }

    [Fact]
    public void StopsAtHeaderCap()
    {
        var lines = string.Join('\n', Enumerable.Range(0, SheinSessionKeys.MaxHeaders + 10)
            .Select(i => $"x-header-{i}: v{i}"));

        var parsed = SheinSessionHeaderTextParser.Parse(lines);

        Assert.Equal(SheinSessionKeys.MaxHeaders, parsed.Count);
    }

    [Fact]
    public void EmptyInputReturnsEmpty()
    {
        Assert.Empty(SheinSessionHeaderTextParser.Parse(null));
        Assert.Empty(SheinSessionHeaderTextParser.Parse("   "));
    }
}
