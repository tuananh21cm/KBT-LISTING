using Application.DTOs.Auth;
using FluentValidation;
using Shared.Constants;

namespace Application.Validators.Auth;

/// <summary>
/// Validator for updating the current user's UI theme.
/// </summary>
public class UpdateThemeValidator : AbstractValidator<UpdateThemeDto>
{
    /// <summary>
    /// Initializes validation rules.
    /// </summary>
    public UpdateThemeValidator()
    {
        RuleFor(x => x.Theme)
            .NotEmpty()
            .Must(t => UiThemeModes.Allowed.Contains(t))
            .WithMessage("Theme phải là 'light' hoặc 'dark'.");
    }
}
