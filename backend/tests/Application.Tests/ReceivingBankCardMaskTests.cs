using Application.Services;
using Shared.Constants;
using Xunit;

namespace Application.Tests;

public class ReceivingBankCardMaskTests
{
    [Theory]
    [InlineData("********4529", "00395823000144529", true)]
    [InlineData("********4529", "0039****4529", true)]
    [InlineData("0039****8618", "00395823000148618", true)]
    [InlineData("********4529", "00395823000148618", false)]
    [InlineData("********4529", "0039****8618", false)]
    [InlineData(null, "00395823000144529", false)]
    [InlineData("", "00395823000144529", false)]
    [InlineData("********4529", null, false)]
    [InlineData("***12", "00395823000144529", false)]
    public void MatchesAccountLast4_ComparesTrailingFourDigits(
        string? mask,
        string? account,
        bool expected)
    {
        Assert.Equal(expected, ReceivingBankCardMask.MatchesAccountLast4(mask, account));
    }

    [Fact]
    public void ExtractMaskFromPayoutNote_ReadsCardToken()
    {
        Assert.Equal(
            "********4529",
            ReceivingBankCardMask.ExtractMaskFromPayoutNote(
                "TikTok Shop-USUSLCA3ER3S, payout ID 1 · card ********4529"));
        Assert.Null(ReceivingBankCardMask.ExtractMaskFromPayoutNote("TikTok payout 1"));
    }

    [Fact]
    public void IsPayoutNoteMismatched_OnlyWhenLast4Differs()
    {
        Assert.True(ReceivingBankCardMask.IsPayoutNoteMismatched(
            "card ********4529",
            "00395823000148618"));
        Assert.False(ReceivingBankCardMask.IsPayoutNoteMismatched(
            "card ********8618",
            "00395823000148618"));
        Assert.True(ReceivingBankCardMask.IsPayoutNoteMismatched(null, "00395823000148618"));
    }

    [Fact]
    public void ResolveShopBankMatchStatus_Matched_WhenLast4Equal()
    {
        var bankId = Guid.NewGuid();
        Assert.Equal(
            ShopBankMatchStatuses.Matched,
            ReceivingBankCardMask.ResolveShopBankMatchStatus(
                bankId,
                "00395823000144529",
                "********4529"));
    }

    [Fact]
    public void ResolveShopBankMatchStatus_Mismatched_WhenLast4Differ()
    {
        var bankId = Guid.NewGuid();
        Assert.Equal(
            ShopBankMatchStatuses.Mismatched,
            ReceivingBankCardMask.ResolveShopBankMatchStatus(
                bankId,
                "00395823000144529",
                "********8618"));
    }

    [Fact]
    public void ResolveShopBankMatchStatus_MissingSettlement_WhenTikTokBankAbsent()
    {
        var bankId = Guid.NewGuid();
        Assert.Equal(
            ShopBankMatchStatuses.MissingSettlement,
            ReceivingBankCardMask.ResolveShopBankMatchStatus(
                bankId,
                "00395823000144529",
                null));
        Assert.Equal(
            ShopBankMatchStatuses.MissingSettlement,
            ReceivingBankCardMask.ResolveShopBankMatchStatus(
                bankId,
                "00395823000144529",
                "***12"));
    }

    [Fact]
    public void ResolveShopBankMatchStatus_MissingReceivingBank_WhenNoCardOrShortNumber()
    {
        Assert.Equal(
            ShopBankMatchStatuses.MissingReceivingBank,
            ReceivingBankCardMask.ResolveShopBankMatchStatus(
                null,
                null,
                "********4529"));
        Assert.Equal(
            ShopBankMatchStatuses.MissingReceivingBank,
            ReceivingBankCardMask.ResolveShopBankMatchStatus(
                Guid.NewGuid(),
                "12",
                "********4529"));
    }
}
