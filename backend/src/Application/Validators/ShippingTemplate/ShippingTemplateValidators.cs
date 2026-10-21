using Application.DTOs.ShippingTemplate;
using Application.Services;
using FluentValidation;

namespace Application.Validators.ShippingTemplate;

/// <summary>
/// Validator for create shipping template requests.
/// </summary>
public class CreateShippingTemplateValidator : AbstractValidator<CreateShippingTemplateDto>
{
    /// <summary>Initializes validation rules.</summary>
    public CreateShippingTemplateValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Tên template không được để trống.")
            .MaximumLength(100);

        RuleFor(x => x.PackageWeight).GreaterThan(0).WithMessage("Weight phải lớn hơn 0.");
        RuleFor(x => x.PackageLength).GreaterThan(0).WithMessage("Length phải lớn hơn 0.");
        RuleFor(x => x.PackageWidth).GreaterThan(0).WithMessage("Width phải lớn hơn 0.");
        RuleFor(x => x.PackageHeight).GreaterThan(0).WithMessage("Height phải lớn hơn 0.");

        RuleFor(x => x.WeightUnit)
            .NotEmpty()
            .Must(ShippingTemplateService.IsValidWeightUnit)
            .WithMessage("WeightUnit phải là lb hoặc kg.");

        RuleFor(x => x.DimensionUnit)
            .NotEmpty()
            .Must(ShippingTemplateService.IsValidDimensionUnit)
            .WithMessage("DimensionUnit phải là inch hoặc cm.");
    }
}

/// <summary>
/// Validator for update shipping template requests.
/// </summary>
public class UpdateShippingTemplateValidator : AbstractValidator<UpdateShippingTemplateDto>
{
    /// <summary>Initializes validation rules.</summary>
    public UpdateShippingTemplateValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Tên template không được để trống.")
            .MaximumLength(100);

        RuleFor(x => x.PackageWeight).GreaterThan(0).WithMessage("Weight phải lớn hơn 0.");
        RuleFor(x => x.PackageLength).GreaterThan(0).WithMessage("Length phải lớn hơn 0.");
        RuleFor(x => x.PackageWidth).GreaterThan(0).WithMessage("Width phải lớn hơn 0.");
        RuleFor(x => x.PackageHeight).GreaterThan(0).WithMessage("Height phải lớn hơn 0.");

        RuleFor(x => x.WeightUnit)
            .NotEmpty()
            .Must(ShippingTemplateService.IsValidWeightUnit)
            .WithMessage("WeightUnit phải là lb hoặc kg.");

        RuleFor(x => x.DimensionUnit)
            .NotEmpty()
            .Must(ShippingTemplateService.IsValidDimensionUnit)
            .WithMessage("DimensionUnit phải là inch hoặc cm.");
    }
}
