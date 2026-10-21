using Application.DTOs.VariationSpecTemplate;
using Application.Services;
using FluentValidation;

namespace Application.Validators.VariationSpecTemplate;

/// <summary>
/// Validator for create variation spec template requests.
/// </summary>
public class CreateVariationSpecTemplateValidator : AbstractValidator<CreateVariationSpecTemplateDto>
{
    /// <summary>Initializes validation rules.</summary>
    public CreateVariationSpecTemplateValidator()
    {
        RuleFor(x => x.SpecType)
            .NotEmpty().WithMessage("SpecType không được để trống.")
            .Must(t => VariationSpecTemplateService.AllowedSpecTypes.Contains(t.Trim()))
            .WithMessage("SpecType phải là Color, Size hoặc Number of Items.");

        RuleFor(x => x.Value)
            .NotEmpty().WithMessage("Giá trị không được để trống.")
            .MaximumLength(100);

        RuleFor(x => x.SortOrder)
            .GreaterThanOrEqualTo(0)
            .When(x => x.SortOrder.HasValue);
    }
}

/// <summary>
/// Validator for update variation spec template requests.
/// </summary>
public class UpdateVariationSpecTemplateValidator : AbstractValidator<UpdateVariationSpecTemplateDto>
{
    /// <summary>Initializes validation rules.</summary>
    public UpdateVariationSpecTemplateValidator()
    {
        RuleFor(x => x.Value)
            .NotEmpty().WithMessage("Giá trị không được để trống.")
            .MaximumLength(100);

        RuleFor(x => x.SortOrder).GreaterThanOrEqualTo(0);
    }
}

/// <summary>
/// Validator for reorder requests.
/// </summary>
public class ReorderVariationSpecTemplatesValidator : AbstractValidator<ReorderVariationSpecTemplatesDto>
{
    /// <summary>Initializes validation rules.</summary>
    public ReorderVariationSpecTemplatesValidator()
    {
        RuleFor(x => x.SpecType)
            .NotEmpty()
            .Must(t => VariationSpecTemplateService.AllowedSpecTypes.Contains(t.Trim()));

        RuleFor(x => x.OrderedIds)
            .NotEmpty().WithMessage("Danh sách sắp xếp không được để trống.");
    }
}
