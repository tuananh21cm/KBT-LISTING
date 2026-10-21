using Application.DTOs.Shop;
using Application.Services;
using FluentValidation;

namespace Application.Validators.Shop;

/// <summary>Validator cho ingest điểm Health Center từ extension.</summary>
public class SyncTikTokShopScoresValidator : AbstractValidator<SyncTikTokShopScoresDto>
{
    private const decimal MaxScore = ShopScoreNormalizer.MaxPerformanceScore;

    /// <summary>Initializes validation rules.</summary>
    public SyncTikTokShopScoresValidator()
    {
        RuleFor(x => x)
            .Must(x => !string.IsNullOrWhiteSpace(ResolveExternalShopId(x)))
            .WithMessage("externalShopId / sellerId không được để trống.");

        RuleFor(x => x)
            .Must(HasAnyScore)
            .WithMessage("Cần ít nhất một điểm để đồng bộ.");

        RuleFor(x => x.AhrScore)
            .InclusiveBetween(0, ShopScoreNormalizer.MaxAhrScore)
            .When(x => x.AhrScore.HasValue)
            .WithMessage($"ahrScore phải trong khoảng 0–{ShopScoreNormalizer.MaxAhrScore}.");

        RuleFor(x => x.PerformanceScore)
            .InclusiveBetween(0m, MaxScore)
            .When(x => x.PerformanceScore.HasValue)
            .WithMessage($"performanceScore phải trong khoảng 0–{MaxScore}.");

        RuleFor(x => x.ProductSatisfactionScore)
            .InclusiveBetween(0m, MaxScore)
            .When(x => x.ProductSatisfactionScore.HasValue)
            .WithMessage($"productSatisfactionScore phải trong khoảng 0–{MaxScore}.");

        RuleFor(x => x.FulfillmentScore)
            .InclusiveBetween(0m, MaxScore)
            .When(x => x.FulfillmentScore.HasValue)
            .WithMessage($"fulfillmentScore phải trong khoảng 0–{MaxScore}.");

        RuleFor(x => x.CustomerServiceScore)
            .InclusiveBetween(0m, MaxScore)
            .When(x => x.CustomerServiceScore.HasValue)
            .WithMessage($"customerServiceScore phải trong khoảng 0–{MaxScore}.");
    }

    /// <summary>Resolves TikTok seller id from either field.</summary>
    public static string ResolveExternalShopId(SyncTikTokShopScoresDto dto)
    {
        if (!string.IsNullOrWhiteSpace(dto.ExternalShopId))
        {
            return dto.ExternalShopId.Trim();
        }

        return (dto.SellerId ?? string.Empty).Trim();
    }

    /// <summary>True khi payload mang ít nhất một điểm.</summary>
    public static bool HasAnyScore(SyncTikTokShopScoresDto dto) =>
        dto.AhrScore.HasValue
        || dto.PerformanceScore.HasValue
        || dto.ProductSatisfactionScore.HasValue
        || dto.FulfillmentScore.HasValue
        || dto.CustomerServiceScore.HasValue;
}
