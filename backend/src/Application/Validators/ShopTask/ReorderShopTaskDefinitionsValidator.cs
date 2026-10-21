using Application.DTOs.ShopTask;
using FluentValidation;

namespace Application.Validators.ShopTask;

/// <summary>Validator cho yêu cầu sắp xếp lại nhiệm vụ truy cập URL.</summary>
public class ReorderShopTaskDefinitionsValidator : AbstractValidator<ReorderShopTaskDefinitionsDto>
{
    /// <summary>Initializes validation rules.</summary>
    public ReorderShopTaskDefinitionsValidator()
    {
        RuleFor(x => x.OrderedIds)
            .NotEmpty().WithMessage("Danh sách sắp xếp không được để trống.")
            .Must(ids => ids.Distinct().Count() == ids.Count)
            .WithMessage("Danh sách sắp xếp không được chứa ID trùng.");
    }
}
