namespace Application.DTOs.Fulfil;

/// <summary>Fulfil payment card DTO.</summary>
public class FulfilCardDto
{
    /// <summary>Id.</summary>
    public Guid Id { get; set; }

    /// <summary>Display name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Full card number.</summary>
    public string CardNumber { get; set; } = string.Empty;

    /// <summary>Masked card number for lists.</summary>
    public string CardNumberMasked { get; set; } = string.Empty;

    /// <summary>Visa / Master.</summary>
    public string CardBrand { get; set; } = "Visa";

    /// <summary>Expiry month.</summary>
    public int? ExpiryMonth { get; set; }

    /// <summary>Expiry year.</summary>
    public int? ExpiryYear { get; set; }

    /// <summary>Holder name.</summary>
    public string? HolderName { get; set; }

    /// <summary>CVV.</summary>
    public string? Cvv { get; set; }

    /// <summary>Card billing currency (USD, EUR, …).</summary>
    public string Currency { get; set; } = "USD";

    /// <summary>Provider source id.</summary>
    public Guid SourceId { get; set; }

    /// <summary>Provider source name.</summary>
    public string SourceName { get; set; } = string.Empty;

    /// <summary>Provider source code.</summary>
    public string SourceCode { get; set; } = string.Empty;

    /// <summary>Provider source account id (when source supports accounts).</summary>
    public Guid? SourceAccountId { get; set; }

    /// <summary>Provider source account name.</summary>
    public string? SourceAccountName { get; set; }

    /// <summary>Linked fulfil account ids.</summary>
    public List<Guid> AccountIds { get; set; } = [];

    /// <summary>Linked account names.</summary>
    public List<string> AccountNames { get; set; } = [];

    /// <summary>Assignee user ids (cấp cho nhiều người).</summary>
    public List<Guid> AssigneeUserIds { get; set; } = [];

    /// <summary>Assignee display names.</summary>
    public List<string> AssigneeUserNames { get; set; } = [];

    /// <summary>Whether the card is enabled.</summary>
    public bool IsActive { get; set; }

    /// <summary>Orders that used this card.</summary>
    public int OrderCount { get; set; }

    /// <summary>Created.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>Updated.</summary>
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>Create fulfil card.</summary>
public class CreateFulfilCardDto
{
    /// <summary>Display name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Card number.</summary>
    public string CardNumber { get; set; } = string.Empty;

    /// <summary>Visa / Master.</summary>
    public string CardBrand { get; set; } = "Visa";

    /// <summary>Expiry month.</summary>
    public int? ExpiryMonth { get; set; }

    /// <summary>Expiry year.</summary>
    public int? ExpiryYear { get; set; }

    /// <summary>Holder name.</summary>
    public string? HolderName { get; set; }

    /// <summary>CVV.</summary>
    public string? Cvv { get; set; }

    /// <summary>Card billing currency (USD, EUR, …).</summary>
    public string Currency { get; set; } = "USD";

    /// <summary>Provider source id (PingPongX / WorldFirst / …).</summary>
    public Guid SourceId { get; set; }

    /// <summary>Provider source account id (required when source supports accounts).</summary>
    public Guid? SourceAccountId { get; set; }

    /// <summary>Linked fulfil account ids.</summary>
    public List<Guid> AccountIds { get; set; } = [];

    /// <summary>Assignee user ids.</summary>
    public List<Guid> AssigneeUserIds { get; set; } = [];

    /// <summary>Whether the card is enabled.</summary>
    public bool IsActive { get; set; } = true;
}

/// <summary>Update fulfil card.</summary>
public class UpdateFulfilCardDto
{
    /// <summary>Display name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Card number.</summary>
    public string CardNumber { get; set; } = string.Empty;

    /// <summary>Visa / Master.</summary>
    public string CardBrand { get; set; } = "Visa";

    /// <summary>Expiry month.</summary>
    public int? ExpiryMonth { get; set; }

    /// <summary>Expiry year.</summary>
    public int? ExpiryYear { get; set; }

    /// <summary>Holder name.</summary>
    public string? HolderName { get; set; }

    /// <summary>CVV.</summary>
    public string? Cvv { get; set; }

    /// <summary>Card billing currency (USD, EUR, …).</summary>
    public string Currency { get; set; } = "USD";

    /// <summary>Provider source id (PingPongX / WorldFirst / …).</summary>
    public Guid SourceId { get; set; }

    /// <summary>Provider source account id (required when source supports accounts).</summary>
    public Guid? SourceAccountId { get; set; }

    /// <summary>Linked fulfil account ids.</summary>
    public List<Guid> AccountIds { get; set; } = [];

    /// <summary>Assignee user ids.</summary>
    public List<Guid> AssigneeUserIds { get; set; } = [];

    /// <summary>Whether the card is enabled.</summary>
    public bool IsActive { get; set; } = true;
}

/// <summary>Dropdown option for cards.</summary>
public class FulfilCardOptionDto
{
    /// <summary>Id.</summary>
    public Guid Id { get; set; }

    /// <summary>Name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Masked number.</summary>
    public string CardNumberMasked { get; set; } = string.Empty;

    /// <summary>Brand.</summary>
    public string CardBrand { get; set; } = "Visa";

    /// <summary>Card billing currency.</summary>
    public string Currency { get; set; } = "USD";

    /// <summary>Provider source id.</summary>
    public Guid? SourceId { get; set; }

    /// <summary>Provider source name.</summary>
    public string? SourceName { get; set; }

    /// <summary>Provider source account id.</summary>
    public Guid? SourceAccountId { get; set; }

    /// <summary>Provider source account name.</summary>
    public string? SourceAccountName { get; set; }

    /// <summary>Whether the card is enabled.</summary>
    public bool IsActive { get; set; }

    /// <summary>True when this is the account's preferred ActiveCardId.</summary>
    public bool IsPreferredForAccount { get; set; }
}
