using Shared.Constants;

namespace Application.Services;

/// <summary>
/// Compare TikTok <c>bank_card_mask_code</c> with the shop's receiving-bank account (last 4 digits).
/// </summary>
public static class ReceivingBankCardMask
{
    /// <summary>Digits compared at the account tail.</summary>
    public const int TailLength = 4;

    /// <summary>Last 4 digits, or null when fewer than 4 digit characters.</summary>
    public static string? ExtractLast4Digits(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var digits = string.Concat(value.Where(char.IsDigit));
        return digits.Length >= TailLength ? digits[^TailLength..] : null;
    }

    /// <summary>
    /// True when both sides have ≥4 digits and the tails match
    /// (e.g. <c>********4529</c> vs <c>0039****4529</c>).
    /// Missing / short mask does not match.
    /// </summary>
    public static bool MatchesAccountLast4(string? bankCardMask, string? accountNumber)
    {
        var maskTail = ExtractLast4Digits(bankCardMask);
        var accountTail = ExtractLast4Digits(accountNumber);
        return maskTail is not null
            && accountTail is not null
            && string.Equals(maskTail, accountTail, StringComparison.Ordinal);
    }

    /// <summary>
    /// Trạng thái đối chiếu bank TTS (settlement) với thẻ nhận tiền của shop.
    /// Ưu tiên: chưa gắn thẻ → chưa sync TTS → khớp / lệch.
    /// </summary>
    public static string ResolveShopBankMatchStatus(
        Guid? receivingBankAccountId,
        string? receivingAccountNumber,
        string? settlementAccountNumber)
    {
        var receivingTail = ExtractLast4Digits(receivingAccountNumber);
        if (receivingBankAccountId is null || receivingTail is null)
        {
            return ShopBankMatchStatuses.MissingReceivingBank;
        }

        var settlementTail = ExtractLast4Digits(settlementAccountNumber);
        if (settlementTail is null)
        {
            return ShopBankMatchStatuses.MissingSettlement;
        }

        return string.Equals(settlementTail, receivingTail, StringComparison.Ordinal)
            ? ShopBankMatchStatuses.Matched
            : ShopBankMatchStatuses.Mismatched;
    }

    /// <summary>
    /// TikTok mask stored in payout notes as <c>card ********4529</c>.
    /// </summary>
    public static string? ExtractMaskFromPayoutNote(string? note)
    {
        if (string.IsNullOrWhiteSpace(note))
        {
            return null;
        }

        const string marker = "card ";
        var idx = note.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
        {
            return null;
        }

        var start = idx + marker.Length;
        var end = start;
        while (end < note.Length && (char.IsDigit(note[end]) || note[end] == '*'))
        {
            end++;
        }

        return end > start ? note[start..end] : null;
    }

    /// <summary>
    /// True when a TTS payout note's card tail does not match the shop receiving-bank account.
    /// Missing mask counts as mismatch.
    /// </summary>
    public static bool IsPayoutNoteMismatched(string? note, string? accountNumber) =>
        !MatchesAccountLast4(ExtractMaskFromPayoutNote(note), accountNumber);
}
