using Application.DTOs.Platform;
using FluentValidation;

namespace Application.Validators.Platform;

/// <summary>
/// Validator for create platform requests.
/// </summary>
public class CreatePlatformValidator : AbstractValidator<CreatePlatformDto>
{
    /// <summary>
    /// Initializes validation rules.
    /// </summary>
    public CreatePlatformValidator()
    {
        RuleFor(x => x.Code)
            .NotEmpty().WithMessage("Mã nền tảng không được để trống.")
            .MaximumLength(50)
            .Matches("^[A-Za-z0-9_-]+$")
            .WithMessage("Mã nền tảng chỉ được chứa chữ, số, gạch ngang và gạch dưới.");

        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Tên nền tảng không được để trống.")
            .MaximumLength(200);

        RuleFor(x => x.Description).MaximumLength(500);

        RuleFor(x => x.AvatarUrl).MaximumLength(1000);
    }
}
