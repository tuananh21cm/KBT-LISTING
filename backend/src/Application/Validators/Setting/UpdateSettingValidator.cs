using Application.DTOs.Setting;
using FluentValidation;
using Shared.Constants;

namespace Application.Validators.Setting;

/// <summary>
/// Validator for update setting requests.
/// </summary>
public class UpdateSettingValidator : AbstractValidator<UpdateSettingDto>
{
    /// <summary>
    /// Initializes validation rules.
    /// </summary>
    public UpdateSettingValidator()
    {
        RuleFor(x => x.Group)
            .NotEmpty().WithMessage("Nhóm cấu hình không được để trống.")
            .MaximumLength(100);

        RuleFor(x => x.Key)
            .NotEmpty().WithMessage("Mã cấu hình (key) không được để trống.")
            .MaximumLength(100)
            .Matches("^[A-Za-z0-9_.-]+$")
            .WithMessage("Key chỉ được chứa chữ, số, dấu chấm, gạch ngang và gạch dưới.");

        RuleFor(x => x.Label)
            .NotEmpty().WithMessage("Tên hiển thị không được để trống.")
            .MaximumLength(200);

        RuleFor(x => x.Value).MaximumLength(2000);

        RuleFor(x => x.Type)
            .NotEmpty()
            .Must(t => SettingTypes.All.Contains(t.Trim().ToLowerInvariant()))
            .WithMessage("Loại giá trị không hợp lệ.");

        RuleFor(x => x.Description).MaximumLength(500);
    }
}
