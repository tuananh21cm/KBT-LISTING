using Application.DTOs.Role;
using FluentValidation;

namespace Application.Validators.Role;

/// <summary>
/// Validator for update role requests.
/// </summary>
public class UpdateRoleValidator : AbstractValidator<UpdateRoleDto>
{
    /// <summary>
    /// Initializes validation rules.
    /// </summary>
    public UpdateRoleValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Tên vai trò không được để trống.")
            .MaximumLength(100);

        RuleFor(x => x.Description).MaximumLength(500);
    }
}
