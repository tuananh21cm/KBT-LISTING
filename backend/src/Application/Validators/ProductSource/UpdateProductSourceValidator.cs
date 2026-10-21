using Application.DTOs.ProductSource;
using FluentValidation;

namespace Application.Validators.ProductSource;

/// <summary>
/// Validator for update product source requests.
/// </summary>
public class UpdateProductSourceValidator : AbstractValidator<UpdateProductSourceDto>
{
    /// <summary>
    /// Initializes validation rules.
    /// </summary>
    public UpdateProductSourceValidator()
    {
        RuleFor(x => x.Code)
            .NotEmpty().WithMessage("Mã nguồn hàng không được để trống.")
            .MaximumLength(50)
            .Matches("^[A-Za-z0-9_-]+$")
            .WithMessage("Mã nguồn hàng chỉ được chứa chữ, số, gạch ngang và gạch dưới.");

        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Tên nguồn hàng không được để trống.")
            .MaximumLength(200);

        RuleFor(x => x.LogoUrl).MaximumLength(1000);
        RuleFor(x => x.Description).MaximumLength(500);
    }
}
