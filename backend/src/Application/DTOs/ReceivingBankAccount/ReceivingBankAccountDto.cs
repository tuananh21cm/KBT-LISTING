namespace Application.DTOs.ReceivingBankAccount;

/// <summary>
/// Slim list row for catalog table — no bank SWIFT/routing/address details.
/// Full fields via <see cref="ReceivingBankAccountDto"/> / GET by id.
/// </summary>
public class ReceivingBankAccountListItemDto
{
    /// <summary>Id.</summary>
    public Guid Id { get; set; }

    /// <summary>Provider source id.</summary>
    public Guid SourceId { get; set; }

    /// <summary>Provider source name.</summary>
    public string SourceName { get; set; } = string.Empty;

    /// <summary>Provider account id (optional).</summary>
    public Guid? SourceAccountId { get; set; }

    /// <summary>Provider account name.</summary>
    public string? SourceAccountName { get; set; }

    /// <summary>Account currency.</summary>
    public string Currency { get; set; } = "USD";

    /// <summary>Account holder's name.</summary>
    public string AccountHolderName { get; set; } = string.Empty;

    /// <summary>Bank account number.</summary>
    public string AccountNumber { get; set; } = string.Empty;

    /// <summary>Whether selectable.</summary>
    public bool IsActive { get; set; }

    /// <summary>Number of shops linked to this account.</summary>
    public int ShopCount { get; set; }

    /// <summary>Linked shops (Name · ProfileName for UI).</summary>
    public List<ReceivingBankLinkedShopDto> Shops { get; set; } = [];
}

/// <summary>Receiving bank account (thẻ nhận tiền) DTO.</summary>
public class ReceivingBankAccountDto
{
    /// <summary>Id.</summary>
    public Guid Id { get; set; }

    /// <summary>Display name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Provider source id.</summary>
    public Guid SourceId { get; set; }

    /// <summary>Provider source code.</summary>
    public string SourceCode { get; set; } = string.Empty;

    /// <summary>Provider source name.</summary>
    public string SourceName { get; set; } = string.Empty;

    /// <summary>Provider account id (optional).</summary>
    public Guid? SourceAccountId { get; set; }

    /// <summary>Provider account name.</summary>
    public string? SourceAccountName { get; set; }

    /// <summary>Account currency.</summary>
    public string Currency { get; set; } = "USD";

    /// <summary>Bank name.</summary>
    public string BankName { get; set; } = string.Empty;

    /// <summary>Bank location.</summary>
    public string? BankLocation { get; set; }

    /// <summary>Account holder's name.</summary>
    public string AccountHolderName { get; set; } = string.Empty;

    /// <summary>Account type.</summary>
    public string? AccountType { get; set; }

    /// <summary>SWIFT / BIC.</summary>
    public string? SwiftBic { get; set; }

    /// <summary>Routing number.</summary>
    public string? RoutingNumber { get; set; }

    /// <summary>Bank account number.</summary>
    public string AccountNumber { get; set; } = string.Empty;

    /// <summary>Bank address.</summary>
    public string? BankAddress { get; set; }

    /// <summary>Whether selectable.</summary>
    public bool IsActive { get; set; }

    /// <summary>Number of shops linked to this account.</summary>
    public int ShopCount { get; set; }

    /// <summary>Linked shops (Name · ProfileName for UI).</summary>
    public List<ReceivingBankLinkedShopDto> Shops { get; set; } = [];

    /// <summary>Created.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>Updated.</summary>
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>Shop linked to a receiving bank account.</summary>
public class ReceivingBankLinkedShopDto
{
    /// <summary>Shop id.</summary>
    public Guid Id { get; set; }

    /// <summary>Shop name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Profile name.</summary>
    public string? ProfileName { get; set; }
}

/// <summary>Create receiving bank account.</summary>
public class CreateReceivingBankAccountDto
{
    /// <summary>Provider source id.</summary>
    public Guid SourceId { get; set; }

    /// <summary>Provider account id (required when source supports accounts).</summary>
    public Guid? SourceAccountId { get; set; }

    /// <summary>Account currency.</summary>
    public string Currency { get; set; } = "USD";

    /// <summary>Bank name.</summary>
    public string BankName { get; set; } = string.Empty;

    /// <summary>Bank location.</summary>
    public string? BankLocation { get; set; }

    /// <summary>Account holder's name.</summary>
    public string AccountHolderName { get; set; } = string.Empty;

    /// <summary>Account type.</summary>
    public string? AccountType { get; set; }

    /// <summary>SWIFT / BIC.</summary>
    public string? SwiftBic { get; set; }

    /// <summary>Routing number.</summary>
    public string? RoutingNumber { get; set; }

    /// <summary>Bank account number.</summary>
    public string AccountNumber { get; set; } = string.Empty;

    /// <summary>Bank address.</summary>
    public string? BankAddress { get; set; }

    /// <summary>Whether selectable.</summary>
    public bool IsActive { get; set; } = true;
}

/// <summary>Update receiving bank account.</summary>
public class UpdateReceivingBankAccountDto
{
    /// <summary>Provider source id.</summary>
    public Guid SourceId { get; set; }

    /// <summary>Provider account id (required when source supports accounts).</summary>
    public Guid? SourceAccountId { get; set; }

    /// <summary>Account currency.</summary>
    public string Currency { get; set; } = "USD";

    /// <summary>Bank name.</summary>
    public string BankName { get; set; } = string.Empty;

    /// <summary>Bank location.</summary>
    public string? BankLocation { get; set; }

    /// <summary>Account holder's name.</summary>
    public string AccountHolderName { get; set; } = string.Empty;

    /// <summary>Account type.</summary>
    public string? AccountType { get; set; }

    /// <summary>SWIFT / BIC.</summary>
    public string? SwiftBic { get; set; }

    /// <summary>Routing number.</summary>
    public string? RoutingNumber { get; set; }

    /// <summary>Bank account number.</summary>
    public string AccountNumber { get; set; } = string.Empty;

    /// <summary>Bank address.</summary>
    public string? BankAddress { get; set; }

    /// <summary>Whether selectable.</summary>
    public bool IsActive { get; set; } = true;
}

/// <summary>Dropdown option for shop linking / stats filters.</summary>
public class ReceivingBankAccountOptionDto
{
    /// <summary>Id.</summary>
    public Guid Id { get; set; }

    /// <summary>Name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Provider source id (cascade filters).</summary>
    public Guid SourceId { get; set; }

    /// <summary>Source name.</summary>
    public string SourceName { get; set; } = string.Empty;

    /// <summary>Provider account id (cascade filters).</summary>
    public Guid? SourceAccountId { get; set; }

    /// <summary>Source account name.</summary>
    public string? SourceAccountName { get; set; }

    /// <summary>Currency.</summary>
    public string Currency { get; set; } = "USD";

    /// <summary>Account holder.</summary>
    public string AccountHolderName { get; set; } = string.Empty;

    /// <summary>Account number.</summary>
    public string AccountNumber { get; set; } = string.Empty;

    /// <summary>Bank name.</summary>
    public string BankName { get; set; } = string.Empty;

    /// <summary>Whether active.</summary>
    public bool IsActive { get; set; }
}
