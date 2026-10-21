using Application.DTOs.Fulfil;
using FluentValidation;

namespace Application.Validators.Fulfil;

/// <summary>Create fulfil account validator.</summary>
public class CreateFulfilAccountValidator : AbstractValidator<CreateFulfilAccountDto>
{
    /// <summary>Rules.</summary>
    public CreateFulfilAccountValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Username).MaximumLength(200);
        RuleFor(x => x.Password).MaximumLength(200);
        RuleFor(x => x.Email).MaximumLength(320).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email));
        RuleFor(x => x.PassMail).MaximumLength(200);
        RuleFor(x => x.TwoFactorAuth).MaximumLength(500);
        RuleFor(x => x.SheinProxy).MaximumLength(200);
    }
}

/// <summary>Update fulfil account validator.</summary>
public class UpdateFulfilAccountValidator : AbstractValidator<UpdateFulfilAccountDto>
{
    /// <summary>Rules.</summary>
    public UpdateFulfilAccountValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Username).MaximumLength(200);
        RuleFor(x => x.Password).MaximumLength(200);
        RuleFor(x => x.Email).MaximumLength(320).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email));
        RuleFor(x => x.PassMail).MaximumLength(200);
        RuleFor(x => x.TwoFactorAuth).MaximumLength(500);
        RuleFor(x => x.SheinProxy).MaximumLength(200);
    }
}

/// <summary>SHEIN session push validator.</summary>
public class SaveSheinSessionValidator : AbstractValidator<SaveSheinSessionDto>
{
    /// <summary>Rules.</summary>
    public SaveSheinSessionValidator()
    {
        RuleFor(x => x.AccountEmail)
            .NotEmpty()
            .WithMessage("Thiếu email tài khoản SHEIN để gắn session.")
            .When(x => x.AccountId is null || x.AccountId == Guid.Empty);
        RuleFor(x => x.AccountEmail).MaximumLength(320);
        RuleFor(x => x.Origin).MaximumLength(100);
        RuleFor(x => x.Headers).NotEmpty().WithMessage("Chưa bắt được header SHEIN nào.");
    }
}

/// <summary>Create fulfil card validator.</summary>
public class CreateFulfilCardValidator : AbstractValidator<CreateFulfilCardDto>
{
    /// <summary>Rules.</summary>
    public CreateFulfilCardValidator()
    {
        RuleFor(x => x.Name).MaximumLength(200);
        RuleFor(x => x.CardNumber).NotEmpty().MaximumLength(32);
        RuleFor(x => x.CardBrand).NotEmpty().MaximumLength(20);
        RuleFor(x => x.ExpiryMonth).InclusiveBetween(1, 12).When(x => x.ExpiryMonth.HasValue);
        RuleFor(x => x.ExpiryYear).InclusiveBetween(2000, 2100).When(x => x.ExpiryYear.HasValue);
        RuleFor(x => x.HolderName).MaximumLength(200);
        RuleFor(x => x.Cvv).MaximumLength(10);
        RuleFor(x => x.Currency).NotEmpty().MaximumLength(10);
        RuleFor(x => x.SourceId).NotEmpty();
    }
}

/// <summary>Update fulfil card validator.</summary>
public class UpdateFulfilCardValidator : AbstractValidator<UpdateFulfilCardDto>
{
    /// <summary>Rules.</summary>
    public UpdateFulfilCardValidator()
    {
        RuleFor(x => x.Name).MaximumLength(200);
        RuleFor(x => x.CardNumber).NotEmpty().MaximumLength(32);
        RuleFor(x => x.CardBrand).NotEmpty().MaximumLength(20);
        RuleFor(x => x.ExpiryMonth).InclusiveBetween(1, 12).When(x => x.ExpiryMonth.HasValue);
        RuleFor(x => x.ExpiryYear).InclusiveBetween(2000, 2100).When(x => x.ExpiryYear.HasValue);
        RuleFor(x => x.HolderName).MaximumLength(200);
        RuleFor(x => x.Cvv).MaximumLength(10);
        RuleFor(x => x.Currency).NotEmpty().MaximumLength(10);
        RuleFor(x => x.SourceId).NotEmpty();
    }
}

/// <summary>Create manual transaction validator.</summary>
public class CreateFulfilTransactionValidator : AbstractValidator<CreateFulfilTransactionDto>
{
    /// <summary>Rules.</summary>
    public CreateFulfilTransactionValidator()
    {
        RuleFor(x => x.Amount).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Direction)
            .Must(d => d is "Debit" or "Credit" or null or "")
            .WithMessage("Direction phải là Debit hoặc Credit.");
        RuleFor(x => x.Currency).MaximumLength(10);
        RuleFor(x => x.Note).MaximumLength(2000);
    }
}

/// <summary>Update transaction validator.</summary>
public class UpdateFulfilTransactionValidator : AbstractValidator<UpdateFulfilTransactionDto>
{
    /// <summary>Rules.</summary>
    public UpdateFulfilTransactionValidator()
    {
        RuleFor(x => x.Amount).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Direction)
            .Must(d => d is "Debit" or "Credit" or null or "")
            .WithMessage("Direction phải là Debit hoặc Credit.");
        RuleFor(x => x.Currency).MaximumLength(10);
        RuleFor(x => x.Note).MaximumLength(2000);
        RuleFor(x => x.OccurredAt).NotEmpty();
    }
}
