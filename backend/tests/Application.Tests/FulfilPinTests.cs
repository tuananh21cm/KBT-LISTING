using Shared.Helpers;
using Xunit;

namespace Application.Tests;

public class FulfilPinTests
{
    private const string Pepper = "test-pepper-for-fulfil-pin";

    [Theory]
    [InlineData("1234")]
    [InlineData("0000")]
    [InlineData("9999")]
    public void IsValid_AcceptsFourDigits(string pin)
    {
        Assert.True(FulfilPin.IsValid(pin));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("123")]
    [InlineData("12345")]
    [InlineData("12a4")]
    [InlineData("12 4")]
    public void IsValid_RejectsInvalid(string? pin)
    {
        Assert.False(FulfilPin.IsValid(pin));
    }

    [Fact]
    public void Normalize_TrimsWhitespace()
    {
        Assert.Equal("1234", FulfilPin.Normalize("  1234  "));
    }

    [Fact]
    public void ComputeHash_IsDeterministic()
    {
        var a = FulfilPin.ComputeHash("1234", Pepper);
        var b = FulfilPin.ComputeHash("1234", Pepper);

        Assert.Equal(a, b);
        Assert.Equal(64, a.Length);
        Assert.Matches("^[0-9a-f]{64}$", a);
    }

    [Fact]
    public void ComputeHash_DiffersByPinAndPepper()
    {
        var baseHash = FulfilPin.ComputeHash("1234", Pepper);

        Assert.NotEqual(baseHash, FulfilPin.ComputeHash("1235", Pepper));
        Assert.NotEqual(baseHash, FulfilPin.ComputeHash("1234", Pepper + "x"));
    }
}
