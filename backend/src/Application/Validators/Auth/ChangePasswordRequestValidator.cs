using Application.DTOs.Auth;
using FluentValidation;

namespace Application.Validators.Auth;

/// <summary>
/// Validator for change password requests.
/// </summary>
public class ChangePasswordRequestValidator : AbstractValidator<ChangePasswordRequestDto>
{
    /// <summary>
    /// Initializes validation rules.
    /// </summary>
    public ChangePasswordRequestValidator()
    {
        RuleFor(x => x.CurrentPassword)
            .NotEmpty().WithMessage("Mật khẩu hiện tại không được để trống.");

        RuleFor(x => x.NewPassword).ApplyPasswordRules("Mật khẩu mới");

        RuleFor(x => x.ConfirmPassword)
            .Equal(x => x.NewPassword).WithMessage("Xác nhận mật khẩu không khớp.");
    }
}
