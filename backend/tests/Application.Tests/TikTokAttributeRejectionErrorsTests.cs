using Shared.Constants;
using Xunit;

namespace Application.Tests;

/// <summary>
/// The failure message is the only evidence that TikTok has withdrawn an attribute value — 4Seller
/// keeps offering it with a numeric id — so parsing it correctly is what lets the system self-heal.
/// </summary>
public class TikTokAttributeRejectionErrorsTests
{
    /// <summary>Verbatim message from the category 903824 publish failures.</summary>
    private const string RealMessage =
        "Operation Not Allowed. Cannot use custom value names for product attribute `Washing "
        + "instructions`: attribute ID `100401` does not support custom value names. Remove or replace "
        + "the custom value `Do not dry clean` and retry.";

    [Fact]
    public void TryParseCustomValueRejection_ReadsAttributeAndValue()
    {
        var parsed = TikTokAttributeRejectionErrors.TryParseCustomValueRejection(
            RealMessage,
            out var attributeId,
            out var attributeName,
            out var valueName);

        Assert.True(parsed);
        Assert.Equal("100401", attributeId);
        Assert.Equal("Washing instructions", attributeName);
        Assert.Equal("Do not dry clean", valueName);
    }

    [Fact]
    public void TryParseCustomValueRejection_ReadsMessageBehindPublishFailedPrefix()
    {
        var parsed = TikTokAttributeRejectionErrors.TryParseCustomValueRejection(
            $"【Publish Failed】{RealMessage}",
            out var attributeId,
            out _,
            out var valueName);

        Assert.True(parsed);
        Assert.Equal("100401", attributeId);
        Assert.Equal("Do not dry clean", valueName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("【Publish Failed】Specifics.Region of origin")]
    [InlineData("Product :Product Detail Page (PDP) Inconsistent Information")]
    [InlineData("【Publish Failed】Listing.Back.Day_Limit")]
    public void TryParseCustomValueRejection_IgnoresUnrelatedFailures(string? message)
    {
        Assert.False(TikTokAttributeRejectionErrors.TryParseCustomValueRejection(
            message,
            out _,
            out _,
            out _));
    }

    [Fact]
    public void TryParseCustomValueRejection_IgnoresCustomValueTextWithoutAnAttributeId()
    {
        // Nothing to block without the attribute id: one value name means different enums per
        // attribute, so a partial parse would blacklist the wrong thing.
        Assert.False(TikTokAttributeRejectionErrors.TryParseCustomValueRejection(
            "Cannot use custom value names for product attribute `Washing instructions`.",
            out _,
            out _,
            out _));
    }
}
