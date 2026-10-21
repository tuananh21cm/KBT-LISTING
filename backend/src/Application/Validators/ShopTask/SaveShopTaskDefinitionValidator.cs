using Application.DTOs.ShopTask;
using FluentValidation;
using Shared.Constants;

namespace Application.Validators.ShopTask;

/// <summary>
/// Validator cho nhiệm vụ truy cập URL. Host bị giới hạn theo allowlist để URL luôn nằm trong
/// <c>host_permissions</c> của extension — nếu không extension sẽ không đọc được URL sau redirect.
/// </summary>
public class SaveShopTaskDefinitionValidator : AbstractValidator<SaveShopTaskDefinitionDto>
{
    /// <summary>Initializes validation rules.</summary>
    public SaveShopTaskDefinitionValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Tên nhiệm vụ không được để trống.")
            .MaximumLength(200);

        RuleFor(x => x.Url)
            .NotEmpty().WithMessage("URL không được để trống.")
            .MaximumLength(1000)
            .Must(BeAllowedUrl)
            .WithMessage($"URL phải là https và thuộc: {string.Join(", ", ShopTaskUrlHosts.Allowed)}.");

        RuleFor(x => x.UrlMatchPattern)
            .MaximumLength(1000)
            .Must(BeAllowedUrl)
            .When(x => !string.IsNullOrWhiteSpace(x.UrlMatchPattern))
            .WithMessage("Pattern so khớp phải là URL https thuộc cùng allowlist.");

        RuleFor(x => x.Description).MaximumLength(500);

        RuleFor(x => x.ViewDurationSeconds)
            .InclusiveBetween(0, 600)
            .WithMessage("Thời gian xem trang phải từ 0 đến 600 giây.");

        RuleFor(x => x.SortOrder).GreaterThanOrEqualTo(0);
    }

    private static bool BeAllowedUrl(string? value)
    {
        if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri))
        {
            return false;
        }

        return uri.Scheme == Uri.UriSchemeHttps
               && ShopTaskUrlHosts.Allowed.Contains(uri.Host, StringComparer.OrdinalIgnoreCase);
    }
}
