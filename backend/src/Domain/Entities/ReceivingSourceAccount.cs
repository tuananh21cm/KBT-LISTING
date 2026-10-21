using Domain.Common;

namespace Domain.Entities;

/// <summary>
/// Login / merchant account under a receiving card source
/// (e.g. one of many PingPongX accounts).
/// </summary>
public class ReceivingSourceAccount : BaseEntity
{
    /// <summary>Row identifier.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Parent source (PingPongX, WorldFirst, …).</summary>
    public Guid SourceId { get; set; }

    /// <summary>Display name (e.g. "PingPong — Bay US").</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>External / provider account id or email (optional).</summary>
    public string? ExternalAccountId { get; set; }

    /// <summary>Login username / email (optional).</summary>
    public string? Username { get; set; }

    /// <summary>Optional note.</summary>
    public string? Note { get; set; }

    /// <summary>Whether selectable when creating cards.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>Parent source.</summary>
    public ReceivingCardSource Source { get; set; } = null!;

    /// <summary>Receiving bank cards linked to this provider account.</summary>
    public ICollection<ReceivingBankAccount> ReceivingBankAccounts { get; set; } = new List<ReceivingBankAccount>();

    /// <summary>Fulfil payment cards linked to this provider account.</summary>
    public ICollection<FulfilCard> FulfilCards { get; set; } = new List<FulfilCard>();
}
