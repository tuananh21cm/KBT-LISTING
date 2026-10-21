using Application.DTOs.Auth;
using FluentValidation;

namespace Application.Validators.Auth;

/// <summary>
/// Validator for refresh token requests.
/// </summary>
public class RefreshTokenRequestValidator : AbstractValidator<RefreshTokenRequestDto>
{
    /// <summary>
    /// Initializes validation rules.
    /// </summary>
    public RefreshTokenRequestValidator()
    {
        RuleFor(x => x.RefreshToken)
            .NotEmpty().WithMessage("Refresh token không được để trống.");
    }
}
