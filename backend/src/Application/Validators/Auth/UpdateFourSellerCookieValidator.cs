using Application.DTOs.Auth;
using FluentValidation;

namespace Application.Validators.Auth;

/// <summary>
/// Validator for updating the current user's 4Seller cookie.
/// </summary>
public class UpdateFourSellerCookieValidator : AbstractValidator<UpdateFourSellerCookieDto>
{
    /// <summary>
    /// Initializes validation rules.
    /// </summary>
    public UpdateFourSellerCookieValidator()
    {
        RuleFor(x => x.FourSellerCookie)
            .MaximumLength(16384)
            .When(x => !string.IsNullOrEmpty(x.FourSellerCookie));
    }
}
