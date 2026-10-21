using Application.DTOs.Shop;
using FluentValidation;

namespace Application.Validators.Shop;

/// <summary>
/// Validator for TikTok payout sync from the browser extension.
/// </summary>
public class SyncTikTokPayoutsValidator : AbstractValidator<SyncTikTokPayoutsDto>
{
    /// <summary>
    /// Initializes validation rules.
    /// </summary>
    public SyncTikTokPayoutsValidator()
    {
        RuleFor(x => x)
            .Must(x => !string.IsNullOrWhiteSpace(ResolveExternalShopId(x)))
            .WithMessage("externalShopId / sellerId không được để trống.");

        RuleFor(x => x.Payments)
            .NotNull()
            .Must(p => p.Count <= 200)
            .WithMessage("Tối đa 200 payments mỗi lần sync.");

        RuleForEach(x => x.Payments).ChildRules(payment =>
        {
            payment.RuleFor(p => p.PaymentId)
                .NotEmpty().WithMessage("paymentId không được để trống.")
                .MaximumLength(64);
            payment.RuleFor(p => p.Amount).GreaterThanOrEqualTo(0);
            payment.RuleFor(p => p.Currency).MaximumLength(10).When(p => !string.IsNullOrEmpty(p.Currency));
            payment.RuleFor(p => p.BankCardMask).MaximumLength(50).When(p => !string.IsNullOrEmpty(p.BankCardMask));
            payment.RuleFor(p => p.Note).MaximumLength(500).When(p => !string.IsNullOrEmpty(p.Note));
            payment.RuleForEach(p => p.Orders).ChildRules(order =>
            {
                order.RuleFor(o => o.TradeOrderId)
                    .NotEmpty().WithMessage("tradeOrderId không được để trống.")
                    .MaximumLength(100);
            });
        });
    }

    private static string ResolveExternalShopId(SyncTikTokPayoutsDto dto)
    {
        if (!string.IsNullOrWhiteSpace(dto.ExternalShopId))
        {
            return dto.ExternalShopId.Trim();
        }

        return (dto.SellerId ?? string.Empty).Trim();
    }
}
