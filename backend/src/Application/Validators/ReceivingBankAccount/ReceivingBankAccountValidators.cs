using Application.DTOs.ReceivingBankAccount;
using FluentValidation;

namespace Application.Validators.ReceivingBankAccount;

/// <summary>Validator for create receiving bank account.</summary>
public class CreateReceivingBankAccountValidator : AbstractValidator<CreateReceivingBankAccountDto>
{
    /// <summary>Initializes validation rules.</summary>
    public CreateReceivingBankAccountValidator()
    {
        RuleFor(x => x.SourceId)
            .NotEmpty().WithMessage("Nguồn thẻ không được để trống.");

        RuleFor(x => x.Currency)
            .NotEmpty().WithMessage("Loại tiền không được để trống.")
            .MaximumLength(10);

        RuleFor(x => x.BankName)
            .NotEmpty().WithMessage("Tên ngân hàng không được để trống.")
            .MaximumLength(200);

        RuleFor(x => x.BankLocation).MaximumLength(100);

        RuleFor(x => x.AccountHolderName)
            .NotEmpty().WithMessage("Tên chủ tài khoản không được để trống.")
            .MaximumLength(200);

        RuleFor(x => x.AccountType).MaximumLength(50);
        RuleFor(x => x.SwiftBic).MaximumLength(20);
        RuleFor(x => x.RoutingNumber).MaximumLength(50);

        RuleFor(x => x.AccountNumber)
            .NotEmpty().WithMessage("Số tài khoản không được để trống.")
            .MaximumLength(100);

        RuleFor(x => x.BankAddress).MaximumLength(500);
    }
}

/// <summary>Validator for update receiving bank account.</summary>
public class UpdateReceivingBankAccountValidator : AbstractValidator<UpdateReceivingBankAccountDto>
{
    /// <summary>Initializes validation rules.</summary>
    public UpdateReceivingBankAccountValidator()
    {
        RuleFor(x => x.SourceId)
            .NotEmpty().WithMessage("Nguồn thẻ không được để trống.");

        RuleFor(x => x.Currency)
            .NotEmpty().WithMessage("Loại tiền không được để trống.")
            .MaximumLength(10);

        RuleFor(x => x.BankName)
            .NotEmpty().WithMessage("Tên ngân hàng không được để trống.")
            .MaximumLength(200);

        RuleFor(x => x.BankLocation).MaximumLength(100);

        RuleFor(x => x.AccountHolderName)
            .NotEmpty().WithMessage("Tên chủ tài khoản không được để trống.")
            .MaximumLength(200);

        RuleFor(x => x.AccountType).MaximumLength(50);
        RuleFor(x => x.SwiftBic).MaximumLength(20);
        RuleFor(x => x.RoutingNumber).MaximumLength(50);

        RuleFor(x => x.AccountNumber)
            .NotEmpty().WithMessage("Số tài khoản không được để trống.")
            .MaximumLength(100);

        RuleFor(x => x.BankAddress).MaximumLength(500);
    }
}

/// <summary>Validator for create source account.</summary>
public class CreateReceivingSourceAccountValidator : AbstractValidator<CreateReceivingSourceAccountDto>
{
    /// <summary>Initializes validation rules.</summary>
    public CreateReceivingSourceAccountValidator()
    {
        RuleFor(x => x.SourceId)
            .NotEmpty().WithMessage("Nguồn thẻ không được để trống.");

        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Tên tài khoản nguồn không được để trống.")
            .MaximumLength(200);

        RuleFor(x => x.ExternalAccountId).MaximumLength(200);
        RuleFor(x => x.Username).MaximumLength(200);
        RuleFor(x => x.Note).MaximumLength(500);
    }
}

/// <summary>Validator for update source account.</summary>
public class UpdateReceivingSourceAccountValidator : AbstractValidator<UpdateReceivingSourceAccountDto>
{
    /// <summary>Initializes validation rules.</summary>
    public UpdateReceivingSourceAccountValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Tên tài khoản nguồn không được để trống.")
            .MaximumLength(200);

        RuleFor(x => x.ExternalAccountId).MaximumLength(200);
        RuleFor(x => x.Username).MaximumLength(200);
        RuleFor(x => x.Note).MaximumLength(500);
    }
}

/// <summary>Create manual receiving-bank transaction validator.</summary>
public class CreateReceivingBankTransactionValidator : AbstractValidator<CreateReceivingBankTransactionDto>
{
    /// <summary>Rules.</summary>
    public CreateReceivingBankTransactionValidator()
    {
        RuleFor(x => x.ShopId).NotEmpty().WithMessage("Chọn shop.");
        RuleFor(x => x.Amount).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Direction)
            .Must(d => d is "Debit" or "Credit" or null or "")
            .WithMessage("Direction phải là Debit hoặc Credit.");
        RuleFor(x => x.Currency).MaximumLength(10);
        RuleFor(x => x.Note).MaximumLength(2000);
        RuleFor(x => x.ExternalTransactionId).MaximumLength(200);
    }
}

/// <summary>Update receiving-bank transaction validator.</summary>
public class UpdateReceivingBankTransactionValidator : AbstractValidator<UpdateReceivingBankTransactionDto>
{
    /// <summary>Rules.</summary>
    public UpdateReceivingBankTransactionValidator()
    {
        RuleFor(x => x.ShopId).NotEmpty().WithMessage("Chọn shop.");
        RuleFor(x => x.Amount).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Direction)
            .Must(d => d is "Debit" or "Credit" or null or "")
            .WithMessage("Direction phải là Debit hoặc Credit.");
        RuleFor(x => x.Currency).MaximumLength(10);
        RuleFor(x => x.Note).MaximumLength(2000);
        RuleFor(x => x.ExternalTransactionId).MaximumLength(200);
        RuleFor(x => x.OccurredAt).NotEmpty();
    }
}
