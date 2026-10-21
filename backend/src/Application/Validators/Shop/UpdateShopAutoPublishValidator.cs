using Application.DTOs.Shop;
using FluentValidation;
using Shared.Constants;

namespace Application.Validators.Shop;

/// <summary>
/// Validator for per-shop auto publish updates.
/// </summary>
public class UpdateShopAutoPublishValidator : AbstractValidator<UpdateShopAutoPublishDto>
{
    /// <summary>
    /// Initializes validation rules.
    /// </summary>
    public UpdateShopAutoPublishValidator()
    {
        RuleFor(x => x.AutoPublishIntervalSeconds)
            .GreaterThanOrEqualTo(1)
            .When(x => x.AutoPublishIntervalSeconds.HasValue)
            .WithMessage("Chu kỳ publish phải ≥ 1 giây.");

        RuleFor(x => x.AutoPublishMinDraftAgeMinutes)
            .GreaterThanOrEqualTo(ListingAutoPublishSettingKeys.MinDraftAgeMinutes)
            .When(x => x.AutoPublishMinDraftAgeMinutes.HasValue)
            .WithMessage($"Thời gian chờ draft phải ≥ {ListingAutoPublishSettingKeys.MinDraftAgeMinutes} phút.");
    }
}
