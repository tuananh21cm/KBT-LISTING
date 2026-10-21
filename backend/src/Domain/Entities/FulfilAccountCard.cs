namespace Domain.Entities;

/// <summary>
/// Many-to-many link: a payment card can be shared by multiple fulfil purchase accounts.
/// </summary>
public class FulfilAccountCard
{
    /// <summary>Fulfil account id.</summary>
    public Guid FulfilAccountId { get; set; }

    /// <summary>Payment card id.</summary>
    public Guid FulfilCardId { get; set; }

    /// <summary>Account navigation.</summary>
    public FulfilAccount FulfilAccount { get; set; } = null!;

    /// <summary>Card navigation.</summary>
    public FulfilCard FulfilCard { get; set; } = null!;
}
