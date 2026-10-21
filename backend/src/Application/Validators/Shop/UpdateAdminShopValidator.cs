using Application.DTOs.Shop;
using FluentValidation;

namespace Application.Validators.Shop;

/// <summary>
/// Validator for admin update shop requests.
/// </summary>
public class UpdateAdminShopValidator : AbstractValidator<UpdateAdminShopDto>
{
    /// <summary>
    /// Initializes validation rules.
    /// </summary>
    public UpdateAdminShopValidator()
    {
        Include(new UpdateShopValidator());
    }
}
