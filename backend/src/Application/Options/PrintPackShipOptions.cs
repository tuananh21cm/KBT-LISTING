namespace Application.Options;

/// <summary>
/// PrintPackShip USPS track-scan settings (bound from PrintPackShip appsettings section).
/// </summary>
public class PrintPackShipOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "PrintPackShip";

    /// <summary>API base URL.</summary>
    public string BaseUrl { get; set; } = "https://label.printpackship.store/api/v1/";

    /// <summary>Bearer token — never log.</summary>
    public string BearerToken { get; set; } = string.Empty;

    /// <summary>Hard cap on tracking numbers per push.</summary>
    public int MaxTrackingPerBatch { get; set; } = 100;

    /// <summary>HTTP timeout seconds.</summary>
    public int TimeoutSeconds { get; set; } = 60;
}
