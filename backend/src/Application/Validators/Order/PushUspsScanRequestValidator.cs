using Application.DTOs.Order;
using FluentValidation;

namespace Application.Validators.Order;

/// <summary>Validates bulk USPS scan push request.</summary>
public class PushUspsScanRequestValidator : AbstractValidator<PushUspsScanRequest>
{
    /// <summary>Creates the validator.</summary>
    public PushUspsScanRequestValidator()
    {
        RuleFor(x => x.OrderIds)
            .NotEmpty()
            .WithMessage("Chọn ít nhất một đơn.")
            .Must(ids => ids.Count <= 200)
            .WithMessage("Mỗi lần đẩy tối đa 200 đơn.");

        RuleFor(x => x.Notes)
            .MaximumLength(500)
            .When(x => x.Notes is not null);
    }
}
