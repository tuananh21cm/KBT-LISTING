using Application.DTOs.ProductOpportunity;
using FluentValidation;

namespace Application.Validators.ProductOpportunity;

/// <summary>
/// Validator for Product opportunity snapshot sync from the browser extension.
/// </summary>
public class SyncProductOpportunitiesValidator : AbstractValidator<SyncProductOpportunitiesDto>
{
    /// <summary>Maximum leads accepted in one POST (full snapshot).</summary>
    public const int MaxItems = 500;

    /// <summary>Initializes validation rules.</summary>
    public SyncProductOpportunitiesValidator()
    {
        RuleFor(x => x)
            .Must(x => !string.IsNullOrWhiteSpace(ResolveExternalShopId(x)))
            .WithMessage("externalShopId / sellerId không được để trống.");

        RuleFor(x => x.Items)
            .NotNull()
            .Must(p => p.Count <= MaxItems)
            .WithMessage($"Tối đa {MaxItems} keywords mỗi lần sync.");

        RuleForEach(x => x.Items).ChildRules(item =>
        {
            item.RuleFor(p => p.LeadId)
                .NotEmpty().WithMessage("leadId không được để trống.")
                .MaximumLength(64);
            item.RuleFor(p => p.LeadName).MaximumLength(500);
            item.RuleFor(p => p.PicUrl).MaximumLength(2000).When(p => !string.IsNullOrEmpty(p.PicUrl));
            item.RuleFor(p => p.PicUrls)
                .Must(l => l == null || l.Count <= 8)
                .WithMessage("Tối đa 8 ảnh mỗi keyword.");
            item.RuleForEach(p => p.PicUrls)
                .MaximumLength(2000)
                .When(p => p.PicUrls is { Count: > 0 });
            item.RuleFor(p => p.OnlineProductsRaw).MaximumLength(32).When(p => !string.IsNullOrEmpty(p.OnlineProductsRaw));
            item.RuleFor(p => p.SearchVolumeRingRatio).MaximumLength(32).When(p => !string.IsNullOrEmpty(p.SearchVolumeRingRatio));
            item.RuleFor(p => p.OnlineProductsRingRatio).MaximumLength(32).When(p => !string.IsNullOrEmpty(p.OnlineProductsRingRatio));
            item.RuleFor(p => p.OrderRaw).MaximumLength(32).When(p => !string.IsNullOrEmpty(p.OrderRaw));
            item.RuleFor(p => p.OrderRingRatio).MaximumLength(32).When(p => !string.IsNullOrEmpty(p.OrderRingRatio));
            item.RuleFor(p => p.GmvRaw).MaximumLength(32).When(p => !string.IsNullOrEmpty(p.GmvRaw));
            item.RuleFor(p => p.GmvRingRatio).MaximumLength(32).When(p => !string.IsNullOrEmpty(p.GmvRingRatio));
            item.RuleFor(p => p.Categories)
                .Must(l => l == null || l.Count <= 20)
                .WithMessage("Tối đa 20 danh mục mỗi keyword.");
            item.RuleForEach(p => p.Categories).ChildRules(cat =>
            {
                cat.RuleFor(c => c.CateId).MaximumLength(32);
                cat.RuleFor(c => c.CateName).MaximumLength(200);
            }).When(p => p.Categories is { Count: > 0 });
            item.RuleFor(p => p.Level1CateId).MaximumLength(32).When(p => !string.IsNullOrEmpty(p.Level1CateId));
            item.RuleFor(p => p.Level1CateName).MaximumLength(200).When(p => !string.IsNullOrEmpty(p.Level1CateName));
            item.RuleFor(p => p.Level2CateId).MaximumLength(32).When(p => !string.IsNullOrEmpty(p.Level2CateId));
            item.RuleFor(p => p.Level2CateName).MaximumLength(200).When(p => !string.IsNullOrEmpty(p.Level2CateName));
            item.RuleFor(p => p.Level3CateId).MaximumLength(32).When(p => !string.IsNullOrEmpty(p.Level3CateId));
            item.RuleFor(p => p.Level3CateName).MaximumLength(200).When(p => !string.IsNullOrEmpty(p.Level3CateName));
            item.RuleFor(p => p.LeadTagTexts)
                .Must(l => l == null || l.Count <= 8)
                .WithMessage("Tối đa 8 nhãn lead_tag_text mỗi keyword.");
            item.RuleForEach(p => p.LeadTagTexts)
                .MaximumLength(64)
                .When(p => p.LeadTagTexts is { Count: > 0 });
            item.RuleFor(p => p.LeadSourceTag).MaximumLength(64).When(p => !string.IsNullOrEmpty(p.LeadSourceTag));
            item.RuleFor(p => p.LeadClusterSourceTag).MaximumLength(64).When(p => !string.IsNullOrEmpty(p.LeadClusterSourceTag));
        });
    }

    /// <summary>Resolves TikTok seller id from either field.</summary>
    public static string ResolveExternalShopId(SyncProductOpportunitiesDto dto)
    {
        if (!string.IsNullOrWhiteSpace(dto.ExternalShopId))
        {
            return dto.ExternalShopId.Trim();
        }

        return (dto.SellerId ?? string.Empty).Trim();
    }
}
