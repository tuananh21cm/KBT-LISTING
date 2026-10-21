using Application.DTOs.Role;
using FluentValidation;

namespace Application.Validators.Role;

/// <summary>
/// Validator for create role requests.
/// </summary>
public class CreateRoleValidator : AbstractValidator<CreateRoleDto>
{
    /// <summary>
    /// Initializes validation rules.
    /// </summary>
    public CreateRoleValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Tên vai trò không được để trống.")
            .MaximumLength(100);

        RuleFor(x => x.Description).MaximumLength(500);
    }
}
