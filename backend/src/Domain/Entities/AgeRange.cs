using Domain.Common;

namespace Domain.Entities;

/// <summary>
/// Preset target-audience age bracket with marketing guidance (pain point, desire, product groups).
/// Shop still stores free-form <see cref="Shop.AgeMin"/> / <see cref="Shop.AgeMax"/> — no FK.
/// </summary>
public class AgeRange : BaseEntity
{
    /// <summary>Row identifier.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Stable code used by FE Select (e.g. <c>18-24</c>, <c>65-plus</c>).</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Display label (e.g. <c>18–24</c>, <c>65+</c>).</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>Inclusive lower bound mapped to <see cref="Shop.AgeMin"/>.</summary>
    public int AgeMin { get; set; }

    /// <summary>Inclusive upper bound mapped to <see cref="Shop.AgeMax"/> (65+ uses 100).</summary>
    public int AgeMax { get; set; }

    /// <summary>Main pain point for this age bracket.</summary>
    public string PainPoint { get; set; } = string.Empty;

    /// <summary>What the audience really wants.</summary>
    public string Desire { get; set; } = string.Empty;

    /// <summary>Recommended product groups to target.</summary>
    public string ProductGroups { get; set; } = string.Empty;

    /// <summary>Display order (ascending).</summary>
    public int SortOrder { get; set; }

    /// <summary>When false, hidden from Select API.</summary>
    public bool IsActive { get; set; } = true;
}
