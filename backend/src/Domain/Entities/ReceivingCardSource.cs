using Domain.Common;

namespace Domain.Entities;

/// <summary>
/// Provider of receiving cards (PingPongX, WorldFirst, Other, …).
/// </summary>
public class ReceivingCardSource : BaseEntity
{
    /// <summary>Row identifier.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Unique code (PINGPONGX, WORLDFIRST, OTHER).</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Display name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Optional description.</summary>
    public string? Description { get; set; }

    /// <summary>
    /// When true, cards under this source should link a <see cref="ReceivingSourceAccount"/>
    /// (e.g. PingPongX / WorldFirst have many login accounts).
    /// </summary>
    public bool SupportsAccounts { get; set; }

    /// <summary>Whether selectable.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>Provider accounts under this source.</summary>
    public ICollection<ReceivingSourceAccount> SourceAccounts { get; set; } = new List<ReceivingSourceAccount>();

    /// <summary>Receiving bank cards from this source.</summary>
    public ICollection<ReceivingBankAccount> ReceivingBankAccounts { get; set; } = new List<ReceivingBankAccount>();

    /// <summary>Fulfil payment cards from this source.</summary>
    public ICollection<FulfilCard> FulfilCards { get; set; } = new List<FulfilCard>();
}
