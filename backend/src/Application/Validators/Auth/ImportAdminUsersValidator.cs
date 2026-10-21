using Application.DTOs.Auth;
using FluentValidation;

namespace Application.Validators.Auth;

/// <summary>
/// Validator cho payload import tài khoản hàng loạt. Chỉ kiểm tra bao ngoài —
/// từng dòng được <see cref="ImportAdminUserItemValidator"/> kiểm riêng trong service
/// để một dòng hỏng không chặn cả lô.
/// </summary>
public class ImportAdminUsersValidator : AbstractValidator<ImportAdminUsersDto>
{
    /// <summary>Số dòng tối đa cho một lần import.</summary>
    public const int MaxItems = 500;

    /// <summary>
    /// Initializes validation rules.
    /// </summary>
    public ImportAdminUsersValidator()
    {
        RuleFor(x => x.Items)
            .NotEmpty().WithMessage("Danh sách tài khoản import không được để trống.")
            .Must(items => items.Count <= MaxItems)
            .WithMessage($"Mỗi lần import tối đa {MaxItems} tài khoản.");
    }
}

/// <summary>
/// Validator cho một dòng tài khoản trong file import.
/// </summary>
public class ImportAdminUserItemValidator : AbstractValidator<ImportAdminUserItemDto>
{
    /// <summary>
    /// Initializes validation rules.
    /// </summary>
    public ImportAdminUserItemValidator()
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
    }
}
