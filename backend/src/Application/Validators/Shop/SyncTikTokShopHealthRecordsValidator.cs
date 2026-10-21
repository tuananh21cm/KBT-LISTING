using Application.DTOs.Shop;
using FluentValidation;
using Shared.Constants;

namespace Application.Validators.Shop;

/// <summary>Validator cho ingest Growth Center records từ extension.</summary>
public class SyncTikTokShopHealthRecordsValidator : AbstractValidator<SyncTikTokShopHealthRecordsDto>
{
    /// <summary>Tối đa record mỗi lần POST.</summary>
    public const int MaxItems = 200;

    /// <summary>Initializes validation rules.</summary>
    public SyncTikTokShopHealthRecordsValidator()
    {
        RuleFor(x => x)
            .Must(x => !string.IsNullOrWhiteSpace(ResolveExternalShopId(x)))
            .WithMessage("externalShopId / sellerId không được để trống.");

        RuleFor(x => x.Items)
            .NotNull()
            .Must(p => p!.Count <= MaxItems)
            .WithMessage($"Tối đa {MaxItems} record mỗi lần sync.");

        RuleForEach(x => x.Items).ChildRules(item =>
        {
            item.RuleFor(p => p.RecordId)
                .NotEmpty().WithMessage("recordId không được để trống.")
                .MaximumLength(32);
            item.RuleFor(p => p.RecordType)
                .NotEmpty()
                .Must(t =>
                    string.Equals(t, TikTokShopHealthRecordTypes.Violation, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(t, TikTokShopHealthRecordTypes.Warning, StringComparison.OrdinalIgnoreCase))
                .WithMessage("recordType phải là violation hoặc warning.");
            item.RuleFor(p => p.Reason).MaximumLength(500).When(p => !string.IsNullOrEmpty(p.Reason));
            item.RuleFor(p => p.Detail).MaximumLength(2000).When(p => !string.IsNullOrEmpty(p.Detail));
            item.RuleFor(p => p.CategoryType).MaximumLength(128).When(p => !string.IsNullOrEmpty(p.CategoryType));
            item.RuleFor(p => p.Actions)
                .Must(list => list is null || list.Count <= 20)
                .WithMessage("Tối đa 20 action mỗi record.");
            item.RuleForEach(p => p.Actions).ChildRules(action =>
            {
                action.RuleFor(a => a.Title)
                    .NotEmpty().WithMessage("action title không được để trống.")
                    .MaximumLength(256);
            }).When(p => p.Actions is { Count: > 0 });
        });
    }

    /// <summary>Resolves TikTok seller id from either field.</summary>
    public static string ResolveExternalShopId(SyncTikTokShopHealthRecordsDto dto)
    {
        if (!string.IsNullOrWhiteSpace(dto.ExternalShopId))
        {
            return dto.ExternalShopId.Trim();
        }

        return (dto.SellerId ?? string.Empty).Trim();
    }
}
