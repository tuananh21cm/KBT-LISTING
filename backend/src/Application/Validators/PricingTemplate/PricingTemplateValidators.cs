using Application.DTOs.PricingTemplate;
using Application.Services;
using FluentValidation;

namespace Application.Validators.PricingTemplate;

/// <summary>
/// Validator for create pricing template requests.
/// </summary>
public class CreatePricingTemplateValidator : AbstractValidator<CreatePricingTemplateDto>
{
    /// <summary>Initializes validation rules.</summary>
    public CreatePricingTemplateValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Tên template không được để trống.")
            .MaximumLength(100);

        RuleFor(x => x.SupplierShipping).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Tax).GreaterThanOrEqualTo(0);
        RuleFor(x => x.DiscountPercent).InclusiveBetween(0, 99.99m)
            .WithMessage("Discount phải từ 0 đến 99.99%.");
        RuleFor(x => x.FastSalePercent).InclusiveBetween(0, 99.99m)
            .WithMessage("Fast Sale phải từ 0 đến 99.99%.");
        RuleFor(x => x.AdsFee).GreaterThanOrEqualTo(0);
        RuleFor(x => x.TikTokShipping).GreaterThanOrEqualTo(0);
        RuleFor(x => x.PlatformFeePercent).InclusiveBetween(0, 99.99m)
            .WithMessage("Phí sàn phải từ 0 đến 99.99%.");

        RuleFor(x => x.TargetMode)
            .NotEmpty()
            .Must(PricingTemplateService.IsValidTargetMode)
            .WithMessage("TargetMode phải là margin hoặc profit.");

        RuleFor(x => x.MarginPercent).GreaterThanOrEqualTo(0)
            .When(x => PricingTemplateService.CanonicalizeTargetMode(x.TargetMode) == "margin");
        RuleFor(x => x.ProfitAmount).GreaterThanOrEqualTo(0)
            .When(x => PricingTemplateService.CanonicalizeTargetMode(x.TargetMode) == "profit");
    }
}

/// <summary>
/// Validator for update pricing template requests.
/// </summary>
public class UpdatePricingTemplateValidator : AbstractValidator<UpdatePricingTemplateDto>
{
    /// <summary>Initializes validation rules.</summary>
    public UpdatePricingTemplateValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Tên template không được để trống.")
            .MaximumLength(100);

        RuleFor(x => x.SupplierShipping).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Tax).GreaterThanOrEqualTo(0);
        RuleFor(x => x.DiscountPercent).InclusiveBetween(0, 99.99m)
            .WithMessage("Discount phải từ 0 đến 99.99%.");
        RuleFor(x => x.FastSalePercent).InclusiveBetween(0, 99.99m)
            .WithMessage("Fast Sale phải từ 0 đến 99.99%.");
        RuleFor(x => x.AdsFee).GreaterThanOrEqualTo(0);
        RuleFor(x => x.TikTokShipping).GreaterThanOrEqualTo(0);
        RuleFor(x => x.PlatformFeePercent).InclusiveBetween(0, 99.99m)
            .WithMessage("Phí sàn phải từ 0 đến 99.99%.");

        RuleFor(x => x.TargetMode)
            .NotEmpty()
            .Must(PricingTemplateService.IsValidTargetMode)
            .WithMessage("TargetMode phải là margin hoặc profit.");

        RuleFor(x => x.MarginPercent).GreaterThanOrEqualTo(0)
            .When(x => PricingTemplateService.CanonicalizeTargetMode(x.TargetMode) == "margin");
        RuleFor(x => x.ProfitAmount).GreaterThanOrEqualTo(0)
            .When(x => PricingTemplateService.CanonicalizeTargetMode(x.TargetMode) == "profit");
    }
}
