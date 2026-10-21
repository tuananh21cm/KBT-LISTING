using Application.DTOs.Shop;
using FluentValidation;

namespace Application.Validators.Shop;

/// <summary>
/// Validator for admin shop reorder requests.
/// </summary>
public class ReorderShopsValidator : AbstractValidator<ReorderShopsDto>
{
    /// <summary>Initializes validation rules.</summary>
    public ReorderShopsValidator()
    {
        RuleFor(x => x.OrderedIds)
            .NotEmpty().WithMessage("Danh sách sắp xếp không được để trống.")
            .Must(ids => ids.Distinct().Count() == ids.Count)
            .WithMessage("Danh sách sắp xếp không được chứa ID trùng.");
    }
}
