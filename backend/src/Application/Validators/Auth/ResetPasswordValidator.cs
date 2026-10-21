using Application.DTOs.Auth;
using FluentValidation;

namespace Application.Validators.Auth;

/// <summary>
/// Validator for admin reset password requests.
/// </summary>
public class ResetPasswordValidator : AbstractValidator<ResetPasswordDto>
{
    /// <summary>
    /// Initializes validation rules.
    /// </summary>
    public ResetPasswordValidator()
    {
        RuleFor(x => x.NewPassword).ApplyPasswordRules("Mật khẩu mới");
    }
}
