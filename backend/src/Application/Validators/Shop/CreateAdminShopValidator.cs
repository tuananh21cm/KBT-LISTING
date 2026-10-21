using Application.DTOs.Shop;
using FluentValidation;

namespace Application.Validators.Shop;

/// <summary>
/// Validator for admin create shop requests.
/// </summary>
public class CreateAdminShopValidator : AbstractValidator<CreateAdminShopDto>
{
    /// <summary>
    /// Initializes validation rules.
    /// </summary>
    public CreateAdminShopValidator()
    {
        Include(new CreateShopValidator());

        RuleFor(x => x.OwnerId)
            .NotEmpty().WithMessage("Seller không được để trống.");
    }
}
