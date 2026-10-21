using Application.CompanyLedger;
using Application.DTOs.CompanyLedger;
using Domain.Entities;
using FluentValidation;

namespace Application.Validators.CompanyLedger;

/// <summary>Validates a company ledger create/update body.</summary>
public class SaveCompanyLedgerEntryValidator : AbstractValidator<SaveCompanyLedgerEntryDto>
{
    /// <summary>Initializes rules.</summary>
    public SaveCompanyLedgerEntryValidator()
    {
        RuleFor(x => x.Amount).GreaterThan(0).WithMessage("Số tiền phải lớn hơn 0.");
        RuleFor(x => x.Title).MaximumLength(200);
        RuleFor(x => x.Note).MaximumLength(2000);
        RuleFor(x => x.OccurredAt).NotEqual(default(DateTime)).WithMessage("Chọn ngày phát sinh.");
        RuleFor(x => x.CategoryCode)
            .Must(CompanyLedgerCatalog.IsKnown)
            .WithMessage("Loại khoản thu/chi không hợp lệ.");
        RuleFor(x => x.Direction)
            .Must(CompanyLedgerCatalog.IsDirection)
            .WithMessage("Chiều thu/chi không hợp lệ.");
        RuleFor(x => x.Currency)
            .Must(c => CompanyLedgerCatalog.IsCurrency(string.IsNullOrWhiteSpace(c) ? "USD" : c.Trim().ToUpperInvariant()))
            .WithMessage("Tiền tệ chỉ nhận USD hoặc VND.");
    }
}

/// <summary>Validates the shop-finance acquisition editor. Null or 0 deletes the row.</summary>
public class UpsertShopAcquisitionCostValidator : AbstractValidator<UpsertShopAcquisitionCostDto>
{
    /// <summary>Initializes rules.</summary>
    public UpsertShopAcquisitionCostValidator()
    {
        RuleFor(x => x.Amount)
            .GreaterThanOrEqualTo(0)
            .When(x => x.Amount.HasValue)
            .WithMessage("Số tiền không được âm.");
        RuleFor(x => x.Note).MaximumLength(2000);
    }
}
