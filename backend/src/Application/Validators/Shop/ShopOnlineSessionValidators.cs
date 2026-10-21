using Application.DTOs.Shop;
using FluentValidation;

namespace Application.Validators.Shop;

/// <summary>Validator for online-session heartbeat.</summary>
public class ShopOnlineHeartbeatValidator : AbstractValidator<ShopOnlineHeartbeatDto>
{
    /// <summary>Initializes validation rules.</summary>
    public ShopOnlineHeartbeatValidator()
    {
        RuleFor(x => x.ExternalShopId)
            .NotEmpty().WithMessage("externalShopId không được để trống.")
            .MaximumLength(64);

        RuleFor(x => x.ClientSessionId)
            .NotEmpty().WithMessage("clientSessionId không được để trống.")
            .MaximumLength(64);

        RuleFor(x => x.StartedAt)
            .Must(t => t != default)
            .WithMessage("startedAt không hợp lệ.");

        RuleFor(x => x.LastSeenAt)
            .Must(t => t != default)
            .WithMessage("lastSeenAt không hợp lệ.")
            .Must((dto, last) => last >= dto.StartedAt.AddMinutes(-1))
            .WithMessage("lastSeenAt không được sớm hơn startedAt.");
    }
}

/// <summary>Validator for online-session close.</summary>
public class ShopOnlineCloseValidator : AbstractValidator<ShopOnlineCloseDto>
{
    /// <summary>Initializes validation rules.</summary>
    public ShopOnlineCloseValidator()
    {
        RuleFor(x => x.ExternalShopId)
            .NotEmpty().WithMessage("externalShopId không được để trống.")
            .MaximumLength(64);

        RuleFor(x => x.ClientSessionId)
            .NotEmpty().WithMessage("clientSessionId không được để trống.")
            .MaximumLength(64);

        RuleFor(x => x.EndedAt)
            .Must(t => t != default)
            .WithMessage("endedAt không hợp lệ.");
    }
}
