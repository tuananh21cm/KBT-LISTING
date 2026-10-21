using Application.DTOs.Shop;
using FluentValidation;
using Shared.Constants;

namespace Application.Validators.Shop;

/// <summary>
/// Validator for update shop requests.
/// </summary>
public class UpdateShopValidator : AbstractValidator<UpdateShopDto>
{
    /// <summary>
    /// Initializes validation rules.
    /// </summary>
    public UpdateShopValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Tên shop không được để trống.")
            .MaximumLength(200);

        RuleFor(x => x.ExternalShopId).MaximumLength(100).When(x => !string.IsNullOrEmpty(x.ExternalShopId));

        RuleFor(x => x.FourSellerShopId).MaximumLength(100);
        RuleFor(x => x.AvatarUrl).MaximumLength(2000);
        RuleFor(x => x.ShopUrl).MaximumLength(500);
        RuleFor(x => x.Proxy).MaximumLength(100);
        RuleFor(x => x.ProfileName).MaximumLength(200);
        RuleFor(x => x.Email).MaximumLength(200).EmailAddress().When(x => !string.IsNullOrEmpty(x.Email));
        RuleFor(x => x.TiktokPassword).MaximumLength(200);
        RuleFor(x => x.DayLive).MaximumLength(50);
        RuleFor(x => x.NewPhoneNumber).MaximumLength(50);
        RuleFor(x => x.OldBankNumber).MaximumLength(100);
        RuleFor(x => x.NewBankNumber).MaximumLength(100);
        RuleFor(x => x.PaymentName).MaximumLength(200);
        RuleFor(x => x.ShopType)
            .Must(s => string.IsNullOrEmpty(s) || ShopTypes.All.Contains(s))
            .WithMessage("Loại shop không hợp lệ (Per / Llc).");
        RuleFor(x => x.LlcName).MaximumLength(200);
        RuleFor(x => x.TwoFactorAuth).MaximumLength(500);

        RuleFor(x => x.Status)
            .NotEmpty().WithMessage("Trạng thái không được để trống.")
            .Must(s => ShopStatuses.All.Contains(s))
            .WithMessage("Trạng thái shop không hợp lệ.");
    }
}
