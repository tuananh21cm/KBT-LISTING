using Application.DTOs.Shop;
using FluentValidation;
using Shared.Constants;

namespace Application.Validators.Shop;

/// <summary>
/// Validator for TikTok profile sync from the browser extension.
/// </summary>
public class SyncTikTokShopProfileValidator : AbstractValidator<SyncTikTokShopProfileDto>
{
    /// <summary>
    /// Initializes validation rules.
    /// </summary>
    public SyncTikTokShopProfileValidator()
    {
        RuleFor(x => x.SellerId)
            .NotEmpty().WithMessage("sellerId không được để trống.")
            .MaximumLength(100);

        RuleFor(x => x.ShopName).MaximumLength(200).When(x => !string.IsNullOrEmpty(x.ShopName));
        RuleFor(x => x.ShopCode).MaximumLength(64).When(x => !string.IsNullOrEmpty(x.ShopCode));
        RuleFor(x => x.Region).MaximumLength(10).When(x => !string.IsNullOrEmpty(x.Region));
        RuleFor(x => x.CompanyName).MaximumLength(200).When(x => !string.IsNullOrEmpty(x.CompanyName));
        RuleFor(x => x.LogoUrl).MaximumLength(2000).When(x => !string.IsNullOrEmpty(x.LogoUrl));
        RuleFor(x => x.Timezone).MaximumLength(100).When(x => !string.IsNullOrEmpty(x.Timezone));
        RuleFor(x => x.CompanyCertNum).MaximumLength(100).When(x => !string.IsNullOrEmpty(x.CompanyCertNum));
        RuleFor(x => x.AccountName).MaximumLength(200).When(x => !string.IsNullOrEmpty(x.AccountName));
        RuleFor(x => x.AccountEmail).MaximumLength(200).When(x => !string.IsNullOrEmpty(x.AccountEmail));
        RuleFor(x => x.AccountMobile).MaximumLength(50).When(x => !string.IsNullOrEmpty(x.AccountMobile));
        RuleFor(x => x.BankAccountNumber).MaximumLength(100).When(x => !string.IsNullOrEmpty(x.BankAccountNumber));
        RuleFor(x => x.SettlementBankAccountNumber).MaximumLength(100).When(x => !string.IsNullOrEmpty(x.SettlementBankAccountNumber));
        RuleFor(x => x.SettlementPaymentMethod).MaximumLength(100).When(x => !string.IsNullOrEmpty(x.SettlementPaymentMethod));
        RuleFor(x => x.SettlementBeneficiaryName).MaximumLength(200).When(x => !string.IsNullOrEmpty(x.SettlementBeneficiaryName));
        RuleFor(x => x.SettlementAccountType).MaximumLength(50).When(x => !string.IsNullOrEmpty(x.SettlementAccountType));
        RuleFor(x => x.ShopType)
            .Must(s => string.IsNullOrEmpty(s) || ShopTypes.All.Contains(NormalizeShopType(s!)))
            .WithMessage("Loại shop không hợp lệ (Per / Llc).");

        RuleFor(x => x.OnHoldAmount).GreaterThanOrEqualTo(0).When(x => x.OnHoldAmount.HasValue);
        RuleFor(x => x.OnHoldInTransitAmount).GreaterThanOrEqualTo(0).When(x => x.OnHoldInTransitAmount.HasValue);
        RuleFor(x => x.OnHoldAwaitingSettlementAmount).GreaterThanOrEqualTo(0).When(x => x.OnHoldAwaitingSettlementAmount.HasValue);
        RuleFor(x => x.OnHoldReserveAmount).GreaterThanOrEqualTo(0).When(x => x.OnHoldReserveAmount.HasValue);
        RuleFor(x => x.Deli).GreaterThanOrEqualTo(0).When(x => x.Deli.HasValue);
    }

    private static string NormalizeShopType(string value)
    {
        var trimmed = value.Trim();
        if (string.Equals(trimmed, "Per", StringComparison.OrdinalIgnoreCase))
        {
            return ShopTypes.Per;
        }

        if (string.Equals(trimmed, "Llc", StringComparison.OrdinalIgnoreCase))
        {
            return ShopTypes.Llc;
        }

        return trimmed;
    }
}
