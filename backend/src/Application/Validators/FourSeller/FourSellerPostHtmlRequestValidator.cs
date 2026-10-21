using Application.DTOs.FourSeller;
using Application.Services;
using FluentValidation;

namespace Application.Validators.FourSeller;

/// <summary>
/// Validator for extension postHtml proxy requests.
/// </summary>
public class FourSellerPostHtmlRequestValidator : AbstractValidator<FourSellerPostHtmlRequest>
{
    /// <summary>
    /// Initializes validation rules.
    /// </summary>
    public FourSellerPostHtmlRequestValidator()
    {
        RuleFor(x => x.Url)
            .NotEmpty().WithMessage("URL sản phẩm không được để trống.")
            .MaximumLength(2048);

        RuleFor(x => x.Html)
            .NotEmpty().WithMessage("HTML trang sản phẩm không được để trống.")
            .MinimumLength(200).WithMessage("Không lấy được nội dung trang sản phẩm.")
            .MaximumLength(40_000_000);

        RuleFor(x => x.ShopIdList)
            .NotEmpty().WithMessage("Chọn ít nhất 1 shop trước khi thêm sản phẩm.")
            .Must(ids => ids.All(id => id > 0))
            .WithMessage("shopIdList chứa id không hợp lệ.");

        RuleFor(x => x.Site).MaximumLength(64);
        RuleForEach(x => x.SiteList).MaximumLength(64);

        RuleFor(x => x.AddedFrom)
            .Must(v => ListingAddOriginApply.Normalize(v) is not null)
            .When(x => !string.IsNullOrWhiteSpace(x.AddedFrom))
            .WithMessage("addedFrom chỉ nhận recommended hoặc manual.");
    }
}
