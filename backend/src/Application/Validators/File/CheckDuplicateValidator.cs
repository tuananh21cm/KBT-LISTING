using Application.DTOs.File;
using FluentValidation;

namespace Application.Validators.File;

/// <summary>
/// Validator for check duplicate requests.
/// </summary>
public class CheckDuplicateValidator : AbstractValidator<CheckDuplicateDto>
{
    /// <summary>
    /// Initializes validation rules.
    /// </summary>
    public CheckDuplicateValidator()
    {
        RuleFor(x => x.FileHash)
            .NotEmpty().WithMessage("File hash không được để trống.")
            .Length(64).WithMessage("File hash phải là SHA256 hex (64 ký tự).")
            .Matches("^[a-fA-F0-9]+$").WithMessage("File hash phải là hex.");
    }
}
