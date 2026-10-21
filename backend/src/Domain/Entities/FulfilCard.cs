using Domain.Common;

namespace Domain.Entities;

/// <summary>
/// Visa/Master payment card used for fulfil purchases.
/// A card may link many fulfil accounts and assignees; shop is chosen per order at fulfil time.
/// </summary>
public class FulfilCard : BaseEntity
{
    /// <summary>Row identifier.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Card display name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Full card number (stored like shop secrets).</summary>
    public string CardNumber { get; set; } = string.Empty;

    /// <summary>Visa or Master.</summary>
    public string CardBrand { get; set; } = "Visa";

    /// <summary>Expiry month (1–12).</summary>
    public int? ExpiryMonth { get; set; }

    /// <summary>Expiry year (e.g. 2028).</summary>
    public int? ExpiryYear { get; set; }

    /// <summary>Cardholder name.</summary>
    public string? HolderName { get; set; }

    /// <summary>CVV / security code.</summary>
    public string? Cvv { get; set; }

    /// <summary>Card billing currency (e.g. USD).</summary>
    public string Currency { get; set; } = "USD";

    /// <summary>Provider source (PingPongX, WorldFirst, …).</summary>
    public Guid SourceId { get; set; }

    /// <summary>
    /// Optional provider login/account under the source
    /// (required when source <c>SupportsAccounts</c>).
    /// </summary>
    public Guid? SourceAccountId { get; set; }

    /// <summary>Whether the card is usable (enabled).</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>Provider source.</summary>
    public ReceivingCardSource Source { get; set; } = null!;

    /// <summary>Provider login/account (optional).</summary>
    public ReceivingSourceAccount? SourceAccount { get; set; }

    /// <summary>Account links.</summary>
    public ICollection<FulfilAccountCard> AccountLinks { get; set; } = new List<FulfilAccountCard>();

    /// <summary>Users this card is assigned to.</summary>
    public ICollection<FulfilCardAssignee> Assignees { get; set; } = new List<FulfilCardAssignee>();

    /// <summary>Transactions charged on this card.</summary>
    public ICollection<FulfilTransaction> Transactions { get; set; } = new List<FulfilTransaction>();
}
