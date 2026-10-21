using Application.DTOs.Listing;
using FluentValidation;

namespace Application.Validators.Listing;

/// <summary>
/// Validator for the TikTok Compass analytics ingest from the browser extension.
/// </summary>
public class SyncListingAnalyticsValidator : AbstractValidator<SyncListingAnalyticsDto>
{
    /// <summary>Maximum products accepted in one POST.</summary>
    public const int MaxItems = 3000;

    /// <summary>Widest Compass range we accept (guards a bogus client payload).</summary>
    public const int MaxRangeDays = 90;

    /// <summary>Initializes validation rules.</summary>
    public SyncListingAnalyticsValidator()
    {
        RuleFor(x => x)
            .Must(x => !string.IsNullOrWhiteSpace(ResolveExternalShopId(x)))
            .WithMessage("externalShopId / sellerId không được để trống.");

        RuleFor(x => x.Preset).MaximumLength(32).When(x => !string.IsNullOrEmpty(x.Preset));

        RuleFor(x => x)
            .Must(x => x.RangeStart is null || x.RangeEnd is null || x.RangeStart <= x.RangeEnd)
            .WithMessage("rangeStart phải ≤ rangeEnd.")
            .Must(x => x.RangeStart is null
                || x.RangeEnd is null
                || x.RangeEnd.Value.DayNumber - x.RangeStart.Value.DayNumber <= MaxRangeDays)
            .WithMessage($"Khoảng thời gian tối đa {MaxRangeDays} ngày.");

        RuleFor(x => x.Items)
            .NotNull()
            .Must(p => p.Count <= MaxItems)
            .WithMessage($"Tối đa {MaxItems} product mỗi lần sync.");

        RuleForEach(x => x.Items).ChildRules(item =>
        {
            item.RuleFor(p => p.ProductId)
                .NotEmpty().WithMessage("productId không được để trống.")
                .MaximumLength(64);
            item.RuleFor(p => p.ProductName).MaximumLength(500).When(p => !string.IsNullOrEmpty(p.ProductName));
            item.RuleFor(p => p.MainImage).MaximumLength(1000).When(p => !string.IsNullOrEmpty(p.MainImage));
            item.RuleFor(p => p.Currency).MaximumLength(8).When(p => !string.IsNullOrEmpty(p.Currency));
            item.RuleFor(p => p.Gmv).GreaterThanOrEqualTo(0).When(p => p.Gmv.HasValue);
            item.RuleFor(p => p.Orders).GreaterThanOrEqualTo(0).When(p => p.Orders.HasValue);
            item.RuleFor(p => p.Impressions).GreaterThanOrEqualTo(0).When(p => p.Impressions.HasValue);
            item.RuleFor(p => p.Clicks).GreaterThanOrEqualTo(0).When(p => p.Clicks.HasValue);
            item.RuleFor(p => p.AddToCartUnits).GreaterThanOrEqualTo(0).When(p => p.AddToCartUnits.HasValue);
            item.RuleFor(p => p.AddToCartBuyers).GreaterThanOrEqualTo(0).When(p => p.AddToCartBuyers.HasValue);
        });
    }

    /// <summary>Resolves TikTok seller id from either field.</summary>
    public static string ResolveExternalShopId(SyncListingAnalyticsDto dto)
    {
        if (!string.IsNullOrWhiteSpace(dto.ExternalShopId))
        {
            return dto.ExternalShopId.Trim();
        }

        return (dto.SellerId ?? string.Empty).Trim();
    }
}
