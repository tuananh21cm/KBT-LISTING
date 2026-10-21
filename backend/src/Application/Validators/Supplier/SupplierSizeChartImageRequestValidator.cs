using Application.DTOs.Supplier;
using FluentValidation;

namespace Application.Validators.Supplier;

/// <summary>Validates supplier size chart image link requests.</summary>
public class SupplierSizeChartImageRequestValidator : AbstractValidator<SupplierSizeChartImageRequest>
{
    /// <summary>Creates validation rules.</summary>
    public SupplierSizeChartImageRequestValidator()
    {
        RuleFor(x => x.SourceUrl)
            .NotEmpty()
            .MaximumLength(2000);

        RuleFor(x => x.PublicUrl)
            .NotEmpty()
            .MaximumLength(2000)
            .Must(url => Uri.TryCreate(url, UriKind.Absolute, out var u)
                         && (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps))
            .WithMessage("PublicUrl phải là URL http(s) hợp lệ.");
    }
}
