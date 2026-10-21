using Domain.Common;

namespace Domain.Entities;

/// <summary>
/// Global receiving bank account (thẻ nhận tiền) that shops can connect to.
/// One shop links at most one account; one account may be shared by many shops.
/// </summary>
public class ReceivingBankAccount : BaseEntity
{
    /// <summary>Row identifier.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Admin display label (e.g. "Deutsche Bank USD — Bay").</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Provider source (PingPongX, WorldFirst, …).</summary>
    public Guid SourceId { get; set; }

    /// <summary>
    /// Optional provider login/account under the source
    /// (required when source <c>SupportsAccounts</c>).
    /// </summary>
    public Guid? SourceAccountId { get; set; }

    /// <summary>Account currency (e.g. USD).</summary>
    public string Currency { get; set; } = "USD";

    /// <summary>Bank name.</summary>
    public string BankName { get; set; } = string.Empty;

    /// <summary>Bank location / country.</summary>
    public string? BankLocation { get; set; }

    /// <summary>Account holder's name.</summary>
    public string AccountHolderName { get; set; } = string.Empty;

    /// <summary>Account type (Checking, Savings, …).</summary>
    public string? AccountType { get; set; }

    /// <summary>SWIFT / BIC code.</summary>
    public string? SwiftBic { get; set; }

    /// <summary>ABA routing number.</summary>
    public string? RoutingNumber { get; set; }

    /// <summary>Bank account number.</summary>
    public string AccountNumber { get; set; } = string.Empty;

    /// <summary>Bank street / address line.</summary>
    public string? BankAddress { get; set; }

    /// <summary>Whether the account can be selected by shops.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>Provider source.</summary>
    public ReceivingCardSource Source { get; set; } = null!;

    /// <summary>Provider login/account (optional).</summary>
    public ReceivingSourceAccount? SourceAccount { get; set; }

    /// <summary>Shops linked to this receiving account.</summary>
    public ICollection<Shop> Shops { get; set; } = new List<Shop>();

    /// <summary>Inbound / adjustment ledger rows for this card.</summary>
    public ICollection<ReceivingBankTransaction> Transactions { get; set; } = new List<ReceivingBankTransaction>();
}
