using Application.DTOs.Auth;
using FluentValidation;

namespace Application.Validators.Auth;

/// <summary>
/// Validator for Fulfil PIN login requests.
/// </summary>
public class PinLoginRequestValidator : AbstractValidator<PinLoginRequestDto>
{
    /// <summary>
    /// Initializes validation rules.
    /// </summary>
    public PinLoginRequestValidator()
    {
        RuleFor(x => x.Pin)
            .NotEmpty().WithMessage("Mã PIN không được để trống.")
            .Matches(@"^\d{4}$").WithMessage("Mã PIN phải gồm đúng 4 chữ số.");
    }
}
