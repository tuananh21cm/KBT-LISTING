namespace Application.DTOs.Shop;

/// <summary>Preset audience age bracket for shop catalog Select.</summary>
public class AgeRangeDto
{
    /// <summary>Row id.</summary>
    public Guid Id { get; set; }

    /// <summary>Stable Select value (e.g. <c>18-24</c>).</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Display label (e.g. <c>18–24</c>).</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>Inclusive lower bound → Shop.AgeMin.</summary>
    public int AgeMin { get; set; }

    /// <summary>Inclusive upper bound → Shop.AgeMax.</summary>
    public int AgeMax { get; set; }

    /// <summary>Main pain point.</summary>
    public string PainPoint { get; set; } = string.Empty;

    /// <summary>What they really want.</summary>
    public string Desire { get; set; } = string.Empty;

    /// <summary>Recommended product groups.</summary>
    public string ProductGroups { get; set; } = string.Empty;

    /// <summary>Sort order ascending.</summary>
    public int SortOrder { get; set; }
}
