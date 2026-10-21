using Application.DTOs.Shop;
using FluentValidation;
using Shared.Constants;

namespace Application.Validators.Shop;

/// <summary>
/// Validator for shop catalog-profile updates (at least one category; ranges optional).
/// </summary>
public class UpdateShopCatalogProfileValidator : AbstractValidator<UpdateShopCatalogProfileDto>
{
    /// <summary>Max operational categories per shop.</summary>
    public const int MaxCategories = 20;

    /// <summary>
    /// Initializes validation rules.
    /// </summary>
    public UpdateShopCatalogProfileValidator()
    {
        RuleFor(x => x)
            .Must(x => (x.Categories != null && x.Categories.Count > 0)
                       || !string.IsNullOrWhiteSpace(x.CategoryId))
            .WithMessage("Chọn ít nhất một category cho shop.");

        RuleFor(x => x.Categories)
            .Must(c => c == null || c.Count <= MaxCategories)
            .WithMessage($"Tối đa {MaxCategories} category mỗi shop.");

        RuleForEach(x => x.Categories)
            .ChildRules(item =>
            {
                item.RuleFor(i => i.Id)
                    .NotEmpty().WithMessage("Category thiếu id.")
                    .MaximumLength(100);
                item.RuleFor(i => i.Name).MaximumLength(500);
            });

        RuleFor(x => x.CategoryId).MaximumLength(100);
        RuleFor(x => x.CategoryName).MaximumLength(2000);

        RuleFor(x => x.AgeMin)
            .InclusiveBetween(0, 100)
            .When(x => x.AgeMin.HasValue)
            .WithMessage("Tuổi tối thiểu từ 0 đến 100.");

        RuleFor(x => x.AgeMax)
            .InclusiveBetween(0, 100)
            .When(x => x.AgeMax.HasValue)
            .WithMessage("Tuổi tối đa từ 0 đến 100.");

        RuleFor(x => x)
            .Must(x => !x.AgeMin.HasValue || !x.AgeMax.HasValue || x.AgeMin <= x.AgeMax)
            .WithMessage("Tuổi tối thiểu không được lớn hơn tuổi tối đa.");

        RuleFor(x => x.PriceMinUsd)
            .GreaterThanOrEqualTo(0)
            .When(x => x.PriceMinUsd.HasValue)
            .WithMessage("Giá tối thiểu phải ≥ 0.");

        RuleFor(x => x.PriceMaxUsd)
            .GreaterThanOrEqualTo(0)
            .When(x => x.PriceMaxUsd.HasValue)
            .WithMessage("Giá tối đa phải ≥ 0.");

        RuleFor(x => x)
            .Must(x => !x.PriceMinUsd.HasValue || !x.PriceMaxUsd.HasValue || x.PriceMinUsd <= x.PriceMaxUsd)
            .WithMessage("Giá tối thiểu không được lớn hơn giá tối đa.");

        RuleFor(x => x.MinRating)
            .InclusiveBetween(0, 5)
            .WithMessage("Rating tối thiểu từ 0 đến 5. 0 = không lọc.");

        RuleFor(x => x.MinSoldCount)
            .GreaterThanOrEqualTo(0)
            .WithMessage("Sold tối thiểu phải ≥ 0. 0 = không lọc.");

        RuleFor(x => x.MaxVariantCount)
            .InclusiveBetween(0, 500)
            .WithMessage("Variants tối đa từ 0 đến 500. 0 = không lọc.");

        RuleFor(x => x.AutoPublishMinDraftAgeMinutes)
            .GreaterThanOrEqualTo(ListingAutoPublishSettingKeys.MinDraftAgeMinutes)
            .When(x => x.AutoPublishMinDraftAgeMinutes.HasValue)
            .WithMessage($"Thời gian chờ draft phải ≥ {ListingAutoPublishSettingKeys.MinDraftAgeMinutes} phút.");

        RuleFor(x => x.BodySizeCode)
            .MaximumLength(32)
            .Must(code => string.IsNullOrWhiteSpace(code) || ShopBodySizeCodes.IsValid(code))
            .WithMessage("Body size không hợp lệ. Chọn regular, plus, midsize, curvy, petite hoặc tall.");

        RuleFor(x => x.StyleCodes)
            .Must(codes => codes == null || codes.Count <= ShopStyleCodes.MaxPerShop)
            .WithMessage($"Tối đa {ShopStyleCodes.MaxPerShop} style mỗi shop.");

        RuleFor(x => x.StyleCodes)
            .Must(codes =>
            {
                if (codes == null || codes.Count == 0)
                {
                    return true;
                }

                var canonical = codes
                    .Where(c => ShopStyleCodes.IsValid(c))
                    .Select(c => ShopStyleCodes.Canonical(c)!)
                    .ToList();
                return canonical
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count() == canonical.Count;
            })
            .WithMessage($"Style bị trùng. Chọn {ShopStyleCodes.AllowedList}.");
    }
}
