using Application.DTOs.Auth;
using FluentValidation;

namespace Application.Validators.Auth;

/// <summary>
/// Validator for admin create user requests.
/// </summary>
public class CreateAdminUserValidator : AbstractValidator<CreateAdminUserDto>
{
    /// <summary>
    /// Initializes validation rules.
    /// </summary>
    public CreateAdminUserValidator()
    {
        RuleFor(x => x.UserName)
            .NotEmpty().WithMessage("Tên đăng nhập không được để trống.")
            .MaximumLength(100);

        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email không được để trống.")
            .EmailAddress().WithMessage("Email không hợp lệ.")
            .MaximumLength(256);

        RuleFor(x => x.FullName)
            .NotEmpty().WithMessage("Họ tên không được để trống.")
            .MaximumLength(200);

        RuleFor(x => x.Password).ApplyPasswordRules();

        RuleFor(x => x.Roles)
            .NotEmpty().WithMessage("Phải chọn ít nhất một vai trò.");

        RuleFor(x => x.FourSellerCookie)
            .MaximumLength(16384)
            .When(x => !string.IsNullOrEmpty(x.FourSellerCookie));

        RuleFor(x => x.FourSellerProxy)
            .MaximumLength(300)
            .When(x => !string.IsNullOrEmpty(x.FourSellerProxy));

        RuleFor(x => x.FulfilPin)
            .Matches(@"^\d{4}$").WithMessage("Mã PIN phải gồm đúng 4 chữ số.")
            .When(x => !string.IsNullOrEmpty(x.FulfilPin));
    }
}
