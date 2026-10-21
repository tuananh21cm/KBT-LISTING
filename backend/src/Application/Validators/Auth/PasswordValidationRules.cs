using FluentValidation;

namespace Application.Validators.Auth;

/// <summary>
/// Shared FluentValidation rules aligned with ASP.NET Identity password options.
/// </summary>
public static class PasswordValidationRules
{
    /// <summary>
    /// Applies standard password complexity rules.
    /// </summary>
    public static IRuleBuilderOptions<T, string> ApplyPasswordRules<T>(
        this IRuleBuilder<T, string> ruleBuilder,
        string fieldLabel = "Mật khẩu")
    {
        return ruleBuilder
            .NotEmpty().WithMessage($"{fieldLabel} không được để trống.")
            .MinimumLength(8).WithMessage($"{fieldLabel} phải có ít nhất 8 ký tự.")
            .Matches("[A-Z]").WithMessage($"{fieldLabel} phải có ít nhất một chữ hoa.")
            .Matches("[a-z]").WithMessage($"{fieldLabel} phải có ít nhất một chữ thường.")
            .Matches("[0-9]").WithMessage($"{fieldLabel} phải có ít nhất một chữ số.");
    }
}
